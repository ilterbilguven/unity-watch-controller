using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AppleWatchController.Editor;
using NUnit.Framework;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PipelineIntegrationTests
{
    private string folder;
    private string statePath;
    private byte[] previousState;

    [SetUp]
    public void SetUp()
    {
        var name = "PipelineWatchTest_" + Guid.NewGuid().ToString("N");
        folder = "Assets/" + name;
        AssetDatabase.CreateFolder("Assets", name);
        var state = typeof(WatchUiExportMenu).Assembly.GetType("AppleWatchController.Editor.AppleWatchUiBuildState", true);
        statePath = (string)state.GetMethod("GetStateFilePath").Invoke(null, null);
        previousState = File.Exists(statePath) ? File.ReadAllBytes(statePath) : null;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<WatchUiPreviewWindow>()) window.Close();
        AssetDatabase.DeleteAsset(folder);
        if (previousState != null) File.WriteAllBytes(statePath, previousState);
        else if (File.Exists(statePath)) File.Delete(statePath);
    }

    [Test]
    public void CommandsPresentWithPipeline07()
    {
        CheckCommand("watch_ui_export", "AppleWatchController.Pipeline.Editor", "watch_ui_path");
        CheckCommand("watch_ui_preview", "AppleWatchController.Pipeline.Editor", "watch_ui_path", "route");
#if UNITY_IOS
        CheckCommand("apple_watch_export_ios", "AppleWatchController.Pipeline.Build.Editor", "output_path", "simulator");
        CheckCommand("apple_watch_ios_sdk_state", "AppleWatchController.Pipeline.Build.Editor");
#else
        Assert.That(RegisteredCommands().Any(c => c.Name == "apple_watch_export_ios" || c.Name == "apple_watch_ios_sdk_state"), Is.False);
#endif
        Assert.That(RegisteredCommands().Any(c => c.Name == "apple_watch_remove_assistant"), Is.False);
    }

    [UnityTest]
    public IEnumerator RegisteredExportDelegatesToAssetsOwnedService()
    {
        var path = folder + "/Controller.watch.uxml";
        File.WriteAllText(path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\"><watch:watch-text label=\"Pipeline export proof\" /></ui:UXML>");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var command = FindCommand("watch_ui_export");
        LogAssert.Expect(LogType.Warning, "[AppleWatch] Watch UI conversion warning: USS width/height are not exported: public resolved styles cannot distinguish authored dimensions from auto layout. Use min/max size constraints or supported align-self: stretch instead.");
        var task = (Task<WatchUiExportResult>)command.Method.Invoke(null, new object[] { path });
        yield return Await(task);
        Assert.That(task.Result.Status, Is.EqualTo("success"));
        Assert.That(task.Result.InputPath, Is.EqualTo(path));
        Assert.That(task.Result.OutputPath, Is.EqualTo(Path.GetFullPath(folder + "/Controller.generated.swift")));
        Assert.That(File.ReadAllText(task.Result.OutputPath), Does.Contain("Text(\"Pipeline export proof\")"));
        Assert.That(File.ReadAllText(statePath), Does.Contain(path));
        AssertDirectCall(command.Method, typeof(WatchUiExportMenu).GetMethod("ExportWatchUiWithExplicitPathAsync"));
    }

    [Test]
    public void TargetPopCommandsPreserveNamesAndRequireExplicitSampleRoot()
    {
        CheckCommand("watch_demo_create", "AppleWatchController.Samples.TargetPop.Pipeline.Editor", "sample_root", "scene_path", "make_startup_scene", "watch_ui_path");
        Assert.That(FindCommand("watch_demo_create").Method.GetParameters()[2].DefaultValue, Is.EqualTo(false));
        foreach (var name in new[] { "watch_demo_state", "watch_demo_pose", "watch_demo_simulate", "watch_demo_ui", "watch_demo_capture" })
        {
            var command = FindCommand(name);
            Assert.That(command.Method.DeclaringType.Assembly.GetName().Name, Is.EqualTo("AppleWatchController.Samples.TargetPop.Pipeline.Editor"));
            Assert.That(command.MainThreadRequired, Is.True);
        }
    }

    [UnityTest]
    public IEnumerator RegisteredPreviewResolvesRequestedRoute()
    {
        var path = folder + "/Controller.watch.uxml";
        File.WriteAllText(path, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\"><watch:watch-navigation-stack><watch:watch-text label=\"Home\" /><watch:watch-navigation-destination route=\"details\"><watch:watch-text label=\"Pipeline route proof\" /></watch:watch-navigation-destination></watch:watch-navigation-stack></ui:UXML>");
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var task = (Task<WatchUiPreviewResult>)FindCommand("watch_ui_preview").Method.Invoke(null, new object[] { path, "details" });
        yield return Await(task);
        Assert.That(task.Result.Status, Is.EqualTo("success"));
        Assert.That(task.Result.InputPath, Is.EqualTo(path));
        Assert.That(task.Result.Route, Is.EqualTo("details"));
        Assert.That(task.Result.WindowType, Is.EqualTo(typeof(WatchUiPreviewWindow).FullName));
        var screen = Resources.FindObjectsOfTypeAll<WatchUiPreviewWindow>().Single().rootVisualElement.Q("watch-screen");
        Assert.That(screen.panel, Is.Not.Null);
        Assert.That(screen.resolvedStyle.width, Is.EqualTo(211f).Within(0.1f));
        Assert.That(screen.resolvedStyle.height, Is.EqualTo(257f).Within(0.1f));
        Assert.That(screen.Query<Label>().ToList().Any(label => label.text == "Pipeline route proof" && label.resolvedStyle.display != DisplayStyle.None), Is.True);
    }

#if UNITY_IOS
    [Test]
    public void RegisteredBuildCommandsDelegateWithoutChangingSettingsOnRejection()
    {
        var service = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies().Single(a => a.GetName().Name == "AppleWatchController.Build.Editor")
            .GetType("AppleWatchController.Editor.AppleWatchBuildService", true);
        var settingsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProjectSettings/ProjectSettings.asset"));
        var before = File.ReadAllBytes(settingsPath);
        var sdk = FindCommand("apple_watch_ios_sdk_state");
        var actual = (AppleWatchIosSdkState)sdk.Method.Invoke(null, null);
        var expected = (AppleWatchIosSdkState)service.GetMethod("GetIosSdkState").Invoke(null, null);
        Assert.That(actual.Sdk, Is.EqualTo(expected.Sdk));
        Assert.That(actual.SavedSettings, Is.EqualTo(expected.SavedSettings));
        AssertDirectCall(sdk.Method, service.GetMethod("GetIosSdkState"));
        var export = FindCommand("apple_watch_export_ios");
        var failure = Assert.Throws<TargetInvocationException>(() => export.Method.Invoke(null, new object[] { "", true }));
        Assert.That(failure.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(File.ReadAllBytes(settingsPath), Is.EqualTo(before));
        AssertDirectCall(export.Method, service.GetMethod("ExportIos"));
    }
#endif

    private static void CheckCommand(string name, string assembly, params string[] arguments)
    {
        var command = FindCommand(name);
        Assert.That(command.Method.DeclaringType.Assembly.GetName().Name, Is.EqualTo(assembly));
        Assert.That(command.MainThreadRequired, Is.True);
        Assert.That(command.Parameters.Select(p => p.Name), Is.EqualTo(arguments));
        if (arguments.Length > 0) Assert.That(command.Parameters[0].Required, Is.True);
        if (arguments.Length > 1) Assert.That(command.Parameters[1].Required, Is.False);
    }

    private static CommandInfo FindCommand(string name)
    {
        var commands = RegisteredCommands().Where(command => command.Name == name).ToArray();
        Assert.That(commands.Length, Is.EqualTo(1), "Actual Pipeline registry must contain exactly one " + name);
        return commands[0];
    }

    private static CommandInfo[] RegisteredCommands()
    {
        var registry = typeof(CliCommandAttribute).Assembly.GetType("Unity.Pipeline.Commands.CommandRegistry", true);
        return ((System.Collections.Generic.IEnumerable<CommandInfo>)registry.GetMethod("DiscoverCommands").Invoke(null, null)).ToArray();
    }

    private static IEnumerator Await(Task task)
    {
        var deadline = EditorApplication.timeSinceStartup + 60;
        while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "Real Pipeline handler timed out.");
        Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        Assert.That(task.IsCanceled, Is.False);
    }

    // Thin synchronous adapters must call the actual shared service, not duplicate it.
    private static void AssertDirectCall(MethodInfo adapter, MethodInfo service)
    {
        var bytes = adapter.GetMethodBody().GetILAsByteArray();
        var callsService = false;
        for (var i = 0; i + 4 < bytes.Length; i++)
        {
            if (bytes[i] != 0x28) continue; // call
            try
            {
                if (adapter.Module.ResolveMethod(BitConverter.ToInt32(bytes, i + 1)) == service) callsService = true;
            }
            catch (ArgumentException) { }
        }
        Assert.That(callsService, Is.True, adapter.Name + " must delegate to " + service.Name);
    }
}
