using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class AppleWatchDemoSceneTests
{
    private string folder;
    private SceneSetup[] originalScenes;
    private EditorBuildSettingsScene[] originalBuildScenes;

    [SetUp]
    public void SetUp()
    {
        originalScenes = EditorSceneManager.GetSceneManagerSetup();
        originalBuildScenes = EditorBuildSettings.scenes;
        var name = "WatchDemoTest_" + Guid.NewGuid().ToString("N");
        folder = "Assets/" + name;
        AssetDatabase.CreateFolder("Assets", name);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, folder + "/Before.unity");
    }

    [TearDown]
    public void TearDown()
    {
        EditorBuildSettings.scenes = originalBuildScenes;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(folder);
        if (originalScenes.Length > 0 && originalScenes.All(scene => !string.IsNullOrEmpty(scene.path)))
            EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
    }

    [Test]
    public void CreateCommandSavesACompletePlayableDemoWithoutReplacingBuildScenes()
    {
        InvokeCreate(folder + "/Demo.unity", false);
        var scene = SceneManager.GetActiveScene();
        Assert.That(scene.path, Is.EqualTo(folder + "/Demo.unity"));
        Assert.That(File.Exists(scene.path), Is.True);
        var components = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true)).ToArray();
        Assert.That(components.Count(component => component is Camera), Is.EqualTo(1));
        Assert.That(components.Count(component => component is WatchInputReceiver), Is.EqualTo(1));
        Assert.That(components.Count(component => component is AudioSource), Is.EqualTo(2));
        var orientationTip = components.OfType<Transform>().SingleOrDefault(item => item.name == "Orientation Tip");
        Assert.That(orientationTip, Is.Not.Null, "A symmetric three-bar indicator cannot make quarter-turn orientation changes clear.");
        Assert.That(orientationTip.localPosition.x * orientationTip.localPosition.y * orientationTip.localPosition.z, Is.Not.Zero,
            "The marker must be off-axis so roll, pitch and yaw are visually distinguishable.");
        Assert.That(orientationTip.GetComponent<Collider>(), Is.Null);
        foreach (var typeName in new[] { "AppleWatchDemoController", "AppleWatchDemoGame", "AppleWatchDemoAudio", "AppleWatchDemoHud" })
            Assert.That(components.Count(component => component != null && component.GetType().Name == typeName), Is.EqualTo(1), typeName);
        var document = components.OfType<UIDocument>().Single();
        Assert.That(document.visualTreeAsset, Is.Not.Null);
        Assert.That(document.panelSettings, Is.Not.Null);
        Assert.That(EditorBuildSettings.scenes.Select(item => item.path), Is.EqualTo(originalBuildScenes.Select(item => item.path)));
        new GameObject("User-added marker");
        EditorSceneManager.SaveScene(scene);
        InvokeCreate(scene.path, false);
        Assert.That(GameObject.Find("User-added marker"), Is.Not.Null, "Reopening a generated demo must not overwrite scene edits.");
    }

    [Test]
    public void ProbeRefusesToInjectInputOutsidePlayMode()
    {
        var type = Type.GetType("AppleWatchController.Samples.TargetPop.Editor.AppleWatchDemoProbe, AppleWatchController.Samples.TargetPop.Editor");
        Assert.That(type, Is.Not.Null, "Demo verification must use a reusable Unity CLI command.");
        var task = (Task)type.GetMethod("SimulateAsync").Invoke(null, new object[] { "Space", 0.2f, false });
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception.GetBaseException(), Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void UiProbeRefusesToInvokeButtonsOutsidePlayMode()
    {
        var type = Type.GetType("AppleWatchController.Samples.TargetPop.Editor.AppleWatchDemoProbe, AppleWatchController.Samples.TargetPop.Editor");
        var method = type.GetMethod("UiActionAsync");
        Assert.That(method, Is.Not.Null, "The runtime HUD must be testable through the project CLI.");
        var task = (Task)method.Invoke(null, new object[] { "test-sound" });
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception.GetBaseException(), Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void PoseProbeIsRegisteredAndRefusesToChangeEditModeScene()
    {
        var method = PoseProbeMethod();
        Assert.That(method.DeclaringType.Assembly.GetName().Name, Is.EqualTo("AppleWatchController.Samples.TargetPop.Editor"));
        var task = (Task)method.Invoke(null, new object[] { true, 0f, 0f, 0f });
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception.GetBaseException(), Is.TypeOf<InvalidOperationException>());
        Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
    }

    [TestCase(float.NaN, 0f, 0f)]
    [TestCase(0f, float.NaN, 0f)]
    [TestCase(0f, 0f, float.NaN)]
    [TestCase(float.PositiveInfinity, 0f, 0f)]
    [TestCase(0f, float.NegativeInfinity, 0f)]
    [TestCase(0f, 0f, float.PositiveInfinity)]
    public void PoseProbeRejectsNonFiniteAnglesBeforeLookingForRuntimeScene(float x, float y, float z)
    {
        var task = (Task)PoseProbeMethod().Invoke(null, new object[] { true, x, y, z });
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception.GetBaseException(), Is.TypeOf<ArgumentException>());
        Assert.That(SceneManager.GetActiveScene().isDirty, Is.False);
    }

    private static MethodInfo PoseProbeMethod()
    {
        var type = Type.GetType("AppleWatchController.Samples.TargetPop.Editor.AppleWatchDemoProbe, AppleWatchController.Samples.TargetPop.Editor");
        var method = type.GetMethod("PoseAsync");
        Assert.That(method, Is.Not.Null, "Transient Play Mode pose simulation needs its own CLI command, without scene authoring side effects.");
        return method;
    }

    [Test]
    public void CreateCommandRefusesToDiscardUnsavedSceneChanges()
    {
        new GameObject("Unsaved user object");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        var error = Assert.Throws<TargetInvocationException>(() => InvokeCreate(folder + "/Demo.unity", false));
        Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(GameObject.Find("Unsaved user object"), Is.Not.Null);
        Assert.That(File.Exists(folder + "/Demo.unity"), Is.False);
    }

    private static void InvokeCreate(string path, bool startup)
    {
        var type = Type.GetType("AppleWatchController.Samples.TargetPop.Editor.TargetPopSampleSetup, AppleWatchController.Samples.TargetPop.Editor");
        Assert.That(type, Is.Not.Null, "Scene setup must run through the project Unity CLI custom command.");
        type.GetMethod("Create").Invoke(null, new object[] { "Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Target Pop", path, startup });
    }
}
