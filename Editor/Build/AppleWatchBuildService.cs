using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AppleWatchController.Editor
{
    /// <summary>Exports iOS Xcode projects without requiring Unity Pipeline or a CLI.</summary>
    /// <remarks>Call on Unity's main thread. Export does not compile, sign, or install an Xcode app.</remarks>
    public static class AppleWatchBuildService
    {
        /// <summary>Reads live and saved iOS SDK settings without changing them.</summary>
        public static AppleWatchIosSdkState GetIosSdkState()
        {
            var settingsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../ProjectSettings/ProjectSettings.asset"));
            var savedLines = File.ReadLines(settingsPath)
                .Where(line => line.TrimStart().StartsWith("iPhoneSdkVersion:", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("iOSSimulatorArchitecture:", StringComparison.Ordinal))
                .Select(line => line.Trim()).ToArray();
            var settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            return new AppleWatchIosSdkState
            {
                Sdk = PlayerSettings.iOS.sdkVersion.ToString(),
                SdkValue = (int)PlayerSettings.iOS.sdkVersion,
                SimulatorArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture.ToString(),
                SimulatorArchitectureValue = (int)PlayerSettings.iOS.simulatorSdkArchitecture,
                SavedSettings = savedLines,
                LoadedSettings = settingsAssets.Select(asset => asset.GetType().FullName
                    + " dirty=" + EditorUtility.IsDirty(asset)).ToArray()
            };
        }

        /// <summary>Exports enabled scenes to a new or empty iOS Xcode output directory.</summary>
        /// <param name="outputPath">An absolute path or a path relative to the consumer project root.</param>
        /// <param name="simulator">Temporarily selects the ARM64 Simulator SDK.</param>
        /// <returns>The successful export result, absolute path, and Unity-reported size.</returns>
        /// <remarks>
        /// Requires iOS Build Support and a fresh Assets-owned Watch UI export. Does not clean output.
        /// Simulator settings are restored and saved in finally, including after build failure.
        /// The postprocessor enforces Swift, ARM64, iOS 18, and watchOS 11 during export.
        /// Export can leave partial output on failure; success does not establish native/device behavior.
        /// </remarks>
        /// <exception cref="ArgumentException">The path is blank or lies inside the installed package.</exception>
        /// <exception cref="InvalidOperationException">A prerequisite is missing, output is nonempty, or export fails.</exception>
        public static AppleWatchBuildCommandResult ExportIos(string outputPath, bool simulator = false)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An iOS Xcode output path is required.", nameof(outputPath));
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var resolvedOutputPath = Path.GetFullPath(Path.IsPathRooted(outputPath)
                ? outputPath : Path.Combine(projectRoot, outputPath));
            var packageRoot = Path.GetDirectoryName(WatchPackagePaths.FilePath("package.json"));
            var packageRelative = Path.GetRelativePath(packageRoot, resolvedOutputPath).Replace('\\', '/');
            if (packageRelative == "." || (!Path.IsPathRooted(packageRelative)
                && packageRelative != ".." && !packageRelative.StartsWith("../", StringComparison.Ordinal)))
                throw new ArgumentException("Xcode output must not be written inside the installed package.", nameof(outputPath));
            if (Directory.Exists(resolvedOutputPath) && Directory.EnumerateFileSystemEntries(resolvedOutputPath).Any())
                throw new InvalidOperationException($"Apple Watch export requires an empty output directory: '{resolvedOutputPath}'.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                throw new InvalidOperationException("Install iOS Build Support for this Unity Editor, then select iOS in Build Profiles.");
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("At least one enabled scene is required for the iOS export. Add it in Build Profiles.");
            if (!AppleWatchUiBuildState.TryReadLatestOutputPath(out _))
                throw new InvalidOperationException("Export an Assets-owned .watch.uxml through Tools > AppleWatch before exporting iOS.");
            Directory.CreateDirectory(resolvedOutputPath);
            var report = WithIosSdk(simulator, () => BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = resolvedOutputPath, target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS, options = BuildOptions.None
            }));
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                var result = report == null ? "NoReport" : report.summary.result.ToString();
                var details = report == null ? string.Empty : string.Join(" | ", report.steps.SelectMany(step => step.messages)
                    .Where(message => message.type == LogType.Error || message.type == LogType.Exception)
                    .Select(message => message.content?.Trim()).Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct().Take(8));
                throw new InvalidOperationException($"Apple Watch iOS export failed: {result}. {details}".TrimEnd());
            }
            return new AppleWatchBuildCommandResult
            {
                OutputPath = resolvedOutputPath, TotalSize = report.summary.totalSize, Result = report.summary.result.ToString()
            };
        }

        private static BuildReport WithIosSdk(bool simulator, Func<BuildReport> build)
        {
            if (!simulator) return build();
            var previousSdk = PlayerSettings.iOS.sdkVersion;
            var previousArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;
            try
            {
                PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
                PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
                return build();
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = previousSdk;
                PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("Tools/AppleWatch/Export iOS Device Xcode Project")]
        private static void ExportDeviceFromMenu() => ExportFromMenu(false);

        [MenuItem("Tools/AppleWatch/Export iOS Simulator Xcode Project")]
        private static void ExportSimulatorFromMenu() => ExportFromMenu(true);

        private static void ExportFromMenu(bool simulator)
        {
            var path = EditorUtility.SaveFolderPanel("Choose a new or empty Xcode output directory", "", "AppleWatchExport");
            if (string.IsNullOrWhiteSpace(path)) return;
            try { Debug.Log("[AppleWatch] iOS Xcode project exported: " + ExportIos(path, simulator).OutputPath); }
            catch (Exception error) { EditorUtility.DisplayDialog("Apple Watch export", error.Message, "OK"); }
        }
    }
}
