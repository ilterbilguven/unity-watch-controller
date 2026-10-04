using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;

public sealed class AppleWatchSimulatorBuildTests
{
    [Test]
    public void IosSdkDiagnosticDoesNotWriteProjectSettings()
    {
        var path = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../ProjectSettings/ProjectSettings.asset"));
        var before = File.ReadAllBytes(path);
        var result = AppleWatchController.Editor.AppleWatchBuildService.GetIosSdkState();
        Assert.That(result.SdkValue, Is.EqualTo((int)PlayerSettings.iOS.sdkVersion));
        Assert.That(result.SimulatorArchitectureValue, Is.EqualTo((int)PlayerSettings.iOS.simulatorSdkArchitecture));
        Assert.That(result.SavedSettings, Is.EqualTo(ReadSavedSdkSettings()));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
    }

    [Test]
    public void IosExportRejectsPackageOutputWithoutWriting()
    {
        var root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
            typeof(AppleWatchController.Editor.WatchUiExportMenu).Assembly).resolvedPath;
        var output = Path.Combine(root, "ExportMustNotBeCreated_" + Guid.NewGuid().ToString("N"));
        var method = BuildCommandType.GetMethod("ExportIos");
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { output, false }));
        Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        Assert.That(Directory.Exists(output), Is.False);
    }

    [Test]
    public void ConsumerObjectiveCIsNotOwnedSource()
    {
        const string plugins = "Assets/Plugins";
        if (!AssetDatabase.IsValidFolder(plugins)) AssetDatabase.CreateFolder("Assets", "Plugins");
        const string ios = plugins + "/iOS";
        if (!AssetDatabase.IsValidFolder(ios)) AssetDatabase.CreateFolder(plugins, "iOS");
        var name = "UnrelatedConsumer_" + Guid.NewGuid().ToString("N");
        var folder = ios + "/" + name;
        AssetDatabase.CreateFolder(ios, name);
        File.WriteAllText(folder + "/Consumer.m", "// Unrelated consumer native plugin");
        try
        {
            var method = typeof(AppleWatchController.Editor.AppleWatchBuildPostprocessor)
                .GetMethod("EnsureNoObjectiveCFiles", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.DoesNotThrow(() => method.Invoke(null, null));
        }
        finally { AssetDatabase.DeleteAsset(folder); }
    }

    [Test]
    public void OwnedNativeObjectiveCIsRejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OwnedWatchNative_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Owned.m"), "// Controlled owned-source boundary fixture");
        try
        {
            var method = typeof(AppleWatchController.Editor.AppleWatchBuildPostprocessor)
                .GetMethod("EnsureNoObjectiveCFilesAtPath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Validate only the resolved package-owned native root.");
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { folder }));
            Assert.That(error.InnerException, Is.TypeOf<BuildFailedException>());
            Assert.That(error.InnerException.Message, Does.Contain("Owned.m"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public void SimulatorExportRejectsNonemptyDirectoryWithoutChangingSdkSettings()
    {
        var method = BuildCommandType.GetMethod("ExportIos", new[] { typeof(string), typeof(bool) });
        Assert.That(method, Is.Not.Null, "The export command must expose an optional simulator flag.");
        var previousSdk = PlayerSettings.iOS.sdkVersion;
        var previousArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
        var folder = Path.Combine(Path.GetTempPath(), "WatchSimulatorPreflight_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "existing-project.txt"), "Do not replace this export.");
        try
        {
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { folder, true }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(previousSdk));
            Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(previousArchitecture));
            Assert.That(File.ReadAllText(Path.Combine(folder, "existing-project.txt")), Is.EqualTo("Do not replace this export."));
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = previousSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void SimulatorSdkScopeRestoresSettingsWhenBuildCallbackThrows()
    {
        var method = SdkScopeMethod;
        var originalSdk = PlayerSettings.iOS.sdkVersion;
        var originalArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
        var previousArchitecture = (AppleMobileArchitectureSimulator)Enum.Parse(
            typeof(AppleMobileArchitectureSimulator), "X86_64");
        var failure = new InvalidOperationException("Controlled build failure.");
        try
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
            Func<BuildReport> callback = () =>
            {
                Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.SimulatorSDK));
                Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(AppleMobileArchitectureSimulator.ARM64));
                throw failure;
            };

            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { true, callback }));
            Assert.That(error.InnerException, Is.SameAs(failure));
            Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.DeviceSDK));
            Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(previousArchitecture));
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = originalSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = originalArchitecture;
        }
    }

    [Test]
    public void DeviceSdkScopePreservesCurrentSdkAndCallbackResult()
    {
        var method = SdkScopeMethod;
        var originalSdk = PlayerSettings.iOS.sdkVersion;
        var originalArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
        try
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            var called = false;
            Func<BuildReport> callback = () =>
            {
                called = true;
                Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.SimulatorSDK));
                Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(originalArchitecture));
                return null;
            };

            Assert.That(method.Invoke(null, new object[] { false, callback }), Is.Null);
            Assert.That(called, Is.True);
            Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.SimulatorSDK));
            Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(originalArchitecture));
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = originalSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = originalArchitecture;
        }
    }

    [Test]
    public void SimulatorSdkScopeRestoresSavedSettingsAfterCallbackPersistsTemporarySdk()
    {
        var method = SdkScopeMethod;
        var originalSdk = PlayerSettings.iOS.sdkVersion;
        var originalArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
        var previousArchitecture = (AppleMobileArchitectureSimulator)Enum.Parse(
            typeof(AppleMobileArchitectureSimulator), "X86_64");
        var failure = new InvalidOperationException("Failure after the export saves temporary settings.");
        try
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
            AssetDatabase.SaveAssets();
            var baseline = ReadSavedSdkSettings();
            Func<BuildReport> callback = () =>
            {
                Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.SimulatorSDK));
                Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(AppleMobileArchitectureSimulator.ARM64));
                AssetDatabase.SaveAssets();
                Assert.That(ReadSavedSdkSettings(), Is.Not.EqualTo(baseline),
                    "The callback must persist the temporary SDK to reproduce the real export failure.");
                throw failure;
            };

            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { true, callback }));
            Assert.That(error.InnerException, Is.SameAs(failure));
            Assert.That(PlayerSettings.iOS.sdkVersion, Is.EqualTo(iOSSdkVersion.DeviceSDK));
            Assert.That(PlayerSettings.iOS.simulatorSdkArchitecture, Is.EqualTo(previousArchitecture));
            Assert.That(ReadSavedSdkSettings(), Is.EqualTo(baseline),
                "Restoring live properties must also replace the temporary SDK saved by BuildPipeline.");
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = originalSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = originalArchitecture;
            AssetDatabase.SaveAssets();
        }
    }

    private static string[] ReadSavedSdkSettings()
    {
        return File.ReadLines("ProjectSettings/ProjectSettings.asset")
            .Where(line => line.TrimStart().StartsWith("iPhoneSdkVersion:", StringComparison.Ordinal) ||
                           line.TrimStart().StartsWith("iOSSimulatorArchitecture:", StringComparison.Ordinal))
            .Select(line => line.Trim()).ToArray();
    }

    private static Type BuildCommandType
    {
        get
        {
            var type = Type.GetType("AppleWatchController.Editor.AppleWatchBuildService, AppleWatchController.Build.Editor");
            Assert.That(type, Is.Not.Null, "iOS export must be a package service without Pipeline.");
            return type;
        }
    }

    private static MethodInfo SdkScopeMethod
    {
        get
        {
            var method = BuildCommandType.GetMethod("WithIosSdk", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(Func<BuildReport>) }, null);
            Assert.That(method, Is.Not.Null, "The real export must scope temporary simulator settings around its build callback.");
            return method;
        }
    }
}
