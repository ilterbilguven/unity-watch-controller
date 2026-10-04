using System;

namespace AppleWatchController.Editor
{
    /// <summary>Read-only diagnostic values returned by the iOS SDK service.</summary>
    /// <remarks>Changing these result properties does not change project settings.</remarks>
    [Serializable]
    public sealed class AppleWatchIosSdkState
    {
        /// <summary>The name of the live iOS SDK enum value.</summary>
        public string Sdk { get; set; }
        /// <summary>The numeric live iOS SDK enum value.</summary>
        public int SdkValue { get; set; }
        /// <summary>The live simulator architecture name.</summary>
        public string SimulatorArchitecture { get; set; }
        /// <summary>The numeric live simulator architecture value.</summary>
        public int SimulatorArchitectureValue { get; set; }
        /// <summary>Saved SDK and simulator architecture lines.</summary>
        public string[] SavedSettings { get; set; }
        /// <summary>Loaded settings asset type names and unsaved-change flags.</summary>
        public string[] LoadedSettings { get; set; }
    }

    /// <summary>A successful Unity iOS export, not an Xcode compile or app installation.</summary>
    [Serializable]
    public sealed class AppleWatchBuildCommandResult
    {
        /// <summary>The absolute exported Xcode project directory.</summary>
        public string OutputPath { get; set; }
        /// <summary>The size in bytes reported by Unity.</summary>
        public ulong TotalSize { get; set; }
        /// <summary>Unity's build result; the service returns only on Succeeded.</summary>
        public string Result { get; set; }
    }

    /// <summary>Contains paths from a completed Watch UI export.</summary>
    /// <remarks>Native compiler validation can be skipped when xcrun is unavailable.</remarks>
    public sealed class WatchUiExportResult
    {
        /// <summary>Gets or sets the export status; completed exports return success.</summary>
        public string Status { get; set; }
        /// <summary>Gets or sets the input path exactly as the caller supplied it.</summary>
        public string InputPath { get; set; }
        /// <summary>Gets or sets the absolute generated Swift path beside the Assets source.</summary>
        public string OutputPath { get; set; }
    }

    /// <summary>Contains source and window details for a resolved Editor preview.</summary>
    public sealed class WatchUiPreviewResult
    {
        /// <summary>Gets or sets the preview status.</summary>
        public string Status { get; set; }
        /// <summary>Gets or sets the source path exactly as supplied.</summary>
        public string InputPath { get; set; }
        /// <summary>Gets or sets the fully qualified preview window type name.</summary>
        public string WindowType { get; set; }
        /// <summary>Gets or sets the Watch viewport selector, #watch-screen.</summary>
        public string Selector { get; set; }
        /// <summary>Gets or sets the requested route, or an empty string.</summary>
        public string Route { get; set; }
    }
}
