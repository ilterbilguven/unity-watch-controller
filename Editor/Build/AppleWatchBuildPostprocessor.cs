using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using XcodePbxProject = UnityEditor.iOS.Xcode.PBXProject;
using XcodePbxSourceTree = UnityEditor.iOS.Xcode.PBXSourceTree;

namespace AppleWatchController.Editor
{
    /// <summary>Enforces the project's Swift-only iOS build settings and adds its companion Watch app.</summary>
    /// <remarks>
    /// Unity calls this Editor-only processor during builds. Non-iOS builds return without changes.
    /// The supported export has one executable AppleWatchApp target, not a legacy app/extension pair.
    /// Edit the authored Watch UXML, USS, and C# generator, then export again; generated Swift is build output.
    /// </remarks>
    public sealed class AppleWatchBuildPostprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        /// <summary>Gets the callback order, zero, used to order this processor among Unity build callbacks.</summary>
        public int callbackOrder => 0;

        private const string MinIosDeploymentTarget = "18.0";
        private const string MinWatchDeploymentTarget = "11.0";
        private const string SwiftTreatWarningsAsErrorsValue = "YES";
        private const string WatchSupportRelativePath = "Libraries/AppleWatchController/WatchApp";
        private const string WatchExtensionInfoPlistName = "AppleWatchExtension-Info.plist";
        private const string WatchAppInfoPlistName = "AppleWatchApp-Info.plist";
        private const string WatchAppName = "AppleWatchApp";
        private const string WatchExtensionName = "AppleWatchExtension";
        private const string WatchExtensionBundleSuffix = ".watchkitextension";
        private const string WatchAppBundleSuffix = ".watchkitapp";
        private const string ModernWatchProductType = "com.apple.product-type.application";
        private const string EmbedWatchContentPhaseName = "Embed Watch Content";

        /// <summary>Checks iOS plugins and sets Swift project type, iOS 18 deployment, and ARM64 architecture.</summary>
        /// <param name="report">The non-null build report supplied by Unity.</param>
        /// <remarks>
        /// Rejects .m and .mm source files under the package-owned native root, not consumer plugins.
        /// Changes PlayerSettings for iOS builds; it does not restore the previous values afterward.
        /// Requires an Editor with writable Swift project and iOS architecture APIs.
        /// </remarks>
        /// <exception cref="BuildFailedException">An Objective-C plugin exists or a required setting cannot be applied and verified.</exception>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            EnsureNoObjectiveCFiles();
            EnsureSwiftXcodeProjectType();
            EnsureiOSTargetVersion();
            EnsureArm64Architecture();
        }

        /// <summary>Creates or updates the Watch target, embeds it in the iOS app, and patches deployment settings.</summary>
        /// <param name="report">The non-null Unity build report whose output path contains an Xcode project.</param>
        /// <remarks>
        /// Requires a fresh fingerprinted Watch UI export and the installed iOS Xcode project APIs.
        /// Copies generated Swift into the build, writes Watch support files, and updates the PBX project.
        /// Assigns the generated entry point to the Watch executable, not the iPhone targets.
        /// Sets Swift 5 and warnings-as-errors, iOS 18 for iOS targets, and watchOS 11 for the Watch app.
        /// Rejects stale or ambiguous Watch target structures; use a clean Replace export in that case.
        /// A successful callback does not compile, sign, or install the resulting Xcode project.
        /// </remarks>
        /// <exception cref="BuildFailedException">Required source, target, bundle identifier, settings, or Xcode APIs are unavailable.</exception>
        /// <exception cref="IOException">The exported project or generated source cannot be read or written.</exception>
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            EnsureWatchTargets(report.summary.outputPath);
            PatchSwiftDeploymentTargets(report.summary.outputPath);
        }

        private static void EnsureNoObjectiveCFiles()
        {
            EnsureNoObjectiveCFilesAtPath(WatchPackagePaths.FilePath("Runtime/Plugins"));
        }

        private static void EnsureNoObjectiveCFilesAtPath(string pluginRoot)
        {
            if (!Directory.Exists(pluginRoot))
                return;

            var objectiveCFiles = Directory.EnumerateFiles(pluginRoot, "*.*", SearchOption.AllDirectories)
                .Where(file =>
                    file.EndsWith(".m", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".mm", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (objectiveCFiles.Length > 0)
            {
                throw new BuildFailedException(
                    $"[AppleWatch] Objective-C source detected in {pluginRoot}. Package-owned native integration requires Swift-only sources. " +
                    $"Detected files:\n - {string.Join("\n - ", objectiveCFiles)}");
            }
        }

        private static void EnsureiOSTargetVersion()
        {
            try
            {
                PlayerSettings.iOS.targetOSVersionString = MinIosDeploymentTarget;
                if (!string.Equals(PlayerSettings.iOS.targetOSVersionString, MinIosDeploymentTarget, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unity reported iOS deployment target '{PlayerSettings.iOS.targetOSVersionString}' after setting '{MinIosDeploymentTarget}'.");
                }
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    $"[AppleWatch] Could not enforce iOS deployment target {MinIosDeploymentTarget}: {ex.Message}");
            }
        }

        private static void EnsureSwiftXcodeProjectType()
        {
            try
            {
                var property = typeof(PlayerSettings).GetProperty(
                    "xcodeProjectType",
                    BindingFlags.Static | BindingFlags.Public);
                if (property == null || !property.CanWrite)
                {
                    throw new MissingMemberException(
                        "PlayerSettings.xcodeProjectType is unavailable or read-only in this Unity Editor.");
                }

                var targetValue = ResolveSwiftProjectTypeValue(property.PropertyType);
                if (targetValue == null)
                    throw new InvalidOperationException("Could not resolve the Swift Xcode project enum value.");

                var currentValue = property.GetValue(null);
                if (Equals(currentValue, targetValue))
                    return;

                property.SetValue(null, targetValue);
                var appliedValue = property.GetValue(null);
                if (!Equals(appliedValue, targetValue))
                {
                    throw new InvalidOperationException(
                        $"Unity reported Xcode project type '{appliedValue}' after setting Swift '{targetValue}'.");
                }

                Debug.Log($"[AppleWatch] Forced PlayerSettings.xcodeProjectType to Swift ({property.PropertyType.Name}={targetValue}).");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[AppleWatch] Could not enforce PlayerSettings.xcodeProjectType = Swift. " +
                    $"This project requires Swift iOS plugins. Error: {ex.Message}");
                throw new BuildFailedException("Apple Watch build requires Swift-only iOS plugin projects.");
            }
        }

        private static object ResolveSwiftProjectTypeValue(Type propertyType)
        {
            if (propertyType == null)
                return null;

            if (propertyType == typeof(int))
            {
                return 1;
            }

            if (!propertyType.IsEnum)
                throw new InvalidOperationException($"Unsupported Xcode project type property: {propertyType.FullName}.");

            var names = Enum.GetNames(propertyType);
            var values = Enum.GetValues(propertyType);
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i].Equals("Swift", StringComparison.OrdinalIgnoreCase))
                    return values.GetValue(i);
                if (names[i].Contains("Swift"))
                    return values.GetValue(i);
            }

            throw new InvalidOperationException($"The Swift Xcode project value is not defined by {propertyType.FullName}.");
        }

        private static void EnsureArm64Architecture()
        {
            try
            {
                var playerSettingsType = typeof(PlayerSettings);
                var setArch = playerSettingsType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => string.Equals(method.Name, "SetArchitecture", StringComparison.Ordinal))
                    .Select(method => new { Method = method, Parameters = method.GetParameters() })
                    .Where(candidate =>
                        candidate.Parameters.Length == 2 &&
                        candidate.Parameters[0].ParameterType == typeof(BuildTargetGroup))
                    .OrderBy(candidate => candidate.Parameters[1].ParameterType == typeof(int) ? 0 : 1)
                    .Select(candidate => candidate.Method)
                    .FirstOrDefault();

                if (setArch == null)
                    throw new MissingMethodException("PlayerSettings.SetArchitecture is unavailable.");

                var paramTypes = setArch.GetParameters().Select(p => p.ParameterType).ToArray();
                if (paramTypes.Length != 2 || paramTypes[0] != typeof(BuildTargetGroup))
                    throw new MissingMethodException("PlayerSettings.SetArchitecture has an unsupported signature.");

                var archArg = ResolveArm64Value(paramTypes[1]);
                setArch.Invoke(null, new[] { (object)BuildTargetGroup.iOS, archArg });

                var getArch = playerSettingsType.GetMethod(
                    "GetArchitecture",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(BuildTargetGroup) },
                    null);
                if (getArch == null)
                    throw new MissingMethodException("PlayerSettings.GetArchitecture is unavailable for ARM64 verification.");

                var appliedArchitecture = getArch.Invoke(null, new object[] { BuildTargetGroup.iOS });
                if (!IsArm64Value(appliedArchitecture))
                {
                    throw new InvalidOperationException(
                        $"Unity reported iOS architecture '{appliedArchitecture}' after setting ARM64.");
                }
            }
            catch (Exception ex)
            {
                throw new BuildFailedException($"[AppleWatch] Could not enforce iOS ARM64 architecture: {ex.Message}");
            }
        }

        private static object ResolveArm64Value(Type architectureType)
        {
            if (architectureType == null)
                throw new ArgumentNullException(nameof(architectureType));

            if (!architectureType.IsEnum)
                return Convert.ChangeType(1, architectureType, CultureInfo.InvariantCulture);

            foreach (var value in Enum.GetValues(architectureType))
            {
                if (IsArm64Value(value))
                    return value;
            }

            throw new InvalidOperationException($"ARM64 is not defined by {architectureType.FullName}.");
        }

        private static bool IsArm64Value(object value)
        {
            if (value == null)
                return false;

            var name = value.ToString()?.Replace("_", string.Empty).Replace("-", string.Empty);
            if (!string.IsNullOrWhiteSpace(name) && name.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
            }
            catch
            {
                return false;
            }
        }

        private static void PatchSwiftDeploymentTargets(string buildPath)
        {
            var pbxPath = ResolveXcodeProjectPbxPath(buildPath);

            var pbxType = ResolvePbxProjectType();
            if (pbxType == null)
                throw new BuildFailedException("[AppleWatch] PBXProject type not found in the installed iOS Xcode extension.");

            var pbx = Activator.CreateInstance(pbxType);
            var readFromString = pbxType.GetMethod("ReadFromString");
            var writeToString = pbxType.GetMethod("WriteToString");
            var setBuildProperty = pbxType.GetMethod("SetBuildProperty", new[] { typeof(string), typeof(string), typeof(string) });
            var targetGuidByName = pbxType.GetMethod("TargetGuidByName", new[] { typeof(string) });

            if (readFromString == null || writeToString == null || setBuildProperty == null || targetGuidByName == null)
                throw new BuildFailedException("[AppleWatch] PBXProject API is missing mandatory read/write/target/build-property methods.");

            var pbxText = File.ReadAllText(pbxPath);
            readFromString.Invoke(pbx, new object[] { pbxText });

            var targets = EnumerateRelevantTargetGuids(pbx, pbxType);

            var setIosTargets = 0;
            var setWatchTargets = 0;
            var executableWatchTargetGuid = targetGuidByName.Invoke(pbx, new object[] { WatchAppName }) as string;
            if (string.IsNullOrWhiteSpace(executableWatchTargetGuid))
                throw new BuildFailedException($"[AppleWatch] Executable Watch target '{WatchAppName}' was not found after provisioning.");

            foreach (var targetObj in targets)
            {
                var targetGuid = targetObj as string;
                if (string.IsNullOrWhiteSpace(targetGuid))
                    continue;

                var isWatchTarget = string.Equals(targetGuid, executableWatchTargetGuid, StringComparison.Ordinal);

                setBuildProperty.Invoke(
                    pbx,
                    new object[] { targetGuid, "SWIFT_VERSION", "5.0" });

                if (isWatchTarget)
                {
                    setBuildProperty.Invoke(
                        pbx,
                        new object[] { targetGuid, "WATCHOS_DEPLOYMENT_TARGET", MinWatchDeploymentTarget });
                }
                else
                {
                    setBuildProperty.Invoke(
                        pbx,
                        new object[] { targetGuid, "IPHONEOS_DEPLOYMENT_TARGET", MinIosDeploymentTarget });
                }
                setBuildProperty.Invoke(
                    pbx,
                    new object[] { targetGuid, "SWIFT_TREAT_WARNINGS_AS_ERRORS", SwiftTreatWarningsAsErrorsValue });

                if (isWatchTarget)
                    setWatchTargets += 1;
                else
                    setIosTargets += 1;
            }

            if (setIosTargets == 0)
                throw new BuildFailedException("[AppleWatch] No iOS target was found while applying iOS 18 build settings.");

            if (setWatchTargets != 1)
            {
                throw new BuildFailedException(
                    $"[AppleWatch] Expected exactly one executable watchOS target while applying watchOS 11 settings; found {setWatchTargets}.");
            }

            var serializedProject = writeToString.Invoke(pbx, null) as string;
            if (string.IsNullOrWhiteSpace(serializedProject))
                throw new BuildFailedException("[AppleWatch] PBXProject.WriteToString returned empty output.");

            File.WriteAllText(pbxPath, serializedProject);
            Debug.Log(
                $"[AppleWatch] Patched PBX ({setIosTargets} iOS targets, {setWatchTargets} watch target): {pbxPath}");
        }

        private static void EnsureWatchTargets(string buildPath)
        {
            var pbxPath = ResolveXcodeProjectPbxPath(buildPath);
            var pbxType = ResolvePbxProjectType();
            if (pbxType == null)
                throw new BuildFailedException("[AppleWatch] PBXProject type not found in the installed iOS Xcode extension.");

            var pbx = Activator.CreateInstance(pbxType);
            var readFromString = pbxType.GetMethod("ReadFromString");
            var writeToString = pbxType.GetMethod("WriteToString");
            if (readFromString == null || writeToString == null)
                throw new BuildFailedException("[AppleWatch] PBXProject read/write methods are unavailable.");

            var getBuildProperty = pbxType.GetMethod("GetBuildPropertyForAnyConfig", new[] { typeof(string), typeof(string) });
            var getMainTarget = pbxType.GetMethod("GetUnityMainTargetGuid");
            var addTarget = pbxType.GetMethod("AddTarget", new[] { typeof(string), typeof(string), typeof(string) });
            var setBuildProperty = pbxType.GetMethod("SetBuildProperty", new[] { typeof(string), typeof(string), typeof(string) });

            if (getBuildProperty == null || getMainTarget == null || addTarget == null || setBuildProperty == null)
            {
                throw new BuildFailedException(
                    "[AppleWatch] Installed PBXProject API does not expose the mandatory modern watchOS target methods.");
            }

            var pbxText = File.ReadAllText(pbxPath);
            readFromString.Invoke(pbx, new object[] { pbxText });

            var existingTargets = EnumerateRelevantTargetGuids(pbx, pbxType);
            var watchExecutableTargetGuid = FindWatchExecutableTargetGuid(pbx, pbxType);
            var watchSupportDir = Path.Combine(buildPath, WatchSupportRelativePath);
            Directory.CreateDirectory(watchSupportDir);

            if (!string.IsNullOrWhiteSpace(watchExecutableTargetGuid))
            {
                if (!HasGeneratedSwiftSourceAvailable())
                {
                    throw new BuildFailedException(
                        "[AppleWatch] Existing watch targets detected but no generated watch UI source is available. " +
                        "Use Tools > AppleWatch > Export Selected Watch UI to SwiftUI before building iOS.");
                }

                if (!AddWatchGeneratedSwiftSourceToTarget(
                        pbx,
                        pbxType,
                        watchSupportDir,
                        watchExecutableTargetGuid,
                        existingTargets,
                        getBuildProperty))
                {
                    throw new BuildFailedException(
                        "[AppleWatch] Could not attach generated @main Swift source to the executable Watch target.");
                }

                EnsureModernWatchEmbedding(pbx, pbxType, getMainTarget, watchExecutableTargetGuid);
                WritePbxProjectOrThrow(pbxPath, pbx, writeToString);
                Debug.Log(string.Format("[AppleWatch] Reconciled executable watchOS target: {0}", watchExecutableTargetGuid));

                return;
            }

            if (HasWatchTarget(pbx, getBuildProperty, existingTargets))
            {
                throw new BuildFailedException(
                    "[AppleWatch] Watch-related PBX targets exist, but no unique executable Watch target could be resolved. " +
                    "Remove the stale/legacy Watch graph and perform a clean Replace export.");
            }

            var mainTargetGuid = getMainTarget.Invoke(pbx, null) as string;
            if (string.IsNullOrWhiteSpace(mainTargetGuid))
                throw new BuildFailedException("[AppleWatch] Could not determine the main iOS target GUID.");

            var iosBundleId = GetApplicationIdentifierSafe();
            var watchAppBundleId = string.Format("{0}{1}", iosBundleId, WatchAppBundleSuffix);
            var appInfoPlist = WriteWatchInfoPlist(
                Path.Combine(watchSupportDir, WatchAppInfoPlistName),
                WatchAppName,
                watchAppBundleId,
                iosBundleId,
                isWatchExtension: false);
            if (string.IsNullOrWhiteSpace(appInfoPlist))
                throw new BuildFailedException("[AppleWatch] Could not write the modern Watch app Info.plist.");

            var watchAppTargetGuid = addTarget.Invoke(
                pbx,
                new object[] { WatchAppName, "app", ModernWatchProductType }) as string;
            if (string.IsNullOrWhiteSpace(watchAppTargetGuid))
                throw new BuildFailedException("[AppleWatch] PBXProject.AddTarget failed to create the modern watchOS app target.");

            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "PRODUCT_NAME", WatchAppName);
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "PRODUCT_BUNDLE_IDENTIFIER", watchAppBundleId);
            SetBuildProperty(
                pbx,
                setBuildProperty,
                watchAppTargetGuid,
                "INFOPLIST_FILE",
                BuildProjectRelativePath(buildPath, appInfoPlist));
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "SDKROOT", "watchos");
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "ARCHS", "$(ARCHS_STANDARD)");
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "SUPPORTED_PLATFORMS", "watchos watchsimulator");
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "TARGETED_DEVICE_FAMILY", "4");
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "WATCHOS_DEPLOYMENT_TARGET", MinWatchDeploymentTarget);
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "SWIFT_VERSION", "5.0");
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "SWIFT_TREAT_WARNINGS_AS_ERRORS", SwiftTreatWarningsAsErrorsValue);
            SetBuildProperty(pbx, setBuildProperty, watchAppTargetGuid, "SKIP_INSTALL", "YES");

            var targetsWithModernWatch = existingTargets.Concat(new[] { watchAppTargetGuid }).ToArray();
            if (!AddWatchGeneratedSwiftSourceToTarget(
                    pbx,
                    pbxType,
                    watchSupportDir,
                    watchAppTargetGuid,
                    targetsWithModernWatch,
                    getBuildProperty))
            {
                throw new BuildFailedException(
                    "[AppleWatch] Could not attach generated @main Swift source to the modern watchOS app target.");
            }

            EnsureModernWatchEmbedding(pbx, pbxType, getMainTarget, watchAppTargetGuid);
            WritePbxProjectOrThrow(pbxPath, pbx, writeToString);
            Debug.Log("[AppleWatch] Created one modern single-target watchOS app in the Xcode project.");
        }

        private static void SetBuildProperty(
            object pbx,
            MethodInfo setBuildPropertyMethod,
            string targetGuid,
            string propertyName,
            string value)
        {
            if (pbx == null || setBuildPropertyMethod == null || string.IsNullOrWhiteSpace(targetGuid))
                throw new BuildFailedException($"[AppleWatch] Cannot set mandatory PBX property '{propertyName}'.");

            setBuildPropertyMethod.Invoke(pbx, new object[] { targetGuid, propertyName, value });
        }

        private static void WritePbxProjectOrThrow(string pbxPath, object pbx, MethodInfo writeToStringMethod)
        {
            var serializedProject = writeToStringMethod?.Invoke(pbx, null) as string;
            if (string.IsNullOrWhiteSpace(serializedProject))
                throw new BuildFailedException("[AppleWatch] PBXProject.WriteToString returned empty output.");

            File.WriteAllText(pbxPath, serializedProject);
        }

        private static void EnsureModernWatchEmbedding(
            object pbx,
            Type pbxType,
            MethodInfo getMainTargetMethod,
            string watchTargetGuid)
        {
            var mainTargetGuid = getMainTargetMethod?.Invoke(pbx, null) as string;
            if (string.IsNullOrWhiteSpace(mainTargetGuid))
                throw new BuildFailedException("[AppleWatch] Could not resolve the main iOS target for Watch embedding.");

            var getProductFileRef = pbxType.GetMethod("GetTargetProductFileRef", new[] { typeof(string) });
            var addTargetDependency = pbxType.GetMethod("AddTargetDependency", new[] { typeof(string), typeof(string) });
            var getCopyFilesPhase = pbxType.GetMethod(
                "GetCopyFilesBuildPhaseByTarget",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
            var addCopyFilesPhase = pbxType.GetMethod(
                "AddCopyFilesBuildPhase",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string) });
            var addFileToBuildSection = pbxType.GetMethod(
                "AddFileToBuildSection",
                new[] { typeof(string), typeof(string), typeof(string) });
            var removeFileFromBuild = pbxType.GetMethod("RemoveFileFromBuild", new[] { typeof(string), typeof(string) });

            if (getProductFileRef == null || addTargetDependency == null || getCopyFilesPhase == null ||
                addCopyFilesPhase == null || addFileToBuildSection == null || removeFileFromBuild == null)
            {
                throw new BuildFailedException(
                    "[AppleWatch] Installed PBXProject API cannot create an idempotent modern Watch embed graph.");
            }

            var productFileGuid = getProductFileRef.Invoke(pbx, new object[] { watchTargetGuid }) as string;
            if (string.IsNullOrWhiteSpace(productFileGuid))
                throw new BuildFailedException("[AppleWatch] Modern Watch target has no product file reference.");

            const string embedDestination = "$(CONTENTS_FOLDER_PATH)/Watch";
            const string embedSubfolderSpec = "16";
            var embedPhaseGuid = getCopyFilesPhase.Invoke(
                pbx,
                new object[] { mainTargetGuid, EmbedWatchContentPhaseName, embedDestination, embedSubfolderSpec }) as string;
            if (string.IsNullOrWhiteSpace(embedPhaseGuid))
            {
                embedPhaseGuid = addCopyFilesPhase.Invoke(
                    pbx,
                    new object[]
                    {
                        mainTargetGuid,
                        EmbedWatchContentPhaseName,
                        embedDestination,
                        embedSubfolderSpec
                    }) as string;
                if (string.IsNullOrWhiteSpace(embedPhaseGuid))
                    throw new BuildFailedException("[AppleWatch] Could not create the Embed Watch Content phase.");

                addTargetDependency.Invoke(pbx, new object[] { mainTargetGuid, watchTargetGuid });
            }

            removeFileFromBuild.Invoke(pbx, new object[] { mainTargetGuid, productFileGuid });
            addFileToBuildSection.Invoke(pbx, new object[] { mainTargetGuid, embedPhaseGuid, productFileGuid });
        }

        private static bool AddWatchGeneratedSwiftSourceToTarget(
            object pbx,
            Type pbxType,
            string watchSupportDir,
            string executableWatchTargetGuid,
            IEnumerable<string> allTargetGuids,
            MethodInfo getBuildPropertyMethod)
        {
            var generatedSourceFromAssets = FindGeneratedSwiftSourcePath();
            if (string.IsNullOrWhiteSpace(generatedSourceFromAssets))
            {
                Debug.LogWarning("[AppleWatch] No generated watch Swift source found. Export a .watch.uxml source first.");
                return false;
            }

            if (!File.Exists(generatedSourceFromAssets))
            {
                Debug.LogWarning(string.Format("[AppleWatch] Generated Swift source missing: {0}", generatedSourceFromAssets));
                return false;
            }

            var targetDir = Path.Combine(watchSupportDir, "Generated");
            Directory.CreateDirectory(targetDir);
            var destinationFileName = Path.GetFileName(generatedSourceFromAssets);
            if (string.IsNullOrWhiteSpace(destinationFileName))
                return false;

            var destinationPath = Path.Combine(targetDir, destinationFileName);
            File.Copy(generatedSourceFromAssets, destinationPath, true);

            if (pbx is not XcodePbxProject typedPbx)
            {
                Debug.LogWarning("[AppleWatch] PBX project instance has an unexpected runtime type.");
                return false;
            }

            var projectRelativePath = Path.Combine(
                    WatchSupportRelativePath,
                    "Generated",
                    destinationFileName)
                .Replace('\\', '/');
            var fileGuid = typedPbx.FindFileGuidByRealPath(projectRelativePath);
            if (string.IsNullOrWhiteSpace(fileGuid))
            {
                fileGuid = typedPbx.AddFile(projectRelativePath, projectRelativePath, XcodePbxSourceTree.Source);
            }

            if (string.IsNullOrWhiteSpace(fileGuid))
            {
                Debug.LogWarning("[AppleWatch] Could not register generated watch Swift source in PBX project.");
                return false;
            }

            foreach (var targetGuid in allTargetGuids ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(targetGuid) ||
                    string.Equals(targetGuid, executableWatchTargetGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                var targetName = GetBuildPropertyOrEmpty(pbx, getBuildPropertyMethod, targetGuid, "PRODUCT_NAME");
                var productType = GetBuildPropertyOrEmpty(pbx, getBuildPropertyMethod, targetGuid, "PRODUCT_TYPE");
                if (IsWatchTarget(productType, targetName))
                    typedPbx.RemoveFileFromBuild(targetGuid, fileGuid);
            }

            var sourcesGuid = typedPbx.GetSourcesBuildPhaseByTarget(executableWatchTargetGuid);
            if (string.IsNullOrWhiteSpace(sourcesGuid))
            {
                sourcesGuid = typedPbx.AddSourcesBuildPhase(executableWatchTargetGuid);
            }

            if (string.IsNullOrWhiteSpace(sourcesGuid))
            {
                Debug.LogWarning("[AppleWatch] Modern Watch target has no sources build phase.");
                return false;
            }

            try
            {
                typedPbx.RemoveFileFromBuild(executableWatchTargetGuid, fileGuid);
                typedPbx.AddFileToBuild(executableWatchTargetGuid, fileGuid);

                Debug.Log(string.Format("[AppleWatch] Added watch Swift source to target: {0}", fileGuid));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(string.Format("[AppleWatch] watch extension source add failed: {0}", ex.Message));
                return false;
            }
        }

        private static string BuildProjectRelativePath(string buildRoot, string absoluteFilePath)
        {
            var normalizedBuildRoot = buildRoot.Replace('\\', '/').TrimEnd('/');
            var normalizedFile = absoluteFilePath.Replace('\\', '/');

            if (normalizedFile.StartsWith(normalizedBuildRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedFile.Substring(normalizedBuildRoot.Length);
                return relative.TrimStart('/');
            }

            return Path.Combine(WatchSupportRelativePath, "Generated", Path.GetFileName(absoluteFilePath)).Replace('\\', '/');
        }

        private static string FindGeneratedSwiftSourcePath()
        {
            if (AppleWatchUiBuildState.TryReadLatestOutputPath(out var manifestPath)
                && !string.IsNullOrWhiteSpace(manifestPath)
                && File.Exists(manifestPath))
            {
                return manifestPath;
            }

            Debug.LogError(
                "[AppleWatch] No fresh fingerprinted Watch UI export state was found. " +
                "Run watch_ui_export for the intended .watch.uxml source before building iOS.");
            return null;
        }

        private static bool HasGeneratedSwiftSourceAvailable()
        {
            var sourcePath = FindGeneratedSwiftSourcePath();
            return !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath);
        }

        private static string GetApplicationIdentifierSafe()
        {
            var id = GetApplicationIdentifierByNamedBuildTarget();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id.Trim();
            }

            id = GetApplicationIdentifierByBuildTargetGroup();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id.Trim();
            }

            throw new BuildFailedException(
                "[AppleWatch] The iOS application identifier is empty. Configure a valid iOS bundle identifier before export.");
        }

        private static string GetApplicationIdentifierByNamedBuildTarget()
        {
            var methods = typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method =>
                    method.Name == "GetApplicationIdentifier" &&
                    method.GetParameters().Length == 1 &&
                    string.Equals(method.GetParameters()[0].ParameterType.Name, "NamedBuildTarget", StringComparison.OrdinalIgnoreCase));

            var iOSNamedTarget = ResolveIosNamedBuildTargetValue();
            if (iOSNamedTarget == null)
            {
                return null;
            }

            foreach (var method in methods)
            {
                try
                {
                    var identifier = method.Invoke(null, new[] { iOSNamedTarget }) as string;
                    if (!string.IsNullOrWhiteSpace(identifier))
                    {
                        return identifier;
                    }
                }
                catch
                {
                    // Ignore method-shape mismatches across Unity versions.
                }
            }

            return null;
        }

        private static object ResolveIosNamedBuildTargetValue()
        {
            var methodCandidates = typeof(PlayerSettings).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "GetApplicationIdentifier" && method.GetParameters().Length == 1);

            var namedBuildTargetType = methodCandidates
                .Select(method => method.GetParameters()[0].ParameterType)
                .FirstOrDefault(type => string.Equals(type.Name, "NamedBuildTarget", StringComparison.OrdinalIgnoreCase));

            if (namedBuildTargetType == null)
            {
                return null;
            }

            var bindingFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var field = namedBuildTargetType.GetField("iOS", bindingFlags)
                        ?? namedBuildTargetType.GetField("IOS", bindingFlags);
            if (field != null)
            {
                return field.GetValue(null);
            }

            var property = namedBuildTargetType.GetProperty("iOS", bindingFlags)
                           ?? namedBuildTargetType.GetProperty("IOS", bindingFlags);
            if (property != null)
            {
                return property.GetValue(null);
            }

            if (namedBuildTargetType.IsEnum)
            {
                foreach (var value in Enum.GetValues(namedBuildTargetType))
                {
                    if (string.Equals(value?.ToString(), "iOS", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(value?.ToString(), "IOS", StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string GetApplicationIdentifierByBuildTargetGroup()
        {
            try
            {
                var legacyMethod = typeof(PlayerSettings).GetMethod(
                    "GetApplicationIdentifier",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(BuildTargetGroup) },
                    null);

                if (legacyMethod == null)
                {
                    return null;
                }

                return legacyMethod.Invoke(null, new object[] { BuildTargetGroup.iOS }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string WriteWatchInfoPlist(
            string outputPath,
            string bundleName,
            string bundleId,
            string companionId,
            bool isWatchExtension)
        {
            try
            {
                var content = new System.Text.StringBuilder();
                content.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                content.AppendLine(
                    "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
                content.AppendLine("<plist version=\"1.0\">");
                content.AppendLine("<dict>");
                content.AppendLine("  <key>CFBundleExecutable</key>");
                content.AppendLine("  <string>$(EXECUTABLE_NAME)</string>");
                content.AppendLine("  <key>CFBundlePackageType</key>");
                content.AppendLine("  <string>APPL</string>");
                content.AppendLine("  <key>CFBundleShortVersionString</key>");
                content.AppendLine($"  <string>{EscapePlistText(PlayerSettings.bundleVersion)}</string>");
                content.AppendLine("  <key>CFBundleVersion</key>");
                content.AppendLine($"  <string>{EscapePlistText(PlayerSettings.iOS.buildNumber)}</string>");
                content.AppendLine($"  <key>CFBundleDisplayName</key>");
                content.AppendLine($"  <string>{EscapePlistText(bundleName)}</string>");
                content.AppendLine($"  <key>CFBundleIdentifier</key>");
                content.AppendLine($"  <string>{EscapePlistText(bundleId)}</string>");
                content.AppendLine("  <key>NSMicrophoneUsageDescription</key>");
                content.AppendLine("  <string>Use Apple Watch microphone input for game controls and recordings.</string>");
                content.AppendLine("  <key>NSMotionUsageDescription</key>");
                content.AppendLine("  <string>Use Apple Watch motion data for game controls.</string>");

                if (isWatchExtension)
                {
                    content.AppendLine("  <key>NSExtension</key>");
                    content.AppendLine("  <dict>");
                    content.AppendLine("    <key>NSExtensionPointIdentifier</key>");
                    content.AppendLine("    <string>com.apple.watchkit</string>");
                    content.AppendLine("    <key>NSExtensionAttributes</key>");
                    content.AppendLine("    <dict>");
                    content.AppendLine("      <key>WKAppBundleIdentifier</key>");
                    content.AppendLine($"      <string>{EscapePlistText(companionId)}</string>");
                    content.AppendLine("    </dict>");
                    content.AppendLine("  </dict>");
                }
                else
                {
                    content.AppendLine("  <key>WKApplication</key>");
                    content.AppendLine("  <true/>");
                    content.AppendLine("  <key>WKCompanionAppBundleIdentifier</key>");
                    content.AppendLine($"  <string>{EscapePlistText(companionId)}</string>");
                }

                content.AppendLine("</dict>");
                content.AppendLine("</plist>");

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDir))
                    Directory.CreateDirectory(outputDir);

                File.WriteAllText(outputPath, content.ToString());
                return outputPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(string.Format("[AppleWatch] Failed to write Watch Info.plist '{0}': {1}", outputPath, ex.Message));
                return null;
            }
        }

        private static string EscapePlistText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;")
                .Replace("\n", " ");
        }

        private static List<string> EnumerateRelevantTargetGuids(object pbx, Type pbxType)
        {
            var result = new List<string>();
            var targetGetterNames = new[]
            {
                "GetUnityMainTargetGuid",
                "GetUnityFrameworkTargetGuid",
                "GetUnityAPITargetGuid"
            };
            foreach (var getterName in targetGetterNames)
            {
                var getter = pbxType.GetMethod(getterName, Type.EmptyTypes);
                var targetGuid = getter?.Invoke(pbx, null) as string;
                if (!string.IsNullOrWhiteSpace(targetGuid) && !result.Contains(targetGuid))
                    result.Add(targetGuid);
            }

            var targetGuidByName = pbxType.GetMethod("TargetGuidByName", new[] { typeof(string) });
            if (targetGuidByName == null)
                throw new BuildFailedException("[AppleWatch] PBXProject.TargetGuidByName is unavailable.");

            foreach (var targetName in new[] { WatchAppName, WatchExtensionName })
            {
                var targetGuid = targetGuidByName.Invoke(pbx, new object[] { targetName }) as string;
                if (!string.IsNullOrWhiteSpace(targetGuid) && !result.Contains(targetGuid))
                    result.Add(targetGuid);
            }

            if (result.Count == 0)
                throw new BuildFailedException("[AppleWatch] No supported Unity PBX targets could be resolved.");

            return result;
        }

        private static bool HasWatchTarget(
            object pbx,
            MethodInfo buildPropertyMethod,
            IEnumerable<string> targets)
        {
            if (targets == null)
                return false;

            foreach (var targetGuid in targets)
            {
                if (string.IsNullOrWhiteSpace(targetGuid))
                    continue;

                var targetName = GetBuildPropertyOrEmpty(pbx, buildPropertyMethod, targetGuid, "PRODUCT_NAME");
                var productType = GetBuildPropertyOrEmpty(pbx, buildPropertyMethod, targetGuid, "PRODUCT_TYPE");
                if (IsWatchTarget(productType, targetName))
                    return true;
            }

            return false;
        }

        private static string FindWatchExecutableTargetGuid(object pbx, Type pbxType)
        {
            var targetGuidByName = pbxType?.GetMethod("TargetGuidByName", new[] { typeof(string) });
            if (targetGuidByName == null)
                throw new BuildFailedException("[AppleWatch] PBXProject.TargetGuidByName is unavailable.");

            var legacyExtensionGuid = targetGuidByName.Invoke(pbx, new object[] { WatchExtensionName }) as string;
            if (!string.IsNullOrWhiteSpace(legacyExtensionGuid))
            {
                throw new BuildFailedException(
                    "[AppleWatch] Legacy WatchKit extension targets are not supported by this Unity 6000.7 Swift export path. " +
                    "A single executable watchOS application target is required; perform a clean Replace export.");
            }

            return targetGuidByName.Invoke(pbx, new object[] { WatchAppName }) as string;
        }

        private static bool IsWatchTarget(string productType, string targetName)
        {
            if (!string.IsNullOrWhiteSpace(productType))
            {
                var lower = productType.ToLowerInvariant();
                if (lower.Contains("watchapp") || lower.Contains("watchextension") || lower.Contains(".watchkit"))
                    return true;
            }

            return string.Equals(targetName, WatchAppName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(targetName, WatchExtensionName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveXcodeProjectPbxPath(string buildPath)
        {
            if (string.IsNullOrWhiteSpace(buildPath))
                throw new BuildFailedException("[AppleWatch] iOS build output path is empty.");

            var pbxType = ResolvePbxProjectType();
            if (pbxType == null)
                throw new BuildFailedException("[AppleWatch] PBXProject type not found in the installed iOS Xcode extension.");

            var getPbxProjectPath = pbxType.GetMethod(
                "GetPBXProjectPath",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (getPbxProjectPath == null)
                throw new BuildFailedException("[AppleWatch] PBXProject.GetPBXProjectPath is unavailable.");

            var resolvedPath = getPbxProjectPath.Invoke(null, new object[] { buildPath }) as string;
            if (string.IsNullOrWhiteSpace(resolvedPath))
                throw new BuildFailedException("[AppleWatch] PBXProject.GetPBXProjectPath returned an empty path.");

            if (!Path.IsPathRooted(resolvedPath))
                resolvedPath = Path.GetFullPath(Path.Combine(buildPath, resolvedPath));

            if (!File.Exists(resolvedPath))
                throw new BuildFailedException($"[AppleWatch] Xcode PBX project was not found: '{resolvedPath}'.");

            return resolvedPath;
        }

        private static string GetBuildPropertyOrEmpty(object pbx, MethodInfo buildPropertyMethod, string targetGuid, string propertyName)
        {
            try
            {
                if (buildPropertyMethod == null)
                    return string.Empty;

                var name = buildPropertyMethod.Invoke(pbx, new object[] { targetGuid, propertyName });
                return name?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Type ResolvePbxProjectType()
        {
            return typeof(XcodePbxProject);
        }
    }
}
