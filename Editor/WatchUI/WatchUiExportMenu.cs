using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using WatchUI;
using AppleWatchController.Editor;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Debug = UnityEngine.Debug;

namespace AppleWatchController.Editor
{
    /// <summary>Exports authored Watch UI assets through a style-resolved Editor preview.</summary>
    /// <remarks>The menu and CLI use this same export path. Generated Swift is output; edit the UXML, USS, or generator instead.</remarks>
    public static class WatchUiExportMenu
    {
        private const int NativeSwiftCompileTimeoutSeconds = 300;
        private static readonly SemaphoreSlim ExportGate = new SemaphoreSlim(1, 1);

        [MenuItem("Tools/AppleWatch/Configure iOS Export")]
        private static void ShowIosExportRequirements()
        {
            EditorUtility.DisplayDialog("Apple Watch iOS export requirements",
                "Install iOS Build Support for this Editor and select iOS in Build Profiles. "
                + "Add an enabled scene, configure your own bundle identifier and signing, then export an Assets-owned .watch.uxml. "
                + "Device and Simulator export menus are available when iOS is selected. "
                + "Export enforces Swift, ARM64, iOS 18 and watchOS 11. Package import does not change project settings.", "Open Build Profiles");
            EditorApplication.ExecuteMenuItem("File/Build Profiles");
        }

        [MenuItem("Tools/AppleWatch/Export Selected Watch UI to SwiftUI")]
        private static async void ExportSelectedWatchUI()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                Debug.LogError("[AppleWatch] No asset selected. Select a .watch.uxml asset first.");
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrWhiteSpace(path) || !IsWatchUiAsset(path))
            {
                Debug.LogError("[AppleWatch] Select a .watch.uxml asset.");
                return;
            }

            try
            {
                await ExportWatchUiWithExplicitPathAsync(path);
            }
            catch (Exception exception)
            {
                Debug.LogError("[AppleWatch] Export failed: " + exception.Message);
            }
        }

        /// <summary>Writes generated SwiftUI beside a Watch UI asset and records the export fingerprint.</summary>
        /// <param name="requestedPath">An absolute or project-relative .watch.uxml path inside this project's Assets folder.</param>
        /// <returns>A task with success status, the original input path, and the absolute .generated.swift path.</returns>
        /// <remarks>
        /// Call on Unity's main thread. Exports run one at a time and use a temporary preview window that closes afterward.
        /// The workflow imports source dependencies, resolves styles, converts the tree, and validates temporary Swift output.
        /// On macOS with /usr/bin/xcrun, validation compiles for watchOS 11 with Swift 5 and warnings-as-errors.
        /// Outside macOS, or when xcrun is absent, validation logs a warning and continues without a compiler check.
        /// Failed compiler validation or a changed source fingerprint preserves the previous output before replacement.
        /// Later import or state-recording failures can occur after the new output has been written.
        /// The generated plugin is excluded from Unity player targets; the iOS postprocessor adds it to the Watch target.
        /// Export state tracks source dependencies, output bytes, generator assemblies, and the Unity version.
        /// Re-export after these inputs change; a generated file alone is not valid build state.
        /// </remarks>
        /// <exception cref="ArgumentException">The path is blank, outside Assets, has the wrong suffix, or does not exist.</exception>
        /// <exception cref="InvalidOperationException">The tree, conversion, validation, import, or state recording fails.</exception>
        /// <exception cref="TimeoutException">The preview does not resolve styles and layout within 15 seconds.</exception>
        /// <exception cref="OperationCanceledException">The export preview closes before layout resolves.</exception>
        public static async Task<WatchUiExportResult> ExportWatchUiWithExplicitPathAsync(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                throw new ArgumentException("A .watch.uxml source path is required.", nameof(requestedPath));

            var resolvedPath = ResolveProjectRelativeWatchUiPath(requestedPath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                throw new ArgumentException("Watch UI source must belong to this project's Assets folder.", nameof(requestedPath));

            await ExportGate.WaitAsync();
            try
            {
                var outputPath = await ExportWatchUiAssetAsync(resolvedPath);
                return new WatchUiExportResult { Status = "success", InputPath = requestedPath, OutputPath = outputPath };
            }
            finally
            {
                ExportGate.Release();
            }
        }

        [MenuItem("Tools/AppleWatch/Export Selected Watch UI to SwiftUI", true)]
        private static bool ExportSelectedWatchUIValidate()
        {
            var selected = Selection.activeObject;
            if (selected == null)
                return false;

            var path = AssetDatabase.GetAssetPath(selected);
            return !string.IsNullOrEmpty(path) && IsWatchUiAsset(path);
        }

        private static bool IsWatchUiAsset(string path)
        {
            return path.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Resolves a path to an Assets-relative path in the current project.</summary>
        /// <param name="requestedPath">An absolute or project-relative path; backslashes are accepted.</param>
        /// <returns>The normalized Assets path, or null for blank paths and paths outside Assets.</returns>
        /// <remarks>Checks the path location only. The caller must check the extension and file existence.</remarks>
        internal static string ResolveProjectRelativeWatchUiPath(string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                return null;
            var normalizedInputPath = requestedPath.Replace('\\', '/');
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            var normalizedRequestPath = Path.IsPathRooted(normalizedInputPath)
                ? Path.GetFullPath(normalizedInputPath)
                : Path.GetFullPath(Path.Combine(projectRoot, normalizedInputPath));

            var normalizedResolvedPath = Path.GetFullPath(normalizedRequestPath).Replace('\\', '/');
            var relativePath = Path.GetRelativePath(projectRoot, normalizedResolvedPath).Replace('\\', '/');
            if (relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                return null;
            }

            if (Path.IsPathRooted(relativePath))
            {
                return null;
            }

            if (relativePath.StartsWith("/"))
            {
                relativePath = relativePath.TrimStart('/');
            }

            return string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                ? null
                : relativePath;
        }

        private static async Task<string> ExportWatchUiAssetAsync(string path)
        {
            if (!IsWatchUiAsset(path))
                throw new ArgumentException("Watch UI source must end with .watch.uxml.", nameof(path));

            var outputPath = AppleWatchUiBuildState.NormalizeToProjectPath(GetGeneratedOutputPath(path));
            WatchUiPreviewWindow host = null;
            try
            {
                host = await WatchUiPreviewWindow.CreateExportHostAsync(path);
                if (host.WatchRoot.panel == null)
                    throw new InvalidOperationException("Watch UI export requires an attached, style-resolved panel.");
                if (!WatchUiElementConverter.TryGenerateSwiftUI(host.WatchRoot, out var swiftCode, out var diagnostics))
                    throw new InvalidOperationException("Watch UI conversion failed: " + string.Join("; ", diagnostics.Errors));

                var sourceFingerprint = host.SourceDependencyFingerprint;
                if (!await TryWriteTextWithRetryAsync(outputPath, swiftCode ?? string.Empty, path, sourceFingerprint))
                    throw new InvalidOperationException("Generated Swift could not be validated or written.");
                if (!FinalizeWatchUiExportOutput(outputPath))
                    throw new InvalidOperationException("Generated Swift could not be imported.");
                if (!AppleWatchUiBuildState.TryWriteExportStateForSnapshot(path, outputPath, sourceFingerprint))
                    throw new InvalidOperationException("Generated Swift fingerprint state could not be persisted or source dependencies changed during export.");

                foreach (var warning in diagnostics.Warnings)
                {
                    Debug.LogWarning("[AppleWatch] Watch UI conversion warning: " + warning);
                }

                Debug.Log("[AppleWatch] SwiftUI file written: " + outputPath);
                return outputPath;
            }
            finally
            {
                if (host != null)
                    host.Close();
            }
        }

        private static bool FinalizeWatchUiExportOutput(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Debug.LogError("[AppleWatch] Missing generated swift output path.");
                return false;
            }

            var normalizedOutputPath = AppleWatchUiBuildState.NormalizeToProjectPath(outputPath) ?? outputPath;

            if (!File.Exists(normalizedOutputPath))
            {
                Debug.LogError(string.Format("[AppleWatch] Missing generated swift file: '{0}'.", normalizedOutputPath));
                return false;
            }

            var projectRelativePath = AppleWatchUiBuildState.NormalizeToAssetRelativePath(normalizedOutputPath);
            if (!string.IsNullOrWhiteSpace(projectRelativePath)
                && projectRelativePath.EndsWith(".generated.swift", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.ImportAsset(projectRelativePath);

                // This @main source belongs only to the postprocessor-owned Watch target.
                // Unity otherwise copies every Swift plugin into the iPhone player too.
                if (AssetImporter.GetAtPath(projectRelativePath) is PluginImporter importer)
                {
                    importer.SetCompatibleWithAnyPlatform(false);
                    importer.SetCompatibleWithEditor(false);
                    importer.SetCompatibleWithPlatform(BuildTarget.iOS, false);
                    importer.SetCompatibleWithPlatform(BuildTarget.tvOS, false);
                    importer.SetCompatibleWithPlatform(BuildTarget.VisionOS, false);
                    importer.SaveAndReimport();
                }
                return true;
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static string GetGeneratedOutputPath(string watchUiPath)
        {
            var normalized = watchUiPath.Replace('\\', '/');
            if (normalized.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = normalized.Substring(0, normalized.Length - ".watch.uxml".Length);
                return trimmed + ".generated.swift";
            }

            return Path.ChangeExtension(normalized, ".generated.swift");
        }

        private static async Task<bool> TryWriteTextWithRetryAsync(string outputPath, string content,
            string sourcePath, string sourceFingerprint)
        {
            const int maxAttempts = 8;
            const int retryDelayMs = 125;

            var normalizedPath = outputPath.Replace('\\', '/');
            var temporaryDirectory = Path.Combine(Path.GetDirectoryName(AppleWatchUiBuildState.GetStateFilePath()), "Exports");
            Directory.CreateDirectory(temporaryDirectory);
            var tempPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".swift");
            var backupPath = normalizedPath + ".previous";
            var targetPath = normalizedPath;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.WriteAllText(tempPath, content ?? string.Empty, Encoding.UTF8);
                    var validation = await ValidateSwiftSyntaxAsync(tempPath);
                    if (!validation.Success)
                    {
                        Debug.LogError(
                            string.Format(
                                "[AppleWatch] Generated Swift syntax validation failed; previous output was preserved. Error: {0}",
                                validation.Error));
                        return false;
                    }

                    if (!string.Equals(sourceFingerprint,
                            AppleWatchUiBuildState.ComputeSourceDependenciesSha256(sourcePath), StringComparison.Ordinal))
                    {
                        Debug.LogError("[AppleWatch] Source dependencies changed during export; previous output was preserved. Export again.");
                        return false;
                    }

                    if (File.Exists(targetPath))
                    {
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }

                        File.Replace(tempPath, targetPath, backupPath, true);
                        if (File.Exists(backupPath))
                        {
                            File.Delete(backupPath);
                        }
                    }
                    else
                    {
                        File.Move(tempPath, targetPath);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt >= maxAttempts)
                    {
                        Debug.LogError(
                            string.Format(
                                "[AppleWatch] SwiftUI file write failed after {0} attempts: '{1}'. Error: {2}",
                                maxAttempts,
                                targetPath,
                                ex.Message));
                        return false;
                    }

                    await Task.Delay(retryDelayMs * attempt);
                    continue;
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }

                    if (File.Exists(backupPath) && File.Exists(targetPath))
                    {
                        try { File.Delete(backupPath); } catch { }
                    }
                }
            }

            return false;
        }

        private static async Task<(bool Success, string Error)> ValidateSwiftSyntaxAsync(string swiftPath)
        {
            if (Application.platform != RuntimePlatform.OSXEditor || !File.Exists("/usr/bin/xcrun"))
            {
                Debug.LogWarning("[AppleWatch] xcrun is unavailable; generated Swift syntax validation was skipped.");
                return (true, null);
            }

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/xcrun",
                    Arguments = string.Format(
                        "--sdk watchos swiftc -emit-object -o /dev/null -parse-as-library -warnings-as-errors -swift-version 5 " +
                        "-target arm64_32-apple-watchos11.0 \"{0}\"", swiftPath.Replace("\"", "\\\"")),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var deadline = DateTime.UtcNow.AddSeconds(NativeSwiftCompileTimeoutSeconds);
                while (!process.HasExited && DateTime.UtcNow < deadline)
                    await Task.Delay(50);
                if (!process.HasExited)
                {
                    process.Kill();
                    return (false, "watchOS Swift compilation timed out.");
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                if (process.ExitCode == 0)
                    return (true, null);

                return (false, string.Format("{0}\n{1}", stdout, stderr).Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
