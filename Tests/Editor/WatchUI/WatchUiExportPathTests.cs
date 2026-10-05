using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

[TestFixture("Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Watch Lab")]
public sealed class WatchUiExportPathTests
{
    private readonly string sampleRoot;
    public WatchUiExportPathTests(string sampleRoot) => this.sampleRoot = sampleRoot;
    private string folder;
    private string sourcePath;
    private string statePath;
    private byte[] previousState;
    private EditorWindow previewWindow;

    [SetUp]
    public void SetUp()
    {
        var folderName = "WatchExportTest_" + Guid.NewGuid().ToString("N");
        folder = "Assets/" + folderName;
        sourcePath = folder + "/Latest.watch.uxml";
        statePath = (string)BuildStateType.GetMethod("GetStateFilePath").Invoke(null, null);
        previousState = File.Exists(statePath) ? File.ReadAllBytes(statePath) : null;
        AssetDatabase.CreateFolder("Assets", folderName);
        AssetDatabase.DisallowAutoRefresh();
    }

    [TearDown]
    public void TearDown()
    {
        if (previewWindow != null)
            previewWindow.Close();
        AssetDatabase.AllowAutoRefresh();
        AssetDatabase.DeleteAsset(folder);
        if (previousState != null)
            File.WriteAllBytes(statePath, previousState);
        else if (File.Exists(statePath))
            File.Delete(statePath);
    }

    [UnityTest]
    public IEnumerator ExportActualGalleryPreservesEnglishCategoriesAndCustomRows()
    {
        RequireSwiftToolchain();
        var stylesheetPath = folder + "/WatchControllerGallery.uss";
        File.Copy(sampleRoot + "/WatchControllerGallery.uss", stylesheetPath);
        AssetDatabase.ImportAsset(stylesheetPath, ImportAssetOptions.ForceSynchronousImport);
        File.Copy(sampleRoot + "/WatchControllerSample.watch.uxml", sourcePath);
        AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);

        yield return ExportSource();
        var swift = File.ReadAllText(folder + "/Latest.generated.swift");
        Assert.That(swift, Does.Contain("Text(\"Watch Lab\")"));
        foreach (var category in new[] { "Controls", "Touch", "Digital Crown", "Motion", "Audio", "Components" })
            Assert.That(swift, Does.Contain("Text(\"" + category + "\")"));
        Assert.That(swift, Does.Contain("NavigationLink(value: \"controls\") {"));
        Assert.That(swift, Does.Contain("Image(systemName: \"gamecontroller\")"));
        Assert.That(swift, Does.Contain("Text(\"Buttons and values\")"));
        Assert.That(swift,
            Does.Contain(".padding(EdgeInsets(top: 10, leading: 10, bottom: 10, trailing: 10))"));
    }

    [UnityTest]
    public IEnumerator ExportUsesLatestUxmlEvenWhenAutomaticRefreshIsDisabled()
    {
        RequireSwiftToolchain();
        const string oldUxml = "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\"><watch:watch-text label=\"old-copy\" /></ui:UXML>";
        File.WriteAllText(sourcePath, oldUxml);
        AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(sourcePath, oldUxml.Replace("old-copy", "latest-copy"));

        yield return ExportSource();
        var swift = File.ReadAllText(folder + "/Latest.generated.swift");
        Assert.That(swift, Does.Contain("Text(\"latest-copy\")"));
        Assert.That(swift, Does.Not.Contain("Text(\"old-copy\")"));
    }

    [UnityTest]
    public IEnumerator ExportUsesResolvedStylesFromCurrentStylesheetBytes()
    {
        RequireSwiftToolchain();
        WriteStyledSource(13);
        File.WriteAllText(folder + "/Latest.uss", ".headline { font-size: 31px; }");

        yield return ExportSource();
        var swift = File.ReadAllText(folder + "/Latest.generated.swift");
        Assert.That(swift, Does.Contain(".font(.system(size: 31, weight: .regular))"),
            "Export must import current USS and wait for attached-panel style resolution.");
    }

    [UnityTest]
    public IEnumerator ExportHandGestureShortcutsCompilesAllFourNativeControls()
    {
        RequireSwiftToolchain();
        foreach (var control in new[]
        {
            "<watch:watch-button watch-id=\"primary\" label=\"Fire\" action=\"primary\" hand-gesture-shortcut=\"PrimaryAction\" />",
            "<watch:watch-toggle watch-id=\"primary\" label=\"Toggle\" hand-gesture-shortcut=\"PrimaryAction\" />",
            "<watch:watch-navigation-stack><watch:watch-navigation-link watch-id=\"primary\" title=\"Open\" route=\"details\" hand-gesture-shortcut=\"PrimaryAction\" />"
                + "<watch:watch-navigation-destination route=\"details\"><watch:watch-text label=\"Details\" /></watch:watch-navigation-destination></watch:watch-navigation-stack>",
            "<watch:watch-textfield watch-id=\"primary\" title=\"Name\" text-binding=\"name\" hand-gesture-shortcut=\"PrimaryAction\" />"
        })
        {
            File.WriteAllText(sourcePath,
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\">" + control + "</ui:UXML>");
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            yield return ExportSource();
            var swift = File.ReadAllText(folder + "/Latest.generated.swift");
            Assert.That(Regex.Matches(swift, @"\.handGestureShortcut\(\.primaryAction\)").Count, Is.EqualTo(1), control);
        }
    }

    [UnityTest]
    public IEnumerator ImportedSampleExportLeavesPackageUnchanged()
    {
        RequireSwiftToolchain();
        WriteStyledSource(21);
        var before = SnapshotPackage();
        yield return ExportSource();
        Assert.That(File.Exists(folder + "/Latest.generated.swift"), Is.True);
        Assert.That(File.ReadAllText(statePath), Does.Contain(sourcePath));
        Assert.That(SnapshotPackage(), Is.EqualTo(before), "Assets export must not write to the installed package.");
    }

    [Test]
    public void ExportRejectsPackageSourceWithoutWriting()
    {
        const string fixture = "Tests/Editor/WatchUI/Fixtures/ReadOnly.watch.uxml";
        var assetPath = AppleWatchController.Editor.WatchPackagePaths.AssetPath(fixture);
        var filePath = AppleWatchController.Editor.WatchPackagePaths.FilePath(fixture);
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath), Is.Not.Null,
            "The ownership check must reject a real, valid package source, not merely a missing file.");
        var before = SnapshotPackage();
        foreach (var path in new[] { assetPath, filePath })
        {
            var task = AppleWatchController.Editor.WatchUiExportMenu.ExportWatchUiWithExplicitPathAsync(path);
            Assert.Throws<ArgumentException>(() => task.GetAwaiter().GetResult());
        }
        Assert.That(SnapshotPackage(), Is.EqualTo(before));
        Assert.That(Directory.GetFiles(folder), Is.Empty);
    }

    private static string[] SnapshotPackage()
    {
        var root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
            typeof(AppleWatchController.Editor.WatchUiExportMenu).Assembly).resolvedPath;
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new { File = file, Relative = Path.GetRelativePath(root, file).Replace('\\', '/') })
            .Where(file => !file.Relative.StartsWith(".git/", StringComparison.Ordinal))
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file =>
            {
                using var sha = SHA256.Create();
                return file.Relative + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file.File)));
            }).ToArray();
    }

    [UnityTest]
    public IEnumerator ExportPreservesResolvedStylesOnInactiveNavigationDestinations()
    {
        RequireSwiftToolchain();
        var stylesheetPath = folder + "/Latest.uss";
        File.WriteAllText(stylesheetPath,
            ".main-heading { font-size: 13px; color: rgb(0, 255, 0); }\n" +
            ".hidden-heading { font-size: 37px; color: rgb(255, 0, 0); " +
            "padding-top: 5px; padding-right: 11px; padding-bottom: 13px; padding-left: 7px; " +
            "background-color: rgb(0, 0, 255); min-height: 83px; }");
        AssetDatabase.ImportAsset(stylesheetPath, ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(sourcePath,
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\">" +
            "<ui:Style src=\"Latest.uss\" />" +
            "<watch:watch-navigation-stack>" +
            "<watch:watch-v-stack>" +
            "<watch:watch-text class=\"main-heading\" label=\"Main page\" />" +
            "<watch:watch-navigation-link route=\"details\" title=\"Open details\" />" +
            "</watch:watch-v-stack>" +
            "<watch:watch-navigation-destination name=\"hiddenDestination\" route=\"details\">" +
            "<watch:watch-text class=\"hidden-heading\" label=\"Hidden destination\" />" +
            "</watch:watch-navigation-destination>" +
            "</watch:watch-navigation-stack></ui:UXML>");
        AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);

        var previewType = Type.GetType("AppleWatchController.Editor.WatchUiPreviewWindow, AppleWatchController.Editor", true);
        var previewTask = (Task)previewType.GetMethod("OpenAsync").Invoke(null, new object[] { sourcePath, null });
        yield return AwaitTask(previewTask);
        previewWindow = (EditorWindow)previewTask.GetType().GetProperty("Result").GetValue(previewTask);
        var destination = previewWindow.rootVisualElement.Q("hiddenDestination");
        Assert.That(destination, Is.Not.Null);
        Assert.That(destination.resolvedStyle.display, Is.EqualTo(DisplayStyle.None),
            "The destination must stay inactive; this test must not navigate to it to resolve its styles.");

        yield return ExportSource();
        var swift = File.ReadAllText(folder + "/Latest.generated.swift");
        var destinationText = Regex.Match(swift,
            "Text\\(\\\"Hidden destination\\\"\\)(?<modifiers>[\\s\\S]*?)(?:\\r?\\n\\s*\\}|$)");
        Assert.That(destinationText.Success, Is.True, "The inactive destination must be present in generated navigation.");
        Assert.That(destinationText.Groups["modifiers"].Value,
            Does.Contain(".font(.system(size: 37, weight: .regular))"));
        Assert.That(destinationText.Groups["modifiers"].Value,
            Does.Contain(".foregroundStyle(Color(red: 1, green: 0, blue: 0, opacity: 1))"));
        var modifiers = destinationText.Groups["modifiers"].Value;
        Assert.That(modifiers,
            Does.Contain(".padding(EdgeInsets(top: 5, leading: 7, bottom: 13, trailing: 11))"),
            "Inactive destination padding must be resolved through layout before export.");
        Assert.That(modifiers,
            Does.Contain(".background(RoundedRectangle(cornerRadius: 0).fill(Color(red: 0, green: 0, blue: 1, opacity: 1)))"));
        Assert.That(modifiers, Does.Contain(".frame(minHeight: 83)"));
    }

    [Test]
    public void StylesheetOnlyEditInvalidatesExportFingerprint()
    {
        WriteStyledSource(13);
        var output = folder + "/Latest.generated.swift";
        File.WriteAllText(output, "// Fingerprint fixture");
        Assert.That(BuildStateType.GetMethod("TryWriteExportState").Invoke(null, new object[] { sourcePath, output }), Is.True);
        Assert.That(ReadLatestOutput(), Is.True);

        File.WriteAllText(folder + "/Latest.uss", ".headline { font-size: 31px; }");

        Assert.That(ReadLatestOutput(), Is.False,
            "A stylesheet-only edit must fail closed even before AssetDatabase refresh.");
    }

    [Test]
    public void LegacyFingerprintWithoutDependenciesFailsClosed()
    {
        WriteStyledSource(13);
        var output = folder + "/Latest.generated.swift";
        File.WriteAllText(output, "// Fingerprint fixture");
        Assert.That(BuildStateType.GetMethod("TryWriteExportState").Invoke(null, new object[] { sourcePath, output }), Is.True);
        var oldState = Regex.Replace(File.ReadAllText(statePath),
            "\\\"sourceDependenciesSha256\\\"\\s*:\\s*\\\"[^\\\"]*\\\"\\s*,?", string.Empty);
        File.WriteAllText(statePath, oldState);

        Assert.That(ReadLatestOutput(), Is.False,
            "A pre-USS fingerprint cannot establish that the generated output is current.");
    }

    [UnityTest]
    public IEnumerator PreviewMountsSourceInWatchViewportWithResolvedStyles()
    {
        WriteStyledSource(31);
        var type = Type.GetType("AppleWatchController.Editor.WatchUiPreviewWindow, AppleWatchController.Editor");
        Assert.That(type, Is.Not.Null, "Preview must use a real EditorWindow panel.");
        var method = type.GetMethod("OpenAsync", new[] { typeof(string), typeof(string) });
        Assert.That(method, Is.Not.Null);
        var task = (Task)method.Invoke(null, new object[] { sourcePath, null });
        yield return AwaitTask(task);
        previewWindow = (EditorWindow)task.GetType().GetProperty("Result").GetValue(task);
        var screen = previewWindow.rootVisualElement.Q("watch-screen");
        Assert.That(screen, Is.Not.Null);
        Assert.That(screen.panel, Is.Not.Null);
        Assert.That(screen.resolvedStyle.width, Is.EqualTo(211f).Within(0.1f));
        Assert.That(screen.resolvedStyle.height, Is.EqualTo(257f).Within(0.1f));
        var authoredText = screen.Q(className: "headline");
        Assert.That(authoredText, Is.Not.Null);
        Assert.That(authoredText.resolvedStyle.fontSize, Is.EqualTo(31f).Within(0.1f));
        Assert.That(screen.Q<Label>()?.text, Is.EqualTo("Styled source"),
            "The UXML-authored control must be rendered, not reconstructed in the host.");
    }

    private static Type BuildStateType => Type.GetType(
        "AppleWatchController.Editor.AppleWatchUiBuildState, AppleWatchController.Editor", true);

    private static bool ReadLatestOutput()
    {
        return (bool)BuildStateType.GetMethod("TryReadLatestOutputPath").Invoke(null, new object[] { null });
    }

    private void WriteStyledSource(int fontSize)
    {
        var stylesheetPath = folder + "/Latest.uss";
        File.WriteAllText(stylesheetPath, $".headline {{ font-size: {fontSize}px; }}");
        AssetDatabase.ImportAsset(stylesheetPath, ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(sourcePath,
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\">" +
            "<ui:Style src=\"Latest.uss\" /><watch:watch-text class=\"headline\" label=\"Styled source\" /></ui:UXML>");
        AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
    }

    private IEnumerator ExportSource()
    {
        var exporter = Type.GetType("AppleWatchController.Editor.WatchUiExportMenu, AppleWatchController.Editor", true);
        var task = exporter.GetMethod("ExportWatchUiWithExplicitPathAsync").Invoke(null, new object[] { sourcePath }) as Task;
        Assert.That(task, Is.Not.Null, "The export command must await panel work asynchronously.");
        yield return AwaitTask(task);
        var invocation = task.GetType().GetProperty("Result").GetValue(task);

        Assert.That(invocation.GetType().GetProperty("Status").GetValue(invocation), Is.EqualTo("success"));
        Assert.That(invocation.GetType().GetProperty("InputPath").GetValue(invocation), Is.EqualTo(sourcePath));
        Assert.That(invocation.GetType().GetProperty("OutputPath").GetValue(invocation), Is.Not.Null);
    }

    private static IEnumerator AwaitTask(Task task)
    {
        var deadline = EditorApplication.timeSinceStartup + 330;
        while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline)
            yield return null;
        Assert.That(task.IsCompleted, Is.True, "Panel-backed operation timed out without blocking the Editor.");
        if (task.IsFaulted)
            ExceptionDispatchInfo.Capture(task.Exception.GetBaseException()).Throw();
        Assert.That(task.IsCanceled, Is.False);
    }

    private static void RequireSwiftToolchain()
    {
        if (Application.platform != RuntimePlatform.OSXEditor)
            Assert.Ignore("Watch Swift compilation requires the macOS toolchain.");
    }

    [Test]
    public void GeneratedWatchSourceIsNotAnIosPlayerPlugin()
    {
        var outputPath = folder + "/Latest.generated.swift";
        File.WriteAllText(outputPath, "// Plugin importer fixture");
        var type = Type.GetType("AppleWatchController.Editor.WatchUiExportMenu, AppleWatchController.Editor", true);
        var method = type.GetMethod("FinalizeWatchUiExportOutput", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method.Invoke(null, new object[] { outputPath }), Is.True);
        var importer = AssetImporter.GetAtPath(outputPath) as PluginImporter;
        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.iOS), Is.False,
            "The postprocessor attaches this @main source only to the Watch target.");
    }

    [TestCase("Assets/WatchControllerSample.watch.uxml", "Assets/WatchControllerSample.watch.uxml")]
    [TestCase("/tmp/OtherProject/Assets/WatchControllerSample.watch.uxml", null)]
    [TestCase("../OtherProject/Assets/WatchControllerSample.watch.uxml", null)]
    public void SourcePathMustBelongToTheCurrentProject(string path, string expected)
    {
        var type = Type.GetType("AppleWatchController.Editor.WatchUiExportMenu, AppleWatchController.Editor", true);
        var method = type.GetMethod("ResolveProjectRelativeWatchUiPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { path }), Is.EqualTo(expected));
    }
}
