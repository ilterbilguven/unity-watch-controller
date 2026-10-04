using Unity.Pipeline.Commands;

namespace AppleWatchController.Editor
{
    /// <summary>Exposes the shared iOS export and SDK services through optional Pipeline commands.</summary>
    /// <remarks>Requires a compatible Pipeline package and the iOS build target. Runs on Unity's main thread.</remarks>
    public static class AppleWatchBuildCommand
    {
        /// <summary>Reads live and saved iOS SDK settings without changing them.</summary>
        /// <returns>The shared service's diagnostic snapshot.</returns>
        [CliCommand("apple_watch_ios_sdk_state", "Read live and saved iOS SDK settings without modifying them.", MainThreadRequired = true)]
        public static AppleWatchIosSdkState GetIosSdkState() => AppleWatchBuildService.GetIosSdkState();

        /// <summary>Exports enabled consumer scenes through the shared iOS export service.</summary>
        /// <param name="outputPath">A new or empty absolute or consumer-project-relative directory.</param>
        /// <param name="simulator">Temporarily selects the ARM64 Simulator SDK and restores saved settings afterward.</param>
        /// <returns>The exported Xcode project path, Unity-reported size, and successful result.</returns>
        /// <remarks>
        /// Requires iOS Build Support, enabled scenes, and a fresh Assets-owned Watch UI export.
        /// False preserves the current SDK. Service errors propagate; partial output can remain on failure.
        /// Success does not prove Xcode compilation, signing, installation, or physical device behavior.
        /// </remarks>
        [CliCommand("apple_watch_export_ios", "Create a clean iOS Xcode export and run the Apple Watch postprocessor.", MainThreadRequired = true)]
        public static AppleWatchBuildCommandResult ExportIos(
            [CliArg("output_path", "Empty output directory for the generated Xcode project.", Required = true)] string outputPath,
            [CliArg("simulator", "Export for the ARM64 iPhone Simulator without changing the saved SDK settings.")] bool simulator = false)
        {
            return AppleWatchBuildService.ExportIos(outputPath, simulator);
        }
    }
}
