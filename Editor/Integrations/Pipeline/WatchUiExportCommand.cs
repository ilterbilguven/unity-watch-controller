using System.Threading.Tasks;
using Unity.Pipeline.Commands;

namespace AppleWatchController.Editor
{
    /// <summary>Exposes the shared Watch UI services to a compatible Unity Pipeline Editor.</summary>
    /// <remarks>These optional commands run on the connected consumer Editor's main thread.</remarks>
    public static class WatchUiExportCommand
    {
        /// <summary>Exports an Assets-owned Watch UI source through the shared export service.</summary>
        /// <param name="watchUiPath">An absolute or project-relative .watch.uxml path inside Assets.</param>
        /// <returns>The service's validated generated output and original source path.</returns>
        /// <remarks>Service exceptions propagate to the CLI; success does not establish device acceptance.</remarks>
        [CliCommand("watch_ui_export", "Export a .watch.uxml asset to SwiftUI.", MainThreadRequired = true)]
        public static Task<WatchUiExportResult> WatchUiExport(
            [CliArg("watch_ui_path", "Path to the .watch.uxml source asset inside the consumer project's Assets folder.", Required = true)]
            string watchUiPath)
        {
            return WatchUiExportMenu.ExportWatchUiWithExplicitPathAsync(watchUiPath);
        }

        /// <summary>Opens the shared style-resolved, Watch-sized Editor preview.</summary>
        /// <param name="watchUiPath">An absolute or project-relative .watch.uxml path inside Assets.</param>
        /// <param name="route">An authored navigation route, or null for the initial page.</param>
        /// <returns>Source, route, window type, and viewport selector after layout resolves.</returns>
        /// <remarks>The 211 by 257 point Editor preview does not execute SwiftUI or simulate a physical Watch.</remarks>
        [CliCommand("watch_ui_preview", "Open source-authored Watch UI in a 211 by 257 point UI Toolkit viewport.", MainThreadRequired = true)]
        public static async Task<WatchUiPreviewResult> WatchUiPreview(
            [CliArg("watch_ui_path", "Path to the .watch.uxml source asset.", Required = true)] string watchUiPath,
            [CliArg("route", "Optional authored navigation route to display.")] string route = null)
        {
            await WatchUiPreviewWindow.OpenAsync(watchUiPath, route);
            return new WatchUiPreviewResult
            {
                Status = "success", InputPath = watchUiPath,
                WindowType = typeof(WatchUiPreviewWindow).FullName,
                Selector = "#watch-screen", Route = route ?? string.Empty
            };
        }
    }
}
