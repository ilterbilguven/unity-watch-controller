using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Selects whether the demo shows a relative pose or an angular-velocity indicator.</summary>
public enum MotionDisplayMode
{
    /// <summary>Shows the tracked pose relative to the current center; a still Watch keeps its angle.</summary>
    Orientation,
    /// <summary>Shows a bounded rotation derived from angular velocity; a still Watch returns the indicator to center.</summary>
    AngularVelocity
}

/// <summary>Turns Watch Input System actions into movement, game events, and scene feedback.</summary>
/// <remarks>
/// Owns its action map and enables it with this component. Disabling restores captured transforms and material properties.
/// Crown callbacks apply each delta once. Tick samples continuous channels and maps raw Core Motion attitude into Unity coordinates.
/// The demo prevents screen sleep only while enabled, focused, and not paused, then restores the previous setting.
/// </remarks>
[DisallowMultipleComponent]
public sealed class AppleWatchDemoController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform crownDial;
    [SerializeField, Min(0f)] private float crownDegreesPerUnit = 1f;
    [SerializeField] private Transform motionRig;
    [SerializeField] private Transform gyroIndicator;
    [SerializeField] private MotionDisplayMode motionDisplayMode = MotionDisplayMode.Orientation;
    [SerializeField] private Transform touchMarker;
    [SerializeField] private Transform audioBar;
    [SerializeField] private Transform valueIndicator;
    [SerializeField] private WatchInputReceiver receiver;
    [SerializeField] private Renderer actorRenderer;

    private const float MoveSpeed = 3f;
    private const float HalfExtent = 3.5f;
    private const float HopDuration = 0.45f;
    private InputActionMap actions;
    private InputAction move, acceleration, gyro, microphoneLevel, microphonePeak, touchPosition, axis0, touchPress, drag;
    private InputAction attitude, attitudeTracked, attitudeReference;
    private WatchInputReceiver subscribedReceiver;
    private PoseSnapshot actorPose, dialPose, motionPose, gyroPose, touchPose, audioPose, valuePose;
    private bool capturedPose;
    private float hopRemaining;
    private float hopHeight;
    private int colorIndex;
    private Renderer audioRenderer;
    private MaterialPropertyBlock actorOriginalProperties, actorProperties, audioOriginalProperties, audioProperties;
    private int previousSleepTimeout;
    private bool ownsSleepTimeout;
    private bool demoEnabled, applicationPaused, applicationFocused;
    private bool hasOrientationReference;
    private uint orientationReferenceEpoch;
    private Quaternion mappedAttitude = Quaternion.identity, orientationReference = Quaternion.identity;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly Color[] ActorColors = { new(0.1f, 0.9f, 1f), new(1f, 0.3f, 0.65f), new(1f, 0.75f, 0.1f) };

    /// <summary>Gets movement from the last tick, with vector magnitude clamped to one.</summary>
    public Vector2 MoveInput { get; private set; }
    /// <summary>Gets accumulated Crown deltas since reset or disable, before the degrees-per-unit scale.</summary>
    public float CrownTotal { get; private set; }
    /// <summary>Gets gravity-free Watch acceleration from the last tick, in g.</summary>
    public Vector3 Acceleration { get; private set; }
    /// <summary>Gets Watch angular velocity from the last tick, in radians per second.</summary>
    public Vector3 Gyro { get; private set; }
    /// <summary>Gets the raw right-handed attitude read on the last tick, or identity when the tracking flag is off.</summary>
    /// <remarks>Use AttitudeTracked for validity; identity itself does not mean tracking is unavailable.</remarks>
    public Quaternion Attitude { get; private set; } = Quaternion.identity;
    /// <summary>Gets whether the last tick found a valid attitude with a nonzero motion-session reference.</summary>
    public bool AttitudeTracked { get; private set; }
    /// <summary>Gets the motion-session reference read on the last tick, or zero when the tracking flag is off.</summary>
    public uint AttitudeReference { get; private set; }
    /// <summary>Gets the normalized Unity-space rotation relative to the center pose, or identity when untracked.</summary>
    public Quaternion RelativeAttitude { get; private set; } = Quaternion.identity;
    /// <summary>Gets whether the latest sampled pose can become the orientation center.</summary>
    public bool CanCenterOrientation => AttitudeTracked;
    /// <summary>Gets the player-facing tracking status for the HUD.</summary>
    public string OrientationStatus => AttitudeTracked ? "Tracked orientation • reference set" : "Orientation unavailable (off or stale)";
    /// <summary>Gets or sets the motion indicator mode and immediately applies the latest sampled values.</summary>
    public MotionDisplayMode MotionMode
    {
        get => motionDisplayMode;
        set { motionDisplayMode = value; ApplyMotionVisualization(); }
    }
    /// <summary>Gets the average microphone meter from the last tick, clamped to [0, 1].</summary>
    public float MicrophoneLevel { get; private set; }
    /// <summary>Gets the peak microphone meter from the last tick, clamped to [0, 1].</summary>
    public float MicrophonePeak { get; private set; }
    /// <summary>Gets the last sampled touch position in SwiftUI points in the emitting control's coordinate space, not Unity world coordinates.</summary>
    public Vector2 TouchPosition { get; private set; }
    /// <summary>Gets the unmodified generic scalar channel from the last tick.</summary>
    public float Axis0 { get; private set; }
    /// <summary>Gets the latest emitted event name, or the initial waiting message.</summary>
    public string LastEvent { get; private set; } = "Waiting for Watch input";
    /// <summary>Gets the number of emitted events during this instance's lifetime; reset does not clear it.</summary>
    public int EventCount { get; private set; }
    /// <summary>Gets recording-path notifications received during this instance's lifetime, not successful playbacks.</summary>
    public int RecordingCount { get; private set; }
    /// <summary>Gets the latest recording's file name without its directory, or None before receipt.</summary>
    public string LastRecordingName { get; private set; } = "None";
    /// <summary>Raised after a demo reaction or status change, with its case-sensitive event name.</summary>
    /// <remarks>
    /// Names include PrimaryButton, PrimaryAction, Tap, DoubleTap, TouchPress, Drag, SwipeUp, SwipeDown,
    /// SwipeLeft, SwipeRight, Crown, Reset, OrientationCentered, and RecordingReceived.
    /// The recording event carries no path; use the receiver's separate recording event when a path is required.
    /// </remarks>
    public event Action<string> DemoEvent;

    private void OnEnable()
    {
        // Bindings must resolve against the custom device even before transport's first frame.
        _ = AppleWatchControllerDevice.Current;
        CaptureAuthoredPose();
        if (actions == null) CreateActions();
        actions.Enable();
        demoEnabled = true;
        applicationFocused = Application.isFocused;
        UpdateScreenSleep();
        if (subscribedReceiver != receiver)
        {
            UnsubscribeReceiver();
            subscribedReceiver = receiver;
            if (subscribedReceiver != null) subscribedReceiver.RecordingReceived += OnRecordingReceived;
        }
    }

    private void CreateActions()
    {
        actions = new InputActionMap("AppleWatchDemo");
        move = Value("primaryStick");
        acceleration = Value("acceleration");
        gyro = Value("gyro");
        attitude = Value("attitude");
        attitudeTracked = Value("attitudeTracked");
        attitudeReference = Value("attitudeReference");
        microphoneLevel = Value("microphoneLevel");
        microphonePeak = Value("microphonePeak");
        touchPosition = Value("touchPosition");
        axis0 = Value("axis0");
        touchPress = Button("touchPress", "TouchPress", () => { });
        drag = Button("drag", "Drag", () => { });
        Button("primaryButton", "PrimaryButton", () => Hop(0.6f));
        Button("primaryAction", "PrimaryAction", () => Hop(0.6f));
        Button("tap", "Tap", () => Hop(0.6f));
        Button("doubleTap", "DoubleTap", () =>
        {
            Hop(1.1f);
            colorIndex = (colorIndex + 1) % ActorColors.Length;
            if (actorRenderer == null) return;
            actorProperties.SetColor(BaseColorId, ActorColors[colorIndex]);
            actorProperties.SetColor(ColorId, ActorColors[colorIndex]);
            actorRenderer.SetPropertyBlock(actorProperties);
        });
        Button("swipeUp", "SwipeUp", () => Dash(Vector3.forward));
        Button("swipeDown", "SwipeDown", () => Dash(Vector3.back));
        Button("swipeLeft", "SwipeLeft", () => Dash(Vector3.left));
        Button("swipeRight", "SwipeRight", () => Dash(Vector3.right));
        var reset = actions.AddAction("longPress", InputActionType.Button, "<AppleWatchController>/longPress");
        reset.performed += _ => ResetDemo();

        // PassThrough has no initial-state check on re-enable. A stored crown delta
        // must never be replayed or integrated each frame as though it were a rate.
        var crown = actions.AddAction("crown", InputActionType.PassThrough, "<AppleWatchController>/crown");
        crown.performed += context =>
        {
            var delta = context.ReadValue<float>();
            if (Mathf.Approximately(delta, 0f)) return;
            CrownTotal += delta;
            if (crownDial != null)
                crownDial.localRotation = dialPose.Rotation * Quaternion.Euler(0f, CrownTotal * crownDegreesPerUnit, 0f);
            Emit("Crown");
        };
    }

    private InputAction Value(string control) => actions.AddAction(control, InputActionType.Value, $"<AppleWatchController>/{control}");

    private InputAction Button(string control, string eventName, Action reaction)
    {
        var action = actions.AddAction(control, InputActionType.Button, $"<AppleWatchController>/{control}");
        action.performed += _ =>
        {
            reaction();
            Emit(eventName);
        };
        return action;
    }

    private void Update() => Tick(Time.deltaTime);

    /// <summary>Advances the kinematic demo using the current Watch input snapshot.</summary>
    /// <param name="deltaTime">Elapsed seconds; negative and nonfinite values become a zero movement step.</param>
    /// <remarks>
    /// Does nothing until the owned action map is enabled. Update calls this automatically.
    /// Even a zero step refreshes sensor values. Crown deltas arrive through action callbacks, not this method.
    /// </remarks>
    public void Tick(float deltaTime)
    {
        if (actions == null || !actions.enabled) return;
        CaptureAuthoredPose();
        var step = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ? 0f : Mathf.Max(0f, deltaTime);
        MoveInput = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
        Acceleration = acceleration.ReadValue<Vector3>();
        Gyro = gyro.ReadValue<Vector3>();
        ReadOrientation();
        MicrophoneLevel = Mathf.Clamp01(microphoneLevel.ReadValue<float>());
        MicrophonePeak = Mathf.Clamp01(microphonePeak.ReadValue<float>());
        TouchPosition = touchPosition.ReadValue<Vector2>();
        Axis0 = axis0.ReadValue<float>();

        if (target != null)
        {
            var position = target.position + new Vector3(MoveInput.x, 0f, MoveInput.y) * (MoveSpeed * step);
            hopRemaining = Mathf.Max(0f, hopRemaining - step);
            var pulse = hopRemaining > 0f ? Mathf.Sin((1f - hopRemaining / HopDuration) * Mathf.PI) : 0f;
            position.y = actorPose.WorldPosition.y + pulse * hopHeight;
            target.position = BoundPosition(position);
            target.localScale = actorPose.Scale * (1f + pulse * 0.15f);
        }
        if (motionRig != null)
            motionRig.localRotation = motionPose.Rotation * Quaternion.Euler(
                Mathf.Clamp(Acceleration.z, -1f, 1f) * 30f, 0f,
                -Mathf.Clamp(Acceleration.x, -1f, 1f) * 30f);
        ApplyMotionVisualization();

        if (touchMarker != null)
        {
            if (touchPress.IsPressed() || drag.IsPressed())
            {
                // Watch locations are raw SwiftUI points, not world coordinates.
                // This fixed 211x257 reference canvas is a bounded visualization only.
                touchMarker.position = touchPose.WorldPosition + new Vector3(
                    (Mathf.Clamp01(TouchPosition.x / 211f) * 2f - 1f) * HalfExtent, 0f,
                    (1f - Mathf.Clamp01(TouchPosition.y / 257f) * 2f) * HalfExtent);
            }
            else touchMarker.localPosition = touchPose.Position;
        }
        if (audioBar != null)
        {
            audioBar.localScale = new Vector3(audioPose.Scale.x, audioPose.Scale.y * (1f + MicrophoneLevel * 3f), audioPose.Scale.z);
            if (audioRenderer != null)
            {
                var color = Color.Lerp(new Color(0.1f, 0.45f, 0.5f), Color.cyan, MicrophonePeak);
                audioProperties.SetColor(BaseColorId, color);
                audioProperties.SetColor(ColorId, color);
                audioProperties.SetColor(EmissionColorId, color * MicrophonePeak);
                if (MicrophoneLevel == 0f && MicrophonePeak == 0f) audioRenderer.SetPropertyBlock(audioOriginalProperties);
                else audioRenderer.SetPropertyBlock(audioProperties);
            }
        }
        if (valueIndicator != null)
            valueIndicator.localScale = new Vector3(valuePose.Scale.x,
                valuePose.Scale.y * (1f + Mathf.Clamp01(Mathf.Abs(Axis0)) * 2f), valuePose.Scale.z);
    }

    private void Hop(float height)
    {
        hopHeight = height;
        hopRemaining = HopDuration;
    }

    private void ReadOrientation()
    {
        var tracked = attitudeTracked.IsPressed();
        AttitudeReference = tracked ? unchecked((uint)attitudeReference.ReadValue<int>()) : 0u;
        Attitude = tracked ? ReadRawAttitude() : Quaternion.identity;
        // This is the only Core Motion (right-handed) to Unity basis conversion.
        var mapped = new Quaternion(Attitude.x, Attitude.y, -Attitude.z, -Attitude.w);
        AttitudeTracked = tracked && AttitudeReference != 0
            && AppleWatchInputFrame.TryNormalizeAttitude(mapped, out mappedAttitude);
        if (!AttitudeTracked)
        {
            RelativeAttitude = Quaternion.identity;
            return;
        }
        if (!hasOrientationReference || orientationReferenceEpoch != AttitudeReference)
        {
            orientationReference = mappedAttitude;
            orientationReferenceEpoch = AttitudeReference;
            hasOrientationReference = true;
        }
        RelativeAttitude = (Quaternion.Inverse(orientationReference) * mappedAttitude).normalized;
    }

    private Quaternion ReadRawAttitude()
    {
        if (attitude.activeControl != null) return attitude.ReadValue<Quaternion>();
        // An identity-valued control can leave a Value action waiting, whose
        // ReadValue returns Quaternion.zero. Read its resolved control, not an invented pose.
        var trackedDevice = attitudeTracked.activeControl?.device;
        foreach (var control in attitude.controls)
            if (control is QuaternionControl quaternion && control.device == trackedDevice)
                return quaternion.ReadValue();
        return default;
    }

    /// <summary>Uses the latest valid mapped attitude as the center pose and updates the indicator.</summary>
    /// <returns>True after centering and emitting OrientationCentered; false when the last sampled pose is untracked.</returns>
    /// <remarks>This is a local display reference; it does not change the Watch sensor or its motion-session reference.</remarks>
    public bool CenterOrientation()
    {
        if (!AttitudeTracked) return false;
        orientationReference = mappedAttitude;
        orientationReferenceEpoch = AttitudeReference;
        hasOrientationReference = true;
        RelativeAttitude = Quaternion.identity;
        ApplyMotionVisualization();
        Emit("OrientationCentered");
        return true;
    }

    private void ApplyMotionVisualization()
    {
        if (gyroIndicator == null) return;
        var rotation = motionDisplayMode == MotionDisplayMode.AngularVelocity
            ? Quaternion.Euler(Vector3.ClampMagnitude(Gyro, 5f) * 45f)
            : RelativeAttitude;
        gyroIndicator.localRotation = gyroPose.Rotation * rotation;
    }

    private void Dash(Vector3 direction)
    {
        if (target != null) target.position = BoundPosition(target.position + direction);
    }

    private Vector3 BoundPosition(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, actorPose.WorldPosition.x - HalfExtent, actorPose.WorldPosition.x + HalfExtent);
        position.z = Mathf.Clamp(position.z, actorPose.WorldPosition.z - HalfExtent, actorPose.WorldPosition.z + HalfExtent);
        return position;
    }

    /// <summary>Restores captured scene transforms and feedback, clears sampled values, and emits Reset.</summary>
    /// <remarks>
    /// Subscribers use Reset to restart game state. Event and recording counts remain intact.
    /// This does not reset the input device or discard the saved orientation center for the current motion session.
    /// </remarks>
    public void ResetDemo()
    {
        RestoreAuthoredPose();
        Emit("Reset");
    }

    private void CaptureAuthoredPose()
    {
        if (capturedPose || target == null) return;
        actorPose = new PoseSnapshot(target);
        dialPose = new PoseSnapshot(crownDial);
        motionPose = new PoseSnapshot(motionRig);
        gyroPose = new PoseSnapshot(gyroIndicator);
        touchPose = new PoseSnapshot(touchMarker);
        audioPose = new PoseSnapshot(audioBar);
        valuePose = new PoseSnapshot(valueIndicator);
        if (actorRenderer != null)
        {
            actorOriginalProperties = new MaterialPropertyBlock();
            actorProperties = new MaterialPropertyBlock();
            actorRenderer.GetPropertyBlock(actorOriginalProperties);
            actorRenderer.GetPropertyBlock(actorProperties);
        }
        audioRenderer = audioBar != null ? audioBar.GetComponent<Renderer>() : null;
        if (audioRenderer != null)
        {
            audioOriginalProperties = new MaterialPropertyBlock();
            audioProperties = new MaterialPropertyBlock();
            audioRenderer.GetPropertyBlock(audioOriginalProperties);
            audioRenderer.GetPropertyBlock(audioProperties);
        }
        capturedPose = true;
    }

    private void RestoreAuthoredPose()
    {
        if (capturedPose)
        {
            actorPose.Restore(target);
            dialPose.Restore(crownDial);
            motionPose.Restore(motionRig);
            gyroPose.Restore(gyroIndicator);
            touchPose.Restore(touchMarker);
            audioPose.Restore(audioBar);
            valuePose.Restore(valueIndicator);
            if (actorRenderer != null) actorRenderer.SetPropertyBlock(actorOriginalProperties);
            if (audioRenderer != null) audioRenderer.SetPropertyBlock(audioOriginalProperties);
        }
        MoveInput = Vector2.zero;
        CrownTotal = 0f;
        Acceleration = Vector3.zero;
        Gyro = Vector3.zero;
        Attitude = Quaternion.identity;
        AttitudeTracked = false;
        AttitudeReference = 0;
        RelativeAttitude = Quaternion.identity;
        MicrophoneLevel = 0f;
        MicrophonePeak = 0f;
        TouchPosition = Vector2.zero;
        Axis0 = 0f;
        hopRemaining = 0f;
        colorIndex = 0;
    }

    private void Emit(string eventName)
    {
        LastEvent = eventName;
        EventCount++;
        DemoEvent?.Invoke(eventName);
    }

    private void OnRecordingReceived(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        LastRecordingName = Path.GetFileName(path.Replace('\\', '/'));
        RecordingCount++;
        Emit("RecordingReceived");
    }

    private void UnsubscribeReceiver()
    {
        if (subscribedReceiver != null) subscribedReceiver.RecordingReceived -= OnRecordingReceived;
        subscribedReceiver = null;
    }

    private void OnDisable()
    {
        demoEnabled = false;
        RestoreScreenSleep();
        actions?.Disable();
        UnsubscribeReceiver();
        RestoreAuthoredPose();
    }

    private void OnDestroy()
    {
        demoEnabled = false;
        RestoreScreenSleep();
        UnsubscribeReceiver();
        actions?.Dispose();
        actions = null;
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        UpdateScreenSleep();
    }

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        UpdateScreenSleep();
    }

    private void UpdateScreenSleep()
    {
        // Watch input is not a phone touch. Hold the request only while this
        // demo is enabled and foregrounded; focus/pause callbacks can arrive in either order.
        if (demoEnabled && applicationFocused && !applicationPaused) PreventScreenSleep();
        else RestoreScreenSleep();
    }

    private void PreventScreenSleep()
    {
        if (ownsSleepTimeout) return;
        previousSleepTimeout = Screen.sleepTimeout;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        ownsSleepTimeout = true;
    }

    private void RestoreScreenSleep()
    {
        if (!ownsSleepTimeout) return;
        Screen.sleepTimeout = previousSleepTimeout;
        ownsSleepTimeout = false;
    }

    private readonly struct PoseSnapshot
    {
        public readonly Vector3 Position, WorldPosition, Scale;
        public readonly Quaternion Rotation;
        public PoseSnapshot(Transform transform)
        {
            Position = transform != null ? transform.localPosition : Vector3.zero;
            WorldPosition = transform != null ? transform.position : Vector3.zero;
            Scale = transform != null ? transform.localScale : Vector3.one;
            Rotation = transform != null ? transform.localRotation : Quaternion.identity;
        }
        public void Restore(Transform transform)
        {
            if (transform == null) return;
            transform.localPosition = Position;
            transform.localRotation = Rotation;
            transform.localScale = Scale;
        }
    }
}
