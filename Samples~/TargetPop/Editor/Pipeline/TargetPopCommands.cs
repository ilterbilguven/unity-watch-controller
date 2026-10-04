using System.Threading.Tasks;
using AppleWatchController.Samples.TargetPop.Editor;
using Unity.Pipeline.Commands;

namespace AppleWatchController.Samples.TargetPop.Pipeline.Editor
{
    public static class TargetPopCommands
    {
        [CliCommand("watch_demo_create", "Create or open Target Pop from an explicitly imported sample root.", MainThreadRequired = true)]
        public static object Create(
            [CliArg("sample_root", "Imported Target Pop folder under Assets.", Required = true)] string sampleRoot,
            [CliArg("scene_path", "New scene under Assets. Existing scenes are opened, never overwritten.")] string scenePath = TargetPopSampleSetup.ScenePath,
            [CliArg("make_startup_scene", "Place this scene first in build settings.")] bool makeStartupScene = false,
            [CliArg("watch_ui_path", "Explicit authored Assets .watch.uxml source; omitted selects imported Watch Lab by its GUID.")] string watchUiPath = null)
            => string.IsNullOrWhiteSpace(watchUiPath) ? TargetPopSampleSetup.Create(sampleRoot, scenePath, makeStartupScene)
                : TargetPopSampleSetup.CreateWithWatchSource(sampleRoot, scenePath, watchUiPath, makeStartupScene);

        [CliCommand("watch_demo_state", "Read the active game's score, input values and sound state.", MainThreadRequired = true)]
        public static object State() => AppleWatchDemoProbe.State();

        [CliCommand("watch_demo_pose", "Set transient Editor attitude simulation.", MainThreadRequired = true)]
        public static Task<object> PoseAsync(
            [CliArg("enabled", "Enable explicit attitude simulation.")] bool enabled = true,
            [CliArg("x", "Finite Euler X angle.")] float x = 0,
            [CliArg("y", "Finite Euler Y angle.")] float y = 0,
            [CliArg("z", "Finite Euler Z angle.")] float z = 0) => AppleWatchDemoProbe.PoseAsync(enabled, x, y, z);

        [CliCommand("watch_demo_simulate", "Exercise the Editor receiver and Input System path.", MainThreadRequired = true)]
        public static Task<object> SimulateAsync(
            [CliArg("keys", "Comma-separated Input System key names.")] string keys = "Space",
            [CliArg("duration", "Hold time from 0.05 to 2 seconds.")] float duration = 0.25f,
            [CliArg("test_sound", "Play the labeled synthetic test tone.")] bool testSound = false) => AppleWatchDemoProbe.SimulateAsync(keys, duration, testSound);

        [CliCommand("watch_demo_ui", "Submit a focused HUD button.", MainThreadRequired = true)]
        public static Task<object> UiActionAsync(
            [CliArg("element", "HUD button name.")] string element = "test-sound") => AppleWatchDemoProbe.UiActionAsync(element);

        [CliCommand("watch_demo_capture", "Render the game and HUD at an explicit resolution.", MainThreadRequired = true)]
        public static Task<object> CaptureAsync(
            [CliArg("width", "Render width from 320 to 2560.")] int width = 1280,
            [CliArg("height", "Render height from 320 to 2560.")] int height = 720) => AppleWatchDemoProbe.CaptureAsync(width, height);
    }
}
