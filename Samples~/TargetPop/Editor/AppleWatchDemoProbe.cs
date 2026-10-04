using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AppleWatchController.Samples.TargetPop.Editor
{
    /// <summary>Exposes Editor CLI probes for the running Watch-controlled demo.</summary>
    /// <remarks>
    /// Call on Unity's main thread in Play Mode with the demo as the active scene.
    /// Mutating probes run one at a time. They inspect the project's expected demo hierarchy, not arbitrary scenes.
    /// Simulation exercises the Editor input path; it does not test WatchConnectivity or a physical Watch.
    /// </remarks>
    public static class AppleWatchDemoProbe
    {
        private static bool running;

        /// <summary>Reads the current game, input, orientation, audio, and HUD state without changing it.</summary>
        /// <returns>An anonymous CLI result with screen/panel sizes, game counters, input values, and receiver/audio diagnostics.</returns>
        /// <remarks>Requires the complete demo component hierarchy. Unavailable native-frame age is returned as null.</remarks>
        /// <exception cref="InvalidOperationException">Play Mode is stopped or the active scene has no demo root.</exception>

        public static object State()
        {
            return Snapshot(RequireDemo());
        }

        /// <summary>Changes the running receiver's explicit Editor attitude simulation.</summary>
        /// <param name="enabled">Whether the receiver should expose the simulated attitude as available.</param>
        /// <param name="x">Finite raw-attitude Euler X angle in degrees.</param>
        /// <param name="y">Finite raw-attitude Euler Y angle in degrees.</param>
        /// <param name="z">Finite raw-attitude Euler Z angle in degrees.</param>
        /// <returns>A task with the requested pose and before/after state snapshots.</returns>
        /// <remarks>
        /// Changes serialized fields on the live Play Mode receiver without Undo or scene saving.
        /// The pose remains active in that running instance until another change; this method does not restore it.
        /// The command does not author a persistent scene pose or supply data from a Watch sensor.
        /// </remarks>
        /// <exception cref="ArgumentException">Any angle is NaN or infinite.</exception>
        /// <exception cref="InvalidOperationException">The demo is not running, another probe is active, or pose fields are unavailable.</exception>
        /// <exception cref="OperationCanceledException">Play Mode stops while waiting for the result.</exception>

        public static async Task<object> PoseAsync(
            bool enabled = true,
            float x = 0f,
            float y = 0f,
            float z = 0f)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new ArgumentException("Pose angles must be finite.");
            var root = RequireDemo();
            if (running) throw new InvalidOperationException("A demo probe is already running.");
            var receiver = root.GetComponent<WatchInputReceiver>();
            if (receiver == null) throw new InvalidOperationException("The active demo has no Watch input receiver.");
            var before = Snapshot(root);
            running = true;
            try
            {
                using (var data = new SerializedObject(receiver))
                {
                    var enabledProperty = data.FindProperty("editorAttitudeEnabled");
                    var eulerProperty = data.FindProperty("editorAttitudeEuler");
                    if (enabledProperty == null || eulerProperty == null)
                        throw new InvalidOperationException("The receiver does not expose explicit Editor pose simulation.");
                    enabledProperty.boolValue = enabled;
                    eulerProperty.vector3Value = new Vector3(x, y, z);
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                await WaitForFrames(0.12f, null);
                return new { Enabled = enabled, Euler = new[] { x, y, z }, Before = before, After = Snapshot(root) };
            }
            finally { running = false; }
        }

        /// <summary>Holds synthetic keyboard keys through the demo's real Editor receiver and Input System path.</summary>
        /// <param name="keys">Comma-separated Input System Key names, case-insensitive, or blank for no held keys.</param>
        /// <param name="duration">Finite hold time from 0.05 through 2 seconds.</param>
        /// <param name="testSound">Whether to also play the demo's explicitly synthetic test tone.</param>
        /// <returns>Before, during, and after snapshots plus the largest sampled absolute audio output value.</returns>
        /// <remarks>
        /// Adds a temporary keyboard, releases its keys, then removes it in a finally block.
        /// Game and audio actions can change the running session and are not rolled back.
        /// AudioOutputPeak is a sampled Unity AudioSource output value, not proof that a speaker produced sound.
        /// </remarks>
        /// <exception cref="ArgumentException">A key is unknown or None, or the duration is outside the finite allowed range.</exception>
        /// <exception cref="InvalidOperationException">The demo is not running or another probe is active.</exception>
        /// <exception cref="OperationCanceledException">Play Mode stops during simulation.</exception>

        public static async Task<object> SimulateAsync(
            string keys = "Space",
            float duration = 0.25f,
            bool testSound = false)
        {
            var root = RequireDemo();
            if (running) throw new InvalidOperationException("A demo probe is already running.");
            if (!float.IsFinite(duration) || duration < 0.05f || duration > 2f)
                throw new ArgumentException("Duration must be between 0.05 and 2 seconds.");
            var parsedKeys = string.IsNullOrWhiteSpace(keys) ? Array.Empty<Key>() : keys.Split(',').Select(key =>
            {
                if (!Enum.TryParse<Key>(key.Trim(), true, out var result) || !Enum.IsDefined(typeof(Key), result) || result == Key.None)
                    throw new ArgumentException("Unknown keyboard key: " + key);
                return result;
            }).ToArray();
            var before = Snapshot(root);
            Keyboard keyboard = null;
            running = true;
            var output = new float[256];
            var peak = 0f;
            var sources = root.GetComponents<AudioSource>();
            try
            {
                keyboard = InputSystem.AddDevice<Keyboard>("Watch Demo Probe Keyboard");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(parsedKeys));
                if (testSound) root.GetComponent<AppleWatchDemoAudio>().PlaySimulationTone();
                await WaitForFrames(duration, () =>
                {
                    foreach (var source in sources)
                    {
                        source.GetOutputData(output, 0);
                        foreach (var sample in output) peak = Mathf.Max(peak, Mathf.Abs(sample));
                    }
                });
                var during = Snapshot(root);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await WaitForFrames(0.12f, null);
                return new { Before = before, During = during, After = Snapshot(root), AudioOutputPeak = peak };
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                running = false;
            }
        }

        private static GameObject RequireDemo()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Enter Play Mode in WatchControllerDemo first.");
            var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(item => item.GetComponent<AppleWatchDemoGame>() != null);
            if (root == null) throw new InvalidOperationException("The active scene is not a Watch-controlled demo.");
            return root;
        }

        /// <summary>Focuses a named HUD button and submits it through a UI Toolkit navigation event.</summary>
        /// <param name="element">The name of an enabled Button in the demo HUD, such as test-sound or reset.</param>
        /// <returns>The button name, before/after snapshots, and sampled absolute audio output peak.</returns>
        /// <remarks>May scroll the button into view. Button actions can change game or audio state and are not rolled back.</remarks>
        /// <exception cref="InvalidOperationException">The demo is not running, another probe is active, or the button is missing or disabled.</exception>
        /// <exception cref="OperationCanceledException">Play Mode stops before the result is ready.</exception>

        public static async Task<object> UiActionAsync(
            string element = "test-sound")
        {
            var demo = RequireDemo();
            if (running) throw new InvalidOperationException("A demo probe is already running.");
            var root = demo.GetComponentInChildren<UIDocument>().rootVisualElement;
            var button = root.Q<Button>(element);
            if (button == null || !button.enabledInHierarchy)
                throw new InvalidOperationException("The requested HUD button is missing or disabled.");
            running = true;
            var before = Snapshot(demo);
            try
            {
                button.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(button);
                button.Focus();
                await WaitForFrames(0.06f, null);
                using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
                var samples = new float[256];
                var peak = 0f;
                var sources = demo.GetComponents<AudioSource>();
                await WaitForFrames(0.15f, () =>
                {
                    foreach (var source in sources)
                    {
                        source.GetOutputData(samples, 0);
                        foreach (var sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                    }
                });
                return new { Element = element, Before = before, After = Snapshot(demo), AudioOutputPeak = peak };
            }
            finally { running = false; }
        }

        private static object Snapshot(GameObject root)
        {
            var controller = root.GetComponent<AppleWatchDemoController>();
            var game = root.GetComponent<AppleWatchDemoGame>();
            var audio = root.GetComponent<AppleWatchDemoAudio>();
            var receiver = root.GetComponent<WatchInputReceiver>();
            var hud = root.GetComponentInChildren<AppleWatchDemoHud>();
            var position = root.transform.Find("Player").position;
            var panelRoot = root.GetComponentInChildren<UIDocument>().rootVisualElement;
            return new
            {
                Screen = new[] { Screen.width, Screen.height },
                Panel = new[] { panelRoot.resolvedStyle.width, panelRoot.resolvedStyle.height },
                HudSections = panelRoot.Query<VisualElement>(className: "section").ToList()
                    .Select(element => new[] { element.worldBound.yMin, element.worldBound.yMax }).ToArray(),
                game.Score, game.Shots, game.Wave, game.RemainingTargets, game.LastResult, game.ResetCount,
                Position = new[] { position.x, position.y, position.z },
                Move = new[] { controller.MoveInput.x, controller.MoveInput.y },
                controller.CrownTotal, controller.EventCount, controller.LastEvent,
                Acceleration = new[] { controller.Acceleration.x, controller.Acceleration.y, controller.Acceleration.z },
                Gyro = new[] { controller.Gyro.x, controller.Gyro.y, controller.Gyro.z },
                Attitude = new[] { controller.Attitude.x, controller.Attitude.y, controller.Attitude.z, controller.Attitude.w },
                RelativeAttitude = new[] { controller.RelativeAttitude.x, controller.RelativeAttitude.y, controller.RelativeAttitude.z, controller.RelativeAttitude.w },
                controller.AttitudeTracked, controller.AttitudeReference, controller.OrientationStatus,
                MotionMode = controller.MotionMode.ToString(), hud.UpdateFramesPerSecond,
                receiver.NativeFramesPerSecond, receiver.NativeFramesReceived, receiver.LastNativeQueueAgeSeconds,
                SecondsSinceNativeFrame = double.IsFinite(receiver.SecondsSinceNativeFrame) ? (double?)receiver.SecondsSinceNativeFrame : null,
                receiver.MotionSampleInterval,
                controller.MicrophoneLevel, controller.MicrophonePeak, controller.Axis0,
                audio.RecordingStatus, audio.CanPlayRecording, audio.IsPlayingRecording, audio.IsLoadingRecording, audio.EffectsEnabled,
                EffectClip = root.GetComponents<AudioSource>().FirstOrDefault()?.clip?.name
            };
        }

        /// <summary>Captures the running game and HUD to a PNG at an explicit resolution.</summary>
        /// <param name="width">Capture width in pixels, from 320 through 2560.</param>
        /// <param name="height">Capture height in pixels, from 320 through 2560.</param>
        /// <returns>The absolute PNG path, dimensions, and a state snapshot taken at the capture resolution.</returns>
        /// <remarks>
        /// Writes Logs/watch-game-{width}x{height}.png under the project root and overwrites an existing capture at that size.
        /// Temporarily routes the camera and HUD panel into a render texture instead of resizing the Game View.
        /// A finally block restores render targets and releases temporary textures. The game continues to run during capture.
        /// </remarks>
        /// <exception cref="ArgumentException">Either dimension is outside the allowed range.</exception>
        /// <exception cref="InvalidOperationException">The demo is not running or another probe is active.</exception>
        /// <exception cref="OperationCanceledException">Play Mode stops before capture completes.</exception>
        /// <exception cref="System.IO.IOException">The capture directory or PNG cannot be written.</exception>

        public static async Task<object> CaptureAsync(
            int width = 1280,
            int height = 720)
        {
            var root = RequireDemo();
            if (running) throw new InvalidOperationException("A demo probe is already running.");
            if (width < 320 || width > 2560 || height < 320 || height > 2560)
                throw new ArgumentException("Capture dimensions must be between 320 and 2560.");
            running = true;
            var camera = root.GetComponentInChildren<Camera>();
            var panel = root.GetComponentInChildren<UIDocument>().panelSettings;
            var previousCamera = camera.targetTexture;
            var previousPanel = panel.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = null;
            try
            {
                texture.Create();
                camera.targetTexture = texture;
                panel.targetTexture = texture;
                await WaitForFrames(0.3f, null);
                RenderTexture.active = texture;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                var directory = System.IO.Path.Combine(Application.dataPath, "../Logs");
                System.IO.Directory.CreateDirectory(directory);
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, $"watch-game-{width}x{height}.png"));
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
                return new { Path = path, Width = width, Height = height, State = Snapshot(root) };
            }
            finally
            {
                camera.targetTexture = previousCamera;
                panel.targetTexture = previousPanel;
                RenderTexture.active = previousActive;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                running = false;
            }
        }

        private static Task WaitForFrames(float duration, Action onFrame)
        {
            var completion = new TaskCompletionSource<bool>();
            var until = EditorApplication.timeSinceStartup + duration;
            var frames = 0;
            EditorApplication.CallbackFunction update = null;
            update = () =>
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.update -= update;
                    completion.TrySetException(new OperationCanceledException("Play Mode stopped during the probe."));
                    return;
                }
                onFrame?.Invoke();
                if (++frames < 3 || EditorApplication.timeSinceStartup < until) return;
                EditorApplication.update -= update;
                completion.TrySetResult(true);
            };
            EditorApplication.update += update;
            return completion.Task;
        }
    }
}
