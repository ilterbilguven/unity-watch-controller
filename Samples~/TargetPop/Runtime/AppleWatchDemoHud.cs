using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Connects the authored UI Toolkit demo document to game, input, and audio state.</summary>
/// <remarks>
/// The document must contain the element names used by Bind. Values refresh at up to 10 Hz; events update on arrival.
/// Geometry changes select portrait or landscape layout and adjust the arena camera viewport.
/// The component removes its event and UI callbacks when disabled.
/// </remarks>
[RequireComponent(typeof(UIDocument))]
public sealed class AppleWatchDemoHud : MonoBehaviour
{
    [SerializeField] private AppleWatchDemoController controller;
    [SerializeField] private AppleWatchDemoGame game;
    [SerializeField] private AppleWatchDemoAudio audioFeedback;
    [SerializeField] private Camera arenaCamera;

    private VisualElement root;
    private VisualElement micFill;
    private Label score, wave, result, move, crown, acceleration, gyro, touch, axis, mic, events, recording;
    private Label orientationStatus, attitude, motionDescription, transportStats, motionSample, updateRate, resetCount;
    private Button reset, effects, testSound, playRecording, stopRecording;
    private Button orientationMode, angularVelocityMode, centerOrientation;
    private WatchInputReceiver receiver;
    private readonly Queue<string> history = new();
    private double nextRefresh, updateWindowStart = double.NaN;
    private int updatesInWindow;
    private bool bound;

    /// <summary>Gets this component's Update-call rate over the last completed window of at least one second.</summary>
    /// <remarks>This is neither the 10 Hz text-refresh rate nor the native Watch input rate. It resets to zero on enable.</remarks>
    public float UpdateFramesPerSecond { get; private set; }

    private void OnEnable()
    {
        updateWindowStart = double.NaN;
        updatesInWindow = 0;
        UpdateFramesPerSecond = 0f;
        nextRefresh = 0d;
        Bind();
    }

    private void Start() => Bind();

    private void Bind()
    {
        if (bound) return;
        root = GetComponent<UIDocument>().rootVisualElement;
        if (root?.Q("demo") == null) return;
        root.AddToClassList("demo-document");
        score = root.Q<Label>("score");
        wave = root.Q<Label>("wave");
        result = root.Q<Label>("game-result");
        move = root.Q<Label>("move-value");
        crown = root.Q<Label>("crown-value");
        acceleration = root.Q<Label>("acceleration-value");
        gyro = root.Q<Label>("gyro-value");
        orientationStatus = root.Q<Label>("orientation-status");
        attitude = root.Q<Label>("attitude-value");
        motionDescription = root.Q<Label>("motion-description");
        transportStats = root.Q<Label>("transport-stats");
        motionSample = root.Q<Label>("motion-sample");
        updateRate = root.Q<Label>("update-rate");
        resetCount = root.Q<Label>("reset-count");
        touch = root.Q<Label>("touch-value");
        axis = root.Q<Label>("axis-value");
        mic = root.Q<Label>("mic-value");
        events = root.Q<Label>("events");
        recording = root.Q<Label>("recording-status");
        micFill = root.Q("mic-fill");
        reset = root.Q<Button>("reset");
        effects = root.Q<Button>("effects");
        testSound = root.Q<Button>("test-sound");
        playRecording = root.Q<Button>("play-recording");
        stopRecording = root.Q<Button>("stop-recording");
        orientationMode = root.Q<Button>("orientation-mode");
        angularVelocityMode = root.Q<Button>("angular-velocity-mode");
        centerOrientation = root.Q<Button>("center-orientation");
        reset.clicked += Reset;
        effects.clicked += ToggleEffects;
        testSound.clicked += TestSound;
        playRecording.clicked += PlayRecording;
        stopRecording.clicked += StopRecording;
        orientationMode.clicked += ShowOrientation;
        angularVelocityMode.clicked += ShowAngularVelocity;
        centerOrientation.clicked += CenterOrientation;
        receiver = controller != null ? controller.GetComponent<WatchInputReceiver>() : GetComponentInParent<WatchInputReceiver>();
        if (controller != null) controller.DemoEvent += OnDemoEvent;
        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.Q<Label>("input-mode").text = Application.isEditor ? "EDITOR SIMULATION → INPUT SYSTEM" : "WATCH → IPHONE → INPUT SYSTEM";
        root.Q<Label>("keyboard-hints").style.display = Application.isEditor ? DisplayStyle.Flex : DisplayStyle.None;
        bound = true;
        Reflow();
        RefreshValues();
    }

    private void Update() => UpdateHud(Time.realtimeSinceStartupAsDouble);

    private void UpdateHud(double realtime)
    {
        // Count actual Update calls before either binding or the 10 Hz text-refresh gate.
        if (double.IsNaN(updateWindowStart)) updateWindowStart = realtime;
        else
        {
            updatesInWindow++;
            var elapsed = realtime - updateWindowStart;
            if (elapsed >= 1d)
            {
                UpdateFramesPerSecond = (float)(updatesInWindow / elapsed);
                updatesInWindow = 0;
                updateWindowStart = realtime;
            }
        }
        if (!bound) { Bind(); return; }
        if (realtime < nextRefresh) return;
        nextRefresh = realtime + 0.1d;
        RefreshValues();
    }

    private void RefreshValues()
    {
        if (game != null)
        {
            score.text = game.Score.ToString("D4");
            wave.text = $"WAVE {game.Wave:D2}  /  {game.RemainingTargets} LEFT";
            result.text = game.LastResult;
            resetCount.text = $"Resets: {game.ResetCount}";
        }
        if (controller != null)
        {
            move.text = Pair(controller.MoveInput);
            crown.text = controller.CrownTotal.ToString("F2");
            acceleration.text = Triple(controller.Acceleration);
            gyro.text = Triple(controller.Gyro);
            orientationStatus.text = controller.OrientationStatus;
            var pose = controller.Attitude;
            attitude.text = controller.AttitudeTracked ? $"Raw pose x/y/z/w: {pose.x:F2} / {pose.y:F2} / {pose.z:F2} / {pose.w:F2}" : "No tracked pose";
            var orientation = controller.MotionMode == MotionDisplayMode.Orientation;
            orientationMode.EnableInClassList("is-selected", orientation);
            angularVelocityMode.EnableInClassList("is-selected", !orientation);
            centerOrientation.SetEnabled(controller.CanCenterOrientation);
            motionDescription.text = orientation ? "Orientation holds its angle when still." : "Angular velocity returns to center when still.";
            touch.text = Pair(controller.TouchPosition);
            axis.text = controller.Axis0.ToString("F2");
            mic.text = $"LEVEL {controller.MicrophoneLevel:P0} / PEAK {controller.MicrophonePeak:P0}";
            micFill.style.width = Length.Percent(controller.MicrophoneLevel * 100f);
        }
        updateRate.text = UpdateFramesPerSecond > 0f ? $"Game Update: {UpdateFramesPerSecond:F1} Hz" : "Game Update: measuring…";
        if (Application.isEditor) transportStats.text = "Editor simulation • no native cadence sample";
        else if (receiver == null || receiver.NativeFramesReceived == 0) transportStats.text = "Native input: no fresh frames yet";
        else transportStats.text = $"Native accepted: {receiver.NativeFramesPerSecond:F1} frames/s\nQueue age: {receiver.LastNativeQueueAgeSeconds * 1000f:F0} ms • Last frame: {receiver.SecondsSinceNativeFrame:F2} s ago";
        motionSample.text = receiver != null && receiver.MotionSampleInterval > 0f
            ? $"Motion sample step: {receiver.MotionSampleInterval * 1000f:F0} ms"
            : "Motion sample step: unavailable";
        if (audioFeedback != null)
        {
            recording.text = audioFeedback.RecordingStatus;
            playRecording.SetEnabled(audioFeedback.CanPlayRecording);
            stopRecording.SetEnabled(audioFeedback.IsPlayingRecording || audioFeedback.IsLoadingRecording);
            effects.text = audioFeedback.EffectsEnabled ? "Effects: on" : "Effects: off";
        }
    }

    private void OnDemoEvent(string action)
    {
        history.Enqueue(action);
        while (history.Count > 4) history.Dequeue();
        if (events != null) events.text = string.Join("\n", history);
    }

    private void Reset() { if (controller != null) controller.ResetDemo(); else game?.ResetGame(); }
    private void ToggleEffects() { if (audioFeedback != null) audioFeedback.SetEffectsEnabled(!audioFeedback.EffectsEnabled); }
    private void TestSound() => audioFeedback?.PlaySimulationTone();
    private void PlayRecording() => audioFeedback?.PlayLatestRecording();
    private void StopRecording() => audioFeedback?.StopRecording();
    private void ShowOrientation() { if (controller != null) controller.MotionMode = MotionDisplayMode.Orientation; RefreshValues(); }
    private void ShowAngularVelocity() { if (controller != null) controller.MotionMode = MotionDisplayMode.AngularVelocity; RefreshValues(); }
    private void CenterOrientation() { controller?.CenterOrientation(); RefreshValues(); }
    private void OnGeometryChanged(GeometryChangedEvent _) => Reflow();

    private void Reflow()
    {
        if (root == null || root.resolvedStyle.width <= 0f) return;
        var portrait = root.resolvedStyle.width < root.resolvedStyle.height;
        root.Q("demo").EnableInClassList("portrait", portrait);
        if (arenaCamera != null)
            arenaCamera.rect = portrait ? new Rect(0, 0.40f, 1, 0.49f) : new Rect(0, 0, 0.71f, 1);
    }

    private void OnDisable()
    {
        if (!bound) return;
        if (controller != null) controller.DemoEvent -= OnDemoEvent;
        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        reset.clicked -= Reset;
        effects.clicked -= ToggleEffects;
        testSound.clicked -= TestSound;
        playRecording.clicked -= PlayRecording;
        stopRecording.clicked -= StopRecording;
        orientationMode.clicked -= ShowOrientation;
        angularVelocityMode.clicked -= ShowAngularVelocity;
        centerOrientation.clicked -= CenterOrientation;
        history.Clear();
        bound = false;
    }

    private static string Pair(Vector2 value) => $"{value.x:F2} / {value.y:F2}";
    private static string Triple(Vector3 value) => $"{value.x:F2} / {value.y:F2} / {value.z:F2}";
}
