using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads the native Watch bridge each Unity update and submits fresh input to the custom device.</summary>
/// <remarks>
/// On iOS, the receiver drops expired frames and releases native pulses before the next sample.
/// Continuous input survives short gaps, then resets after the stale timeout (0.25 seconds by default).
/// Attitude has its own sample age, so unrelated messages cannot keep an old pose tracked.
/// Editor simulation uses held keyboard states and preserves finite raw Crown deltas.
/// Disabling this component queues neutral device state and clears diagnostics. Recording paths use a separate queue.
/// </remarks>
public class WatchInputReceiver : MonoBehaviour
{
    private const float DefaultMovementScale = 1f;
    private const float DefaultCrownScale = 1f;
    private const float DefaultAxisScale = 1f;
    private const float PressThreshold = 0.5f;

    [SerializeField] private float staleTimeoutSeconds = 0.25f;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void WatchBridge_Start();

    [DllImport("__Internal")] private static extern void WatchBridge_SetStaleTimeout(float seconds);

    [DllImport("__Internal")]
    private static extern int WatchBridge_PopFrameBytes([In, Out] byte[] frameBuffer, int frameBufferLength);

    [DllImport("__Internal")]
    private static extern int WatchBridge_PopRecordingPath([In, Out] byte[] pathBuffer, int pathBufferLength);
#else
    private static void WatchBridge_Start()
    {
    }

    private static void WatchBridge_SetStaleTimeout(float seconds)
    {
        _ = seconds;
    }

    private static int WatchBridge_PopFrameBytes([In, Out] byte[] frameBuffer, int frameBufferLength)
    {
        _ = frameBuffer;
        _ = frameBufferLength;
        return 0;
    }

    private static int WatchBridge_PopRecordingPath([In, Out] byte[] pathBuffer, int pathBufferLength)
    {
        _ = pathBuffer;
        _ = pathBufferLength;
        return 0;
    }
#endif

#if UNITY_EDITOR
    [SerializeField] private bool enableEditorSimulation = true;
    [SerializeField] private float editorMoveScale = DefaultMovementScale;
    [SerializeField] private float editorCrownScale = DefaultCrownScale;
    [SerializeField] private float editorAxisScale = DefaultAxisScale;
    [SerializeField] private bool editorAttitudeEnabled;
    [Tooltip("Explicit raw-attitude simulation in degrees; gyro keys do not synthesize orientation.")]
    [SerializeField] private Vector3 editorAttitudeEuler;
    [SerializeField] private Key editorPressKey = Key.Space;
    [SerializeField] private Key editorTouchPressKey = Key.LeftShift;
    [SerializeField] private Key editorTapKey = Key.T;
    [SerializeField] private Key editorDoubleTapKey = Key.Y;
    [SerializeField] private Key editorDragKey = Key.X;
    [SerializeField] private Key editorPrimaryActionKey = Key.P;
    [SerializeField] private Key editorLongPressKey = Key.L;
    [SerializeField] private Key editorSwipeUpKey = Key.UpArrow;
    [SerializeField] private Key editorSwipeDownKey = Key.DownArrow;
    [SerializeField] private Key editorSwipeLeftKey = Key.LeftArrow;
    [SerializeField] private Key editorSwipeRightKey = Key.RightArrow;
    [SerializeField] private Key editorCrownNegativeKey = Key.Q;
    [SerializeField] private Key editorCrownPositiveKey = Key.E;
    [SerializeField] private Key editorAccelUpKey = Key.I;
    [SerializeField] private Key editorAccelDownKey = Key.K;
    [SerializeField] private Key editorAccelLeftKey = Key.J;
    [SerializeField] private Key editorAccelRightKey = Key.H;
    [SerializeField] private Key editorAccelForwardKey = Key.U;
    [SerializeField] private Key editorAccelBackwardKey = Key.O;
    [SerializeField] private Key editorGyroUpKey = Key.W;
    [SerializeField] private Key editorGyroDownKey = Key.S;
    [SerializeField] private Key editorGyroLeftKey = Key.A;
    [SerializeField] private Key editorGyroRightKey = Key.D;
    [SerializeField] private Key editorGyroForwardKey = Key.B;
    [SerializeField] private Key editorGyroBackwardKey = Key.N;
    [SerializeField] private Key editorMicLevelKey = Key.M;
    [SerializeField] private Key editorMicPeakKey = Key.Comma;

    private InputAction editorMove;
    private InputAction editorPress;
    private InputAction editorTouchPress;
    private InputAction editorTap;
    private InputAction editorDoubleTap;
    private InputAction editorDrag;
    private InputAction editorPrimaryAction;
    private InputAction editorLongPress;
    private InputAction editorSwipeUp;
    private InputAction editorSwipeDown;
    private InputAction editorSwipeLeft;
    private InputAction editorSwipeRight;
    private InputAction editorCrown;
    private InputAction editorAccel;
    private InputAction editorGyro;
    private InputAction editorTouchPosition;
    private InputAction editorAxis0;
    private InputAction editorMicrophoneLevel;
    private InputAction editorMicrophonePeak;
#endif

    private readonly byte[] _frameBuffer = new byte[AppleWatchInputFrame.ByteLength];
    private byte[] recordingPathBuffer = new byte[4096];
    private AppleWatchInputFrame lastContinuousFrame = AppleWatchInputFrame.Neutral;
    private double lastInputFrameRealtime;
    private double lastAttitudeSampleRealtime;
    private bool hasInputFrame;
    private double lastNativeFrameRealtime;
    private bool hasNativeFrame;
    private double nativeRateWindowStart;
    private bool hasNativeRateWindow;
    private int nativeFramesInWindow;

    // Consumer cadence of accepted native dequeues, not Watch sender rate or network latency.
    /// <summary>Gets accepted native frames per second over the last completed window of at least one second.</summary>
    /// <remarks>This measures Unity's accepted dequeue cadence, not the Watch sender rate or network latency.</remarks>
    public float NativeFramesPerSecond { get; private set; }
    /// <summary>Gets the number of accepted native frames since the most recent enable; Editor frames do not count.</summary>
    public long NativeFramesReceived { get; private set; }
    /// <summary>Gets queue age in seconds for the last accepted native frame; zero until one arrives.</summary>
    public float LastNativeQueueAgeSeconds { get; private set; }
    /// <summary>Gets real-time seconds since Unity last accepted a native frame.</summary>
    /// <value>Nonnegative elapsed time, or positive infinity when no frame has arrived since diagnostics reset.</value>
    public double SecondsSinceNativeFrame => hasNativeFrame
        ? Math.Max(0d, Time.realtimeSinceStartupAsDouble - lastNativeFrameRealtime)
        : double.PositiveInfinity;
    /// <summary>Gets the current motion sample interval in seconds, or zero when unavailable.</summary>
    /// <remarks>This comes from the motion sample, not Unity frame timing or the transport rate.</remarks>
    public float MotionSampleInterval { get; private set; }

    /// <summary>Raised on Unity's main thread when the native bridge supplies a received recording's local file path.</summary>
    /// <remarks>
    /// The argument is a nonblank UTF-8-decoded path. This event does not decode or play audio and does not verify file existence.
    /// Recording transfers are separate from live input and microphone meters. Unsubscribe when a consumer is disabled.
    /// </remarks>
    public event Action<string> RecordingReceived;

    private readonly struct EditorSimulationState
    {
        public readonly Vector2 Simulation;
        public readonly float Press;
        public readonly float TouchPress;
        public readonly float Tap;
        public readonly float DoubleTap;
        public readonly float Drag;
        public readonly float PrimaryAction;
        public readonly float LongPress;
        public readonly float SwipeUp;
        public readonly float SwipeDown;
        public readonly float SwipeLeft;
        public readonly float SwipeRight;
        public readonly float Crown;
        public readonly Vector3 Acceleration;
        public readonly Vector3 Gyro;
        public readonly Vector2 TouchPosition;
        public readonly float Axis0;
        public readonly float MicLevel;
        public readonly float MicPeak;

        public EditorSimulationState(
            Vector2 simulation,
            float press,
            float touchPress,
            float tap,
            float doubleTap,
            float drag,
            float primaryAction,
            float longPress,
            float swipeUp,
            float swipeDown,
            float swipeLeft,
            float swipeRight,
            float crown,
            Vector3 acceleration,
            Vector3 gyro,
            Vector2 touchPosition,
            float axis0,
            float micLevel,
            float micPeak)
        {
            Simulation = simulation;
            Press = press;
            TouchPress = touchPress;
            Tap = tap;
            DoubleTap = doubleTap;
            Drag = drag;
            PrimaryAction = primaryAction;
            LongPress = longPress;
            SwipeUp = swipeUp;
            SwipeDown = swipeDown;
            SwipeLeft = swipeLeft;
            SwipeRight = swipeRight;
            Crown = crown;
            Acceleration = acceleration;
            Gyro = gyro;
            TouchPosition = touchPosition;
            Axis0 = axis0;
            MicLevel = micLevel;
            MicPeak = micPeak;
        }
    }

    private void Start()
    {
        WatchBridge_Start();

#if UNITY_EDITOR
        if (!enableEditorSimulation)
            return;
        ConfigureEditorActions();
#endif
    }

    private void OnEnable()
    {
        ResetNativeDiagnostics();
        WatchBridge_SetStaleTimeout(Mathf.Max(0.001f, staleTimeoutSeconds));

#if UNITY_EDITOR
        if (!enableEditorSimulation)
            return;

        editorMove?.Enable();
        editorPress?.Enable();
        editorTouchPress?.Enable();
        editorTap?.Enable();
        editorDoubleTap?.Enable();
        editorDrag?.Enable();
        editorPrimaryAction?.Enable();
        editorLongPress?.Enable();
        editorSwipeUp?.Enable();
        editorSwipeDown?.Enable();
        editorSwipeLeft?.Enable();
        editorSwipeRight?.Enable();
        editorCrown?.Enable();
        editorAccel?.Enable();
        editorGyro?.Enable();
        editorTouchPosition?.Enable();
        editorAxis0?.Enable();
        editorMicrophoneLevel?.Enable();
        editorMicrophonePeak?.Enable();
#endif
    }

    private bool TryReadFrame(out AppleWatchInputFrame frame)
    {
        frame = AppleWatchInputFrame.Neutral;
        // Drain expired backlog without replaying it. The native queue holds at most
        // 64 samples plus a deferred snapshot and an overflow-neutral sentinel.
        const int maxFramesPerUpdate = 66;
        for (var index = 0; index < maxFramesPerUpdate; index++)
        {
            var byteCount = WatchBridge_PopFrameBytes(_frameBuffer, _frameBuffer.Length);
            if (byteCount <= 0)
                return false;

            if (AppleWatchInputFrame.TryRead(_frameBuffer, byteCount, out frame)
                && frame.NativeQueueAgeSeconds < Mathf.Max(0.001f, staleTimeoutSeconds))
                return true;
        }

        frame = AppleWatchInputFrame.Neutral;
        return false;
    }

#if UNITY_EDITOR
    private static string KeyBinding(Key key)
    {
        var controlName = key.ToString();
        if (controlName.StartsWith("Digit", StringComparison.Ordinal))
            controlName = controlName.Substring(5);
        return $"<Keyboard>/{controlName.ToLowerInvariant()}";
    }

    private static bool IsPressed(float value)
    {
        return value > PressThreshold;
    }

    private void ConfigureEditorActions()
    {
        editorMove = new InputAction("watch_simulator_move");
        var moveComposite = editorMove.AddCompositeBinding("2DVector");
        moveComposite.With("Up", "<Keyboard>/w");
        moveComposite.With("Down", "<Keyboard>/s");
        moveComposite.With("Left", "<Keyboard>/a");
        moveComposite.With("Right", "<Keyboard>/d");

        editorPress = new InputAction("watch_simulator_button");
        editorPress.AddBinding(KeyBinding(editorPressKey));

        editorTouchPress = new InputAction("watch_simulator_touch_press");
        editorTouchPress.AddBinding(KeyBinding(editorTouchPressKey));

        editorTap = new InputAction("watch_simulator_tap");
        editorTap.AddBinding(KeyBinding(editorTapKey));

        editorDoubleTap = new InputAction("watch_simulator_double_tap");
        editorDoubleTap.AddBinding(KeyBinding(editorDoubleTapKey));

        editorDrag = new InputAction("watch_simulator_drag");
        editorDrag.AddBinding(KeyBinding(editorDragKey));

        editorPrimaryAction = new InputAction("watch_simulator_primary_action");
        editorPrimaryAction.AddBinding(KeyBinding(editorPrimaryActionKey));

        editorLongPress = new InputAction("watch_simulator_long_press");
        editorLongPress.AddBinding(KeyBinding(editorLongPressKey));

        editorSwipeUp = new InputAction("watch_simulator_swipe_up");
        editorSwipeUp.AddBinding(KeyBinding(editorSwipeUpKey));

        editorSwipeDown = new InputAction("watch_simulator_swipe_down");
        editorSwipeDown.AddBinding(KeyBinding(editorSwipeDownKey));

        editorSwipeLeft = new InputAction("watch_simulator_swipe_left");
        editorSwipeLeft.AddBinding(KeyBinding(editorSwipeLeftKey));

        editorSwipeRight = new InputAction("watch_simulator_swipe_right");
        editorSwipeRight.AddBinding(KeyBinding(editorSwipeRightKey));

        editorCrown = new InputAction("watch_simulator_crown");
        var crownComposite = editorCrown.AddCompositeBinding("1DAxis");
        crownComposite.With("Negative", KeyBinding(editorCrownNegativeKey));
        crownComposite.With("Positive", KeyBinding(editorCrownPositiveKey));

        editorAccel = new InputAction("watch_simulator_acceleration");
        var accelComposite = editorAccel.AddCompositeBinding("3DVector");
        accelComposite.With("Up", KeyBinding(editorAccelUpKey));
        accelComposite.With("Down", KeyBinding(editorAccelDownKey));
        accelComposite.With("Left", KeyBinding(editorAccelLeftKey));
        accelComposite.With("Right", KeyBinding(editorAccelRightKey));
        accelComposite.With("Forward", KeyBinding(editorAccelForwardKey));
        accelComposite.With("Backward", KeyBinding(editorAccelBackwardKey));

        editorGyro = new InputAction("watch_simulator_gyro");
        var gyroComposite = editorGyro.AddCompositeBinding("3DVector");
        gyroComposite.With("Up", KeyBinding(editorGyroUpKey));
        gyroComposite.With("Down", KeyBinding(editorGyroDownKey));
        gyroComposite.With("Left", KeyBinding(editorGyroLeftKey));
        gyroComposite.With("Right", KeyBinding(editorGyroRightKey));
        gyroComposite.With("Forward", KeyBinding(editorGyroForwardKey));
        gyroComposite.With("Backward", KeyBinding(editorGyroBackwardKey));

        editorTouchPosition = new InputAction("watch_simulator_touch_position");
        var touchPosComposite = editorTouchPosition.AddCompositeBinding("2DVector");
        touchPosComposite.With("Up", "<Keyboard>/r");
        touchPosComposite.With("Down", "<Keyboard>/f");
        touchPosComposite.With("Left", "<Keyboard>/v");
        touchPosComposite.With("Right", "<Keyboard>/n");

        editorAxis0 = new InputAction("watch_simulator_axis0");
        editorAxis0.AddBinding("<Keyboard>/x");

        editorMicrophoneLevel = new InputAction("watch_simulator_microphone_level");
        editorMicrophoneLevel.AddBinding(KeyBinding(editorMicLevelKey));

        editorMicrophonePeak = new InputAction("watch_simulator_microphone_peak");
        editorMicrophonePeak.AddBinding(KeyBinding(editorMicPeakKey));

        editorMove.Enable();
        editorPress.Enable();
        editorTouchPress.Enable();
        editorTap.Enable();
        editorDoubleTap.Enable();
        editorDrag.Enable();
        editorPrimaryAction.Enable();
        editorLongPress.Enable();
        editorSwipeUp.Enable();
        editorSwipeDown.Enable();
        editorSwipeLeft.Enable();
        editorSwipeRight.Enable();
        editorCrown.Enable();
        editorAccel.Enable();
        editorGyro.Enable();
        editorTouchPosition.Enable();
        editorAxis0.Enable();
        editorMicrophoneLevel.Enable();
        editorMicrophonePeak.Enable();
    }

    private bool TryReadEditorFrame(out AppleWatchInputFrame frame)
    {
        if (!enableEditorSimulation)
        {
            frame = AppleWatchInputFrame.Neutral;
            return false;
        }

        var state = new EditorSimulationState(
            editorMove?.ReadValue<Vector2>() ?? Vector2.zero,
            editorPress?.ReadValue<float>() ?? 0f,
            editorTouchPress?.ReadValue<float>() ?? 0f,
            editorTap?.ReadValue<float>() ?? 0f,
            editorDoubleTap?.ReadValue<float>() ?? 0f,
            editorDrag?.ReadValue<float>() ?? 0f,
            editorPrimaryAction?.ReadValue<float>() ?? 0f,
            editorLongPress?.ReadValue<float>() ?? 0f,
            editorSwipeUp?.ReadValue<float>() ?? 0f,
            editorSwipeDown?.ReadValue<float>() ?? 0f,
            editorSwipeLeft?.ReadValue<float>() ?? 0f,
            editorSwipeRight?.ReadValue<float>() ?? 0f,
            editorCrown?.ReadValue<float>() ?? 0f,
            editorAccel?.ReadValue<Vector3>() ?? Vector3.zero,
            editorGyro?.ReadValue<Vector3>() ?? Vector3.zero,
            editorTouchPosition?.ReadValue<Vector2>() ?? Vector2.zero,
            editorAxis0?.ReadValue<float>() ?? 0f,
            editorMicrophoneLevel?.ReadValue<float>() ?? 0f,
            editorMicrophonePeak?.ReadValue<float>() ?? 0f);

        var crownDelta = state.Crown * editorCrownScale;
        if (!float.IsFinite(crownDelta)) crownDelta = 0f;

        frame = new AppleWatchInputFrame(
            Mathf.Clamp(state.Simulation.x * editorMoveScale, -1f, 1f),
            Mathf.Clamp(state.Simulation.y * editorMoveScale, -1f, 1f),
            IsPressed(state.Press),
            IsPressed(state.TouchPress),
            IsPressed(state.Tap),
            IsPressed(state.DoubleTap),
            IsPressed(state.PrimaryAction),
            IsPressed(state.LongPress),
            IsPressed(state.Drag),
            IsPressed(state.SwipeUp),
            IsPressed(state.SwipeDown),
            IsPressed(state.SwipeLeft),
            IsPressed(state.SwipeRight),
            crownDelta,
            new Vector3(
                Mathf.Clamp(state.Acceleration.x * editorAxisScale, -1f, 1f),
                Mathf.Clamp(state.Acceleration.y * editorAxisScale, -1f, 1f),
                Mathf.Clamp(state.Acceleration.z * editorAxisScale, -1f, 1f)),
            new Vector3(
                Mathf.Clamp(state.Gyro.x * editorAxisScale, -1f, 1f),
                Mathf.Clamp(state.Gyro.y * editorAxisScale, -1f, 1f),
                Mathf.Clamp(state.Gyro.z * editorAxisScale, -1f, 1f)),
            Mathf.Clamp01(state.MicLevel),
            Mathf.Clamp01(state.MicPeak),
            state.TouchPosition,
            state.Axis0,
            attitude: editorAttitudeEnabled ? Quaternion.Euler(editorAttitudeEuler) : Quaternion.identity,
            attitudeTracked: editorAttitudeEnabled,
            attitudeReference: editorAttitudeEnabled ? 1u : 0u);

        return true;
    }
#endif

    private void Update()
    {
        var hasFrame = TryReadFrame(out var frame);
        var isNativeFrame = hasFrame;
        if (!hasFrame)
        {
            frame = AppleWatchInputFrame.Neutral;
        }

#if UNITY_EDITOR
        if (!hasFrame && TryReadEditorFrame(out frame))
            hasFrame = true;
#endif

        ProcessFrame(hasFrame, frame, Time.realtimeSinceStartupAsDouble, isNativeFrame);
        PumpRecordingPaths();
    }

    private void ProcessFrame(bool hasFrame, AppleWatchInputFrame frame, double realtime, bool isNativeFrame)
    {
        UpdateNativeRateWindow(realtime);
        if (hasFrame && isNativeFrame)
        {
            hasFrame = frame.TryGetCanonical(out frame)
                && frame.NativeQueueAgeSeconds < Mathf.Max(0.001f, staleTimeoutSeconds);
            if (hasFrame)
            {
                NativeFramesReceived++;
                nativeFramesInWindow++;
                LastNativeQueueAgeSeconds = frame.NativeQueueAgeSeconds;
                lastNativeFrameRealtime = realtime;
                hasNativeFrame = true;
            }
        }

        if (hasFrame)
        {
            lastContinuousFrame = frame.WithDiscretePulsesCleared();
            lastInputFrameRealtime = realtime - (isNativeFrame ? frame.NativeQueueAgeSeconds : 0f);
            // Native attitude age already includes queue wait. Unlike packet age,
            // it also includes time before an unrelated packet inherited this pose.
            lastAttitudeSampleRealtime = realtime - (isNativeFrame ? frame.AttitudeAgeSeconds : 0f);
            hasInputFrame = true;
        }
        else if (hasInputFrame && !IsStale(realtime))
        {
            frame = lastContinuousFrame;
        }
        else
        {
            frame = AppleWatchInputFrame.Neutral;
            lastContinuousFrame = AppleWatchInputFrame.Neutral;
            hasInputFrame = false;
        }

        if (frame.AttitudeTracked
            && realtime - lastAttitudeSampleRealtime >= Mathf.Max(0.001f, staleTimeoutSeconds))
        {
            frame = frame.WithAttitudeCleared();
            lastContinuousFrame = lastContinuousFrame.WithAttitudeCleared();
        }

        MotionSampleInterval = frame.MotionSampleInterval;

        // Each native message carries pulses, not a held button snapshot. Queue
        // their release before the next sample so consecutive pulses stay distinct.
        // Editor simulation supplies held states and must retain normal key semantics.
        if (hasFrame && isNativeFrame)
            SubmitFrame(frame.WithDiscretePulsesCleared());

        SubmitFrame(frame);
    }

    private static void SubmitFrame(AppleWatchInputFrame frame)
    {
        AppleWatchControllerDevice.SubmitState(
            frame.MoveX,
            frame.MoveY,
            frame.PrimaryButton,
            frame.TouchPress,
            frame.Tap,
            frame.DoubleTap,
            frame.PrimaryAction,
            frame.LongPress,
            frame.Drag,
            frame.SwipeUp,
            frame.SwipeDown,
            frame.SwipeLeft,
            frame.SwipeRight,
            frame.Crown,
            frame.Acceleration,
            frame.Gyro,
            frame.MicrophoneLevel,
            frame.MicrophonePeak,
            frame.TouchPosition,
            frame.Axis0,
            frame.Attitude,
            frame.AttitudeTracked,
            frame.AttitudeReference);
    }

    private void UpdateNativeRateWindow(double realtime)
    {
        if (!hasNativeRateWindow)
        {
            nativeRateWindowStart = realtime;
            hasNativeRateWindow = true;
            return;
        }

        var elapsed = realtime - nativeRateWindowStart;
        if (elapsed < 1d)
            return;

        NativeFramesPerSecond = (float)(nativeFramesInWindow / elapsed);
        nativeFramesInWindow = 0;
        nativeRateWindowStart = realtime;
    }

    private void ResetNativeDiagnostics()
    {
        NativeFramesPerSecond = 0f;
        NativeFramesReceived = 0;
        LastNativeQueueAgeSeconds = 0f;
        MotionSampleInterval = 0f;
        lastNativeFrameRealtime = 0d;
        hasNativeFrame = false;
        hasNativeRateWindow = false;
        nativeFramesInWindow = 0;
    }

    private void PumpRecordingPaths()
    {
        const int maxRecordingsPerUpdate = 8;
        for (var index = 0; index < maxRecordingsPerUpdate; index++)
        {
            var byteCount = WatchBridge_PopRecordingPath(recordingPathBuffer, recordingPathBuffer.Length);
            if (byteCount == 0)
                return;

            if (byteCount < 0)
            {
                recordingPathBuffer = new byte[Mathf.Max(recordingPathBuffer.Length, -byteCount)];
                continue;
            }

            var path = Encoding.UTF8.GetString(recordingPathBuffer, 0, byteCount);
            if (!string.IsNullOrWhiteSpace(path))
                RecordingReceived?.Invoke(path);
        }
    }

    private void OnDisable()
    {
        ResetNativeDiagnostics();
        AppleWatchControllerDevice.ResetState();
        lastContinuousFrame = AppleWatchInputFrame.Neutral;
        lastAttitudeSampleRealtime = 0d;
        hasInputFrame = false;

#if UNITY_EDITOR
        editorMove?.Disable();
        editorPress?.Disable();
        editorTouchPress?.Disable();
        editorTap?.Disable();
        editorDoubleTap?.Disable();
        editorDrag?.Disable();
        editorPrimaryAction?.Disable();
        editorLongPress?.Disable();
        editorSwipeUp?.Disable();
        editorSwipeDown?.Disable();
        editorSwipeLeft?.Disable();
        editorSwipeRight?.Disable();
        editorCrown?.Disable();
        editorAccel?.Disable();
        editorGyro?.Disable();
        editorTouchPosition?.Disable();
        editorAxis0?.Disable();
        editorMicrophoneLevel?.Disable();
        editorMicrophonePeak?.Disable();
#endif
    }

    private bool IsStale(double realtime)
    {
        return realtime - lastInputFrameRealtime >= Mathf.Max(0.001f, staleTimeoutSeconds);
    }

#if UNITY_EDITOR
    private void OnDestroy()
    {
        editorMove?.Dispose();
        editorPress?.Dispose();
        editorTouchPress?.Dispose();
        editorTap?.Dispose();
        editorDoubleTap?.Dispose();
        editorDrag?.Dispose();
        editorPrimaryAction?.Dispose();
        editorLongPress?.Dispose();
        editorSwipeUp?.Dispose();
        editorSwipeDown?.Dispose();
        editorSwipeLeft?.Dispose();
        editorSwipeRight?.Dispose();
        editorCrown?.Dispose();
        editorAccel?.Dispose();
        editorGyro?.Dispose();
        editorTouchPosition?.Dispose();
        editorAxis0?.Dispose();
        editorMicrophoneLevel?.Dispose();
        editorMicrophonePeak?.Dispose();
    }
#endif
}
