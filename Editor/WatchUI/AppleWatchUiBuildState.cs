using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WatchUI;

namespace AppleWatchController.Editor
{
    /// <summary>Records and checks whether the latest generated Watch UI still matches its inputs.</summary>
    /// <remarks>
    /// Stores disposable state in Library/AppleWatchController/watchui-export.json, not an authored asset.
    /// Freshness includes source and dependency bytes, available dependency .meta files, output bytes,
    /// generator assembly bytes, and the Unity version. A mismatch requires another Watch UI export.
    /// Fingerprints detect changed inputs; they do not prove native compilation or device behavior.
    /// </remarks>
    internal static class AppleWatchUiBuildState
    {
        private const string StateRelativePath = "Library/AppleWatchController/watchui-export.json";

        /// <summary>Stores the paths and hashes needed to validate one completed export.</summary>
        [Serializable]
        private sealed class ExportState
        {
            /// <summary>The source .watch.uxml path supplied when the export state was written.</summary>
            public string watchUiPath;
            /// <summary>The generated Swift path supplied when the export state was written.</summary>
            public string outputPath;
            /// <summary>The SHA-256 hash of the source UXML file bytes.</summary>
            public string watchUiSha256;
            /// <summary>The combined hash of sorted dependency paths, file bytes, and available .meta files.</summary>
            public string sourceDependenciesSha256;
            /// <summary>The SHA-256 hash of the generated Swift file bytes.</summary>
            public string outputSha256;
            /// <summary>The combined hash of the Unity version and the emitter and Editor assemblies.</summary>
            public string generatorSha256;
            /// <summary>The UTC export timestamp in DateTime ticks, recorded for diagnostics rather than freshness checks.</summary>
            public long exportedAtTicks;
        }

        /// <summary>Gets the absolute path of the disposable export-state JSON file.</summary>
        /// <returns>A path under this Unity project's Library/AppleWatchController folder.</returns>
        public static string GetStateFilePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, StateRelativePath);
        }

        /// <summary>Records export state using the source dependency fingerprint computed now.</summary>
        /// <param name="watchUiPath">The existing .watch.uxml source path, absolute or project-relative.</param>
        /// <param name="outputPath">The existing generated Swift path, absolute or project-relative.</param>
        /// <returns>True if state was written; false for invalid inputs, changed inputs, or read/write errors.</returns>
        /// <remarks>Use the snapshot overload when the output was generated from an earlier source read.</remarks>
        public static bool TryWriteExportState(string watchUiPath, string outputPath)
        {
            try
            {
                return TryWriteExportStateForSnapshot(watchUiPath, outputPath,
                    ComputeSourceDependenciesSha256(watchUiPath));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Records an export only if its source snapshot still matches the current dependencies.</summary>
        /// <param name="watchUiPath">The existing .watch.uxml source path.</param>
        /// <param name="outputPath">The existing generated Swift output path.</param>
        /// <param name="sourceFingerprint">The nonempty dependency fingerprint captured before generating the output.</param>
        /// <returns>True after replacing the state file; false if validation or any state write fails.</returns>
        /// <remarks>
        /// Reads and hashes files, creates the state directory, then replaces state through a temporary file.
        /// This method does not create, validate, or modify generated Swift. Call on the Editor main thread.
        /// </remarks>
        public static bool TryWriteExportStateForSnapshot(string watchUiPath, string outputPath, string sourceFingerprint)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(watchUiPath) || string.IsNullOrWhiteSpace(outputPath))
                    return false;

                var resolvedWatchUiPath = ResolveProjectPath(watchUiPath);
                var resolvedOutputPath = ResolveProjectPath(outputPath);
                if (string.IsNullOrWhiteSpace(resolvedWatchUiPath)
                    || string.IsNullOrWhiteSpace(resolvedOutputPath)
                    || !resolvedWatchUiPath.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(resolvedWatchUiPath)
                    || !File.Exists(resolvedOutputPath)
                    || string.IsNullOrWhiteSpace(sourceFingerprint)
                    || !string.Equals(sourceFingerprint, ComputeSourceDependenciesSha256(watchUiPath), StringComparison.Ordinal))
                {
                    return false;
                }

                var statePath = GetStateFilePath();
                var stateDir = Path.GetDirectoryName(statePath);
                if (!string.IsNullOrWhiteSpace(stateDir))
                    Directory.CreateDirectory(stateDir);

                var state = new ExportState
                {
                    watchUiPath = watchUiPath,
                    outputPath = outputPath,
                    watchUiSha256 = ComputeSha256(resolvedWatchUiPath),
                    sourceDependenciesSha256 = sourceFingerprint,
                    outputSha256 = ComputeSha256(resolvedOutputPath),
                    generatorSha256 = ComputeGeneratorSha256(),
                    exportedAtTicks = DateTime.UtcNow.Ticks
                };

                var tempStatePath = statePath + ".tmp";
                File.WriteAllText(tempStatePath, JsonUtility.ToJson(state, true));
                if (File.Exists(statePath))
                {
                    File.Replace(tempStatePath, statePath, null);
                }
                else
                {
                    File.Move(tempStatePath, statePath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Returns the latest output only when every recorded fingerprint still matches.</summary>
        /// <param name="outputPath">Receives the resolved output path on success, or null on failure.</param>
        /// <returns>True for an existing, fresh export; false for missing, invalid, or stale state and file errors.</returns>
        /// <remarks>
        /// Reads files without regenerating output. Changed source dependencies, output edits, generator assemblies,
        /// or Unity version invalidate the state. The recorded timestamp is not a freshness limit.
        /// </remarks>
        public static bool TryReadLatestOutputPath(out string outputPath)
        {
            outputPath = null;

            var statePath = GetStateFilePath();
            if (!File.Exists(statePath))
                return false;

            try
            {
                var raw = File.ReadAllText(statePath);
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                var state = JsonUtility.FromJson<ExportState>(raw);
                if (state == null
                    || string.IsNullOrWhiteSpace(state.watchUiPath)
                    || string.IsNullOrWhiteSpace(state.outputPath)
                    || string.IsNullOrWhiteSpace(state.watchUiSha256)
                    || string.IsNullOrWhiteSpace(state.sourceDependenciesSha256)
                    || string.IsNullOrWhiteSpace(state.outputSha256)
                    || string.IsNullOrWhiteSpace(state.generatorSha256))
                    return false;

                var resolvedWatchUiPath = ResolveProjectPath(state.watchUiPath);
                var resolvedOutputPath = ResolveProjectPath(state.outputPath);
                if (string.IsNullOrWhiteSpace(resolvedWatchUiPath)
                    || string.IsNullOrWhiteSpace(resolvedOutputPath)
                    || !resolvedWatchUiPath.EndsWith(".watch.uxml", StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(resolvedWatchUiPath)
                    || !File.Exists(resolvedOutputPath))
                    return false;

                if (!string.Equals(ComputeSha256(resolvedWatchUiPath), state.watchUiSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(ComputeSourceDependenciesSha256(state.watchUiPath), state.sourceDependenciesSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(ComputeSha256(resolvedOutputPath), state.outputSha256, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(ComputeGeneratorSha256(), state.generatorSha256, StringComparison.OrdinalIgnoreCase))
                    return false;

                outputPath = resolvedOutputPath;
                return true;
            }
            catch
            {
            }

            return false;
        }

        /// <summary>Hashes source dependencies, their paths, and available .meta files in a stable order.</summary>
        /// <param name="watchUiPath">The Watch UI source path used to query Unity's dependency database.</param>
        /// <returns>A lowercase hexadecimal SHA-256 fingerprint.</returns>
        /// <remarks>Includes the preview stylesheet when available. Resolves package dependencies through Package Manager.</remarks>
        /// <exception cref="FileNotFoundException">A dependency cannot be found on disk.</exception>
        /// <exception cref="IOException">A dependency or its .meta file cannot be read.</exception>
        public static string ComputeSourceDependenciesSha256(string watchUiPath)
        {
            var inputs = new StringBuilder();
            foreach (var assetPath in GetSourceDependencyPaths(watchUiPath))
            {
                var physicalPath = ResolveDependencyPath(assetPath);
                if (!File.Exists(physicalPath))
                    throw new FileNotFoundException("Watch UI dependency could not be fingerprinted.", assetPath);
                inputs.Append(assetPath).Append('\n').Append(ComputeSha256(physicalPath)).Append('\n');
                if (File.Exists(physicalPath + ".meta"))
                    inputs.Append(ComputeSha256(physicalPath + ".meta")).Append('\n');
            }

            using var sha256 = SHA256.Create();
            return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(inputs.ToString())));
        }

        /// <summary>Lists the source and preview-style dependency paths known to Unity.</summary>
        /// <param name="watchUiPath">The source path, absolute or project-relative.</param>
        /// <returns>Distinct Assets and Packages paths sorted with ordinal comparison.</returns>
        /// <remarks>Call on Unity's main thread. This query does not import or verify the listed files.</remarks>
        public static string[] GetSourceDependencyPaths(string watchUiPath)
        {
            var assetPath = NormalizeToAssetRelativePath(watchUiPath);
            var previewStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                WatchPackagePaths.AssetPath("Editor/WatchUI/Resources/WatchUiPreview.uss"))
                ?? Resources.Load<StyleSheet>("WatchUiPreview");
            var previewStylePath = AssetDatabase.GetAssetPath(previewStyle);
            var roots = string.IsNullOrEmpty(previewStylePath)
                ? new[] { assetPath }
                : new[] { assetPath, previewStylePath };
            return AssetDatabase.GetDependencies(roots, true).Concat(roots)
                .Where(path => !string.IsNullOrWhiteSpace(path)
                    && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        private static string ResolveDependencyPath(string assetPath)
        {
            var projectPath = ResolveProjectPath(assetPath);
            if (File.Exists(projectPath) || !assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return projectPath;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            return package == null ? projectPath : Path.Combine(package.resolvedPath,
                assetPath.Substring(package.assetPath.Length).TrimStart('/'));
        }

        private static string ComputeSha256(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(stream);
            return ToHex(hash);
        }

        private static string ToHex(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ComputeGeneratorSha256()
        {
            var assemblyPaths = new[] { typeof(WatchUiSwiftEmitter).Assembly.Location, typeof(AppleWatchUiBuildState).Assembly.Location };
            var inputs = new StringBuilder(Application.unityVersion);
            foreach (var assemblyPath in assemblyPaths)
            {
                if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                    throw new FileNotFoundException("WatchUI exporter assembly could not be fingerprinted.", assemblyPath);
                inputs.Append('\n').Append(ComputeSha256(assemblyPath));
            }

            using var sha256 = SHA256.Create();
            return ToHex(sha256.ComputeHash(Encoding.UTF8.GetBytes(inputs.ToString())));
        }

        /// <summary>Resolves relative paths against the Unity project root.</summary>
        /// <param name="candidatePath">An absolute or project-relative path.</param>
        /// <returns>The path with normalized separators and relative paths expanded, or null for blank input.</returns>
        /// <remarks>Does not require the path to exist or remain inside the project. Rooted paths are not canonicalized here.</remarks>
        public static string ResolveProjectPath(string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
                return null;

            var normalized = candidatePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
                return normalized;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var combined = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            return combined;
        }

        /// <summary>Removes the project-root prefix from a path when that prefix matches.</summary>
        /// <param name="absoluteOrRelativePath">An absolute or project-relative path.</param>
        /// <returns>The path after the project-root prefix, or null for blank input or a nonmatching prefix.</returns>
        /// <remarks>
        /// Despite its name, this helper does not require an Assets path or verify directory-boundary containment.
        /// Do not use it to validate untrusted paths. The export entry point applies its own Assets-folder check.
        /// </remarks>
        public static string NormalizeToAssetRelativePath(string absoluteOrRelativePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var normalized = absoluteOrRelativePath?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (!Path.IsPathRooted(normalized))
            {
                normalized = Path.GetFullPath(Path.Combine(projectRoot, normalized));
                normalized = normalized.Replace('\\', '/');
            }
            else
            {
                normalized = normalized.Replace('\\', '/');
            }

            if (!normalized.StartsWith(projectRoot.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return null;

            var relative = normalized.Substring(projectRoot.Length);
            if (relative.StartsWith('/'))
                relative = relative.TrimStart('/');

            return relative;
        }

        /// <summary>Normalizes path separators and resolves a nonblank output path against the project root.</summary>
        /// <param name="outputPath">An absolute or project-relative path.</param>
        /// <returns>The resolved path, or null for blank input.</returns>
        /// <remarks>Does not check file existence or restrict the path to this project.</remarks>
        public static string NormalizeToProjectPath(string outputPath)
        {
            var normalized = outputPath?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var resolved = ResolveProjectPath(normalized);
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }
    }
}
