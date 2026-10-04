using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using AppleWatchController.Samples.TargetPop.Editor;

[TestFixture("Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Target Pop")]
public sealed class TargetPopSampleImportTests
{
    private readonly string sampleRoot;
    private SceneSetup[] originalScenes;
    private EditorBuildSettingsScene[] beforeScenes;
    private const string ScenePath = "Assets/Task4TargetTests/Demo.unity";
    public TargetPopSampleImportTests(string sampleRoot) => this.sampleRoot = sampleRoot;

    [SetUp]
    public void SetUp()
    {
        originalScenes = EditorSceneManager.GetSceneManagerSetup();
        beforeScenes = EditorBuildSettings.scenes;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset("Assets/Task4TargetTests");
        if (originalScenes.Length > 0 && originalScenes.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(originalScenes);
    }

    [Test]
    public void MissingUrpFailsBeforeWritingScene()
    {
        var graphics = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        try
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            Assert.Throws<InvalidOperationException>(() => TargetPopSampleSetup.Create(sampleRoot, ScenePath));
            Assert.That(File.Exists(ScenePath), Is.False);
            Assert.That(AssetDatabase.IsValidFolder("Assets/Task4TargetTests"), Is.False);
            CollectionAssert.AreEqual(beforeScenes.Select(s => (s.path, s.enabled)), EditorBuildSettings.scenes.Select(s => (s.path, s.enabled)));
        }
        finally
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = graphics;
            QualitySettings.renderPipeline = quality;
        }
    }

    [Test]
    public void RelocatedTargetPopFindsHudAndFont()
    {
        const string relocated = "Assets/Samples/Apple Watch Controller/Relocated";
        AssetDatabase.CreateFolder("Assets/Samples/Apple Watch Controller", "Relocated");
        var watchLab = Path.GetDirectoryName(sampleRoot).Replace('\\', '/') + "/Watch Lab";
        Assert.That(AssetDatabase.MoveAsset(sampleRoot, relocated + "/Target Pop"), Is.Empty);
        Assert.That(AssetDatabase.MoveAsset(watchLab, relocated + "/Watch Lab"), Is.Empty);
        try
        {
            TargetPopSampleSetup.Create(relocated + "/Target Pop", ScenePath);
            var document = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
            Assert.That(AssetDatabase.GetAssetPath(document.visualTreeAsset), Is.EqualTo(relocated + "/Target Pop/Demo/WatchDemoHud.uxml"));
            var font = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Task4TargetTests/DemoAssets/DemoFont.asset");
            Assert.That(font.sourceFontFile, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Font>(relocated + "/Target Pop/Demo/Inter.ttf")));
            Assert.That(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(document.panelSettings.themeStyleSheet)), Does.Contain(relocated + "/Target Pop/Demo/WatchDemoHud.uss"));
            CollectionAssert.AreEqual(beforeScenes.Select(s => (s.path, s.enabled)), EditorBuildSettings.scenes.Select(s => (s.path, s.enabled)));
        }
        finally
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(AssetDatabase.MoveAsset(relocated + "/Target Pop", sampleRoot), Is.Empty);
            Assert.That(AssetDatabase.MoveAsset(relocated + "/Watch Lab", watchLab), Is.Empty);
            AssetDatabase.DeleteAsset(relocated);
        }
    }

    [Test]
    public void MisconfiguredUrpRendererFailsBeforeWritingScene()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        Assert.That(pipeline, Is.Not.Null, "This fixture requires the configured URP qualification project.");
        using var data = new SerializedObject(pipeline);
        var renderers = data.FindProperty("m_RendererDataList");
        var index = data.FindProperty("m_DefaultRendererIndex").intValue;
        var renderer = renderers.GetArrayElementAtIndex(index);
        var original = renderer.objectReferenceValue;
        try
        {
            renderer.objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => TargetPopSampleSetup.Create(sampleRoot, ScenePath));
            Assert.That(File.Exists(ScenePath), Is.False);
            Assert.That(AssetDatabase.IsValidFolder("Assets/Task4TargetTests"), Is.False);
            CollectionAssert.AreEqual(beforeScenes.Select(s => (s.path, s.enabled)), EditorBuildSettings.scenes.Select(s => (s.path, s.enabled)));
        }
        finally
        {
            renderer.objectReferenceValue = original;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    [Test]
    public void MissingWatchSourceExplainsWatchLabImport()
    {
        var source = Path.GetDirectoryName(sampleRoot).Replace('\\', '/') + "/Watch Lab/WatchControllerSample.watch.uxml";
        Assert.That(AssetDatabase.MoveAsset(source, source + ".temporarily-unavailable"), Is.Empty);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => TargetPopSampleSetup.Create(sampleRoot, ScenePath));
            Assert.That(error.Message, Does.Contain("Import Watch Lab"));
            Assert.That(File.Exists(ScenePath), Is.False);
        }
        finally { Assert.That(AssetDatabase.MoveAsset(source + ".temporarily-unavailable", source), Is.Empty); }
    }

    [Test]
    public void DirtySceneIsNotDiscarded()
    {
        var marker = new GameObject("Unsaved marker");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Assert.Throws<InvalidOperationException>(() => TargetPopSampleSetup.Create(sampleRoot, ScenePath));
        Assert.That(marker, Is.Not.Null);
        Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
        Assert.That(File.Exists(ScenePath), Is.False);
    }
}
