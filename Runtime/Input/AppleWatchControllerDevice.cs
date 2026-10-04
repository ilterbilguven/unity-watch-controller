using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

/// <summary>Exposes Watch input as a custom Unity Input System device.</summary>
/// <remarks>
/// Bind actions with paths such as <c>&lt;AppleWatchController&gt;/primaryStick</c>.
/// The receiver owns native pulse release and stale-input handling; this device only queues state.
/// Controls become available after Input System setup. Use Input System APIs from Unity's main thread.
/// </remarks>
[InputControlLayout(displayName = "Apple Watch Controller", stateType = typeof(AppleWatchControllerState))]
public class AppleWatchControllerDevice : InputDevice
{
    private const string LayoutName = "AppleWatchController";
    /// <summary>Gets two-axis movement, normally in [-1, 1] per component.</summary>
    [InputControl(name = "primaryStick", layout = "Vector2")]
    public Vector2Control primaryStick { get; private set; }

    /// <summary>Gets the primary-button pulse control; Editor simulation can supply a held state.</summary>
    [InputControl(name = "primaryButton", layout = "Button")]
    public ButtonControl primaryButton { get; private set; }

    /// <summary>Gets the touch-press pulse control.</summary>
    [InputControl(name = "touchPress", layout = "Button")]
    public ButtonControl touchPress { get; private set; }

    /// <summary>Gets the tap pulse control.</summary>
    [InputControl(name = "tap", layout = "Button")]
    public ButtonControl tap { get; private set; }

    /// <summary>Gets the double-tap pulse control.</summary>
    [InputControl(name = "doubleTap", layout = "Button")]
    public ButtonControl doubleTap { get; private set; }

    /// <summary>Gets the shared primary-action pulse control.</summary>
    /// <remarks>Several control kinds use this channel; it does not identify a hand gesture or other input origin.</remarks>
    [InputControl(name = "primaryAction", layout = "Button")]
    public ButtonControl primaryAction { get; private set; }

    /// <summary>Gets the long-press pulse control.</summary>
    [InputControl(name = "longPress", layout = "Button")]
    public ButtonControl longPress { get; private set; }

    /// <summary>Gets a pulse for a drag update; read touch position separately.</summary>
    [InputControl(name = "drag", layout = "Button")]
    public ButtonControl drag { get; private set; }

    /// <summary>Gets the upward-swipe pulse control.</summary>
    [InputControl(name = "swipeUp", layout = "Button")]
    public ButtonControl swipeUp { get; private set; }

    /// <summary>Gets the downward-swipe pulse control.</summary>
    [InputControl(name = "swipeDown", layout = "Button")]
    public ButtonControl swipeDown { get; private set; }

    /// <summary>Gets the leftward-swipe pulse control.</summary>
    [InputControl(name = "swipeLeft", layout = "Button")]
    public ButtonControl swipeLeft { get; private set; }

    /// <summary>Gets the rightward-swipe pulse control.</summary>
    [InputControl(name = "swipeRight", layout = "Button")]
    public ButtonControl swipeRight { get; private set; }

    /// <summary>Gets a per-sample Crown delta, not a rate to integrate every Unity frame.</summary>
    /// <remarks>Native frames clamp to [-1, 1]; direct submissions and Editor simulation need not.</remarks>
    [InputControl(name = "crown", layout = "Axis")]
    public AxisControl crown { get; private set; }

    /// <summary>Gets gravity-free acceleration in Watch coordinates, in g.</summary>
    [InputControl(name = "acceleration", layout = "Vector3")]
    public Vector3Control acceleration { get; private set; }

    /// <summary>Gets angular velocity in Watch coordinates, in radians per second.</summary>
    [InputControl(name = "gyro", layout = "Vector3")]
    public Vector3Control gyro { get; private set; }

    /// <summary>Gets the average microphone meter in [0, 1], not an audio waveform.</summary>
    [InputControl(name = "microphoneLevel", layout = "Axis")]
    public AxisControl microphoneLevel { get; private set; }

    /// <summary>Gets the peak microphone meter in [0, 1], not linear amplitude.</summary>
    [InputControl(name = "microphonePeak", layout = "Axis")]
    public AxisControl microphonePeak { get; private set; }

    /// <summary>Gets touch coordinates in SwiftUI points in the emitting control's coordinate space, without screen-size normalization.</summary>
    [InputControl(name = "touchPosition", layout = "Vector2")]
    public Vector2Control touchPosition { get; private set; }

    /// <summary>Gets the generic scalar channel; its source defines the useful range.</summary>
    [InputControl(name = "axis0", layout = "Axis")]
    public AxisControl axis0 { get; private set; }

    /// <summary>Gets the normalized raw right-handed Core Motion quaternion, or identity when untracked.</summary>
    /// <remarks>
    /// Check <see cref="attitudeTracked"/>. A valid identity pose can leave a Value action waiting;
    /// read the resolved quaternion control for that tracked device when the action has no active control.
    /// </remarks>
    [InputControl(name = "attitude", layout = "Quaternion")]
    public QuaternionControl attitude { get; private set; }

    /// <summary>Gets the held validity flag for attitude; unlike gesture buttons, this is not a pulse.</summary>
    [InputControl(name = "attitudeTracked", layout = "Button")]
    public ButtonControl attitudeTracked { get; private set; }

    /// <summary>Gets the motion-session reference, with zero reserved for untracked input.</summary>
    /// <remarks>Read as <c>int</c>, then use <c>unchecked((uint)value)</c> to preserve all reference bits.</remarks>
    [InputControl(name = "attitudeReference", layout = "Integer")]
    public IntegerControl attitudeReference { get; private set; }

    private static AppleWatchControllerDevice _current;
    private static readonly object SyncRoot = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        RegisterLayout();
    }

    private static void RegisterLayout()
    {
        InputSystem.RegisterLayout<AppleWatchControllerDevice>(LayoutName);
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    /// <summary>Gets a device owned by the active Input System manager, creating one when needed.</summary>
    /// <remarks>The lookup checks instance identity after manager resets; a cached device's added flag alone is insufficient.</remarks>
    public static AppleWatchControllerDevice Current
    {
        get
        {
            lock (SyncRoot)
            {
                // Saved devices can retain their added index across an InputSystem
                // manager reset; the active manager must still own this instance.
                if (_current != null && _current.added
                    && ReferenceEquals(InputSystem.GetDeviceById(_current.deviceId), _current))
                    return _current;

                _current = InputSystem.GetDevice<AppleWatchControllerDevice>();
                if (_current == null)
                {
                    RegisterLayout();
                    _current = (AppleWatchControllerDevice)InputSystem.AddDevice(LayoutName);
                }

                return _current;
            }
        }
    }

    /// <summary>Queues a complete device state for a later Input System update.</summary>
    /// <param name="x">Horizontal movement, normally in [-1, 1].</param>
    /// <param name="y">Vertical movement, normally in [-1, 1].</param>
    /// <param name="primaryButton">Primary-button state.</param>
    /// <param name="touchPress">Touch-press state.</param>
    /// <param name="tap">Tap state.</param>
    /// <param name="doubleTap">Double-tap state.</param>
    /// <param name="primaryAction">Shared primary-action state; not an input-origin marker.</param>
    /// <param name="longPress">Long-press state.</param>
    /// <param name="drag">Drag-update state.</param>
    /// <param name="swipeUp">Upward-swipe state.</param>
    /// <param name="swipeDown">Downward-swipe state.</param>
    /// <param name="swipeLeft">Leftward-swipe state.</param>
    /// <param name="swipeRight">Rightward-swipe state.</param>
    /// <param name="crown">Crown delta for this event, not an accumulated position or rate.</param>
    /// <param name="acceleration">Gravity-free acceleration in Watch coordinates, in g.</param>
    /// <param name="gyro">Angular velocity in Watch coordinates, in radians per second.</param>
    /// <param name="microphoneLevel">Average microphone meter, normally in [0, 1].</param>
    /// <param name="microphonePeak">Peak microphone meter, normally in [0, 1].</param>
    /// <param name="touchPosition">Touch position in SwiftUI points in the emitting control's coordinate space.</param>
    /// <param name="axis0">Generic scalar channel.</param>
    /// <param name="attitude">Raw right-handed quaternion to normalize.</param>
    /// <param name="attitudeTracked">Whether the caller reports a tracked pose.</param>
    /// <param name="attitudeReference">Nonzero motion-session reference required for tracking.</param>
    /// <remarks>
    /// Invalid attitude becomes untracked identity. Other channels pass through without validation or clamping.
    /// This method does not release pulses or expire stale state; callers must do that explicitly.
    /// </remarks>
    public static void SubmitState(
        float x,
        float y,
        bool primaryButton,
        bool touchPress = false,
        bool tap = false,
        bool doubleTap = false,
        bool primaryAction = false,
        bool longPress = false,
        bool drag = false,
        bool swipeUp = false,
        bool swipeDown = false,
        bool swipeLeft = false,
        bool swipeRight = false,
        float crown = 0f,
        Vector3 acceleration = default,
        Vector3 gyro = default,
        float microphoneLevel = 0f,
        float microphonePeak = 0f,
        Vector2 touchPosition = default,
        float axis0 = 0f,
        Quaternion attitude = default,
        bool attitudeTracked = false,
        uint attitudeReference = 0)
    {
        var device = Current;
        if (device == null || !device.added)
            return;

        var normalized = Quaternion.identity;
        var tracked = attitudeTracked && attitudeReference != 0 && AppleWatchInputFrame.TryNormalizeAttitude(attitude, out normalized);
        var state = new AppleWatchControllerState
        {
            primaryStick = new Vector2(x, y),
            primaryButton = (byte)(primaryButton ? 1 : 0),
            touchPress = (byte)(touchPress ? 1 : 0),
            tap = (byte)(tap ? 1 : 0),
            doubleTap = (byte)(doubleTap ? 1 : 0),
            primaryAction = (byte)(primaryAction ? 1 : 0),
            longPress = (byte)(longPress ? 1 : 0),
            drag = (byte)(drag ? 1 : 0),
            swipeUp = (byte)(swipeUp ? 1 : 0),
            swipeDown = (byte)(swipeDown ? 1 : 0),
            swipeLeft = (byte)(swipeLeft ? 1 : 0),
            swipeRight = (byte)(swipeRight ? 1 : 0),
            crown = crown,
            acceleration = acceleration,
            gyro = gyro,
            microphoneLevel = microphoneLevel,
            microphonePeak = microphonePeak,
            touchPosition = touchPosition,
            axis0 = axis0,
            attitude = normalized,
            attitudeTracked = (byte)(tracked ? 1 : 0),
            attitudeReference = tracked ? attitudeReference : 0
        };

        InputSystem.QueueStateEvent(device, state);
    }

    /// <summary>Queues neutral input, including untracked identity attitude; creates the device if necessary.</summary>
    public static void ResetState()
    {
        SubmitState(0f, 0f, false);
    }

    /// <summary>Resolves the typed child controls after the Input System constructs this device.</summary>
    protected override void FinishSetup()
    {
        primaryStick = GetChildControl<Vector2Control>("primaryStick");
        primaryButton = GetChildControl<ButtonControl>("primaryButton");
        touchPress = GetChildControl<ButtonControl>("touchPress");
        tap = GetChildControl<ButtonControl>("tap");
        doubleTap = GetChildControl<ButtonControl>("doubleTap");
        primaryAction = GetChildControl<ButtonControl>("primaryAction");
        longPress = GetChildControl<ButtonControl>("longPress");
        drag = GetChildControl<ButtonControl>("drag");
        swipeUp = GetChildControl<ButtonControl>("swipeUp");
        swipeDown = GetChildControl<ButtonControl>("swipeDown");
        swipeLeft = GetChildControl<ButtonControl>("swipeLeft");
        swipeRight = GetChildControl<ButtonControl>("swipeRight");
        crown = GetChildControl<AxisControl>("crown");
        acceleration = GetChildControl<Vector3Control>("acceleration");
        gyro = GetChildControl<Vector3Control>("gyro");
        microphoneLevel = GetChildControl<AxisControl>("microphoneLevel");
        microphonePeak = GetChildControl<AxisControl>("microphonePeak");
        touchPosition = GetChildControl<Vector2Control>("touchPosition");
        axis0 = GetChildControl<AxisControl>("axis0");
        attitude = GetChildControl<QuaternionControl>("attitude");
        attitudeTracked = GetChildControl<ButtonControl>("attitudeTracked");
        attitudeReference = GetChildControl<IntegerControl>("attitudeReference");
        base.FinishSetup();
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is not AppleWatchControllerDevice watchDevice)
            return;

        if (change == InputDeviceChange.Added)
        {
            lock (SyncRoot)
            {
                _current = watchDevice;
            }
            return;
        }

        if (change == InputDeviceChange.Removed)
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(_current, watchDevice))
                    _current = null;
            }
        }
    }
}

/// <summary>Defines the 96-byte Input System state layout for the Watch device.</summary>
/// <remarks>This is not the 216-byte native transport frame. Its explicit offsets are part of the device layout contract.</remarks>
[StructLayout(LayoutKind.Explicit, Size = 96)]
public struct AppleWatchControllerState : IInputStateTypeInfo
{
    /// <summary>Gets the AWC1 format identifier used to match queued events to this layout.</summary>
    public static FourCC Format => new('A', 'W', 'C', '1');

    /// <summary>Movement in [-1, 1] per component for canonical native samples.</summary>
    [InputControl(layout = "Vector2", name = "primaryStick", format = "VC2F")]
    [FieldOffset(0)] public Vector2 primaryStick;

    /// <summary>Primary-button state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "primaryButton", format = "BIT")]
    [FieldOffset(8)] public byte primaryButton;

    /// <summary>Touch-press state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "touchPress", format = "BIT")]
    [FieldOffset(9)] public byte touchPress;

    /// <summary>Tap state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "tap", format = "BIT")]
    [FieldOffset(10)] public byte tap;

    /// <summary>Double-tap state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "doubleTap", format = "BIT")]
    [FieldOffset(11)] public byte doubleTap;

    /// <summary>Shared primary-action state encoded in the low bit; it does not identify the input origin.</summary>
    [InputControl(layout = "Button", name = "primaryAction", format = "BIT")]
    [FieldOffset(12)] public byte primaryAction;

    /// <summary>Long-press state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "longPress", format = "BIT")]
    [FieldOffset(13)] public byte longPress;

    /// <summary>Drag-update state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "drag", format = "BIT")]
    [FieldOffset(18)] public byte drag;

    /// <summary>Upward-swipe state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "swipeUp", format = "BIT")]
    [FieldOffset(14)] public byte swipeUp;

    /// <summary>Downward-swipe state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "swipeDown", format = "BIT")]
    [FieldOffset(15)] public byte swipeDown;

    /// <summary>Leftward-swipe state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "swipeLeft", format = "BIT")]
    [FieldOffset(16)] public byte swipeLeft;

    /// <summary>Rightward-swipe state encoded in the low bit.</summary>
    [InputControl(layout = "Button", name = "swipeRight", format = "BIT")]
    [FieldOffset(17)] public byte swipeRight;

    /// <summary>Crown delta for one event; consumers must not integrate the stored value each frame.</summary>
    [InputControl(layout = "Axis", name = "crown", format = "FLT")]
    [FieldOffset(20)] public float crown;

    /// <summary>Gravity-free acceleration in Watch coordinates, in g.</summary>
    [InputControl(layout = "Vector3", name = "acceleration", format = "VEC3")]
    [FieldOffset(24)] public Vector3 acceleration;

    /// <summary>Angular velocity in Watch coordinates, in radians per second.</summary>
    [InputControl(layout = "Vector3", name = "gyro", format = "VEC3")]
    [FieldOffset(36)] public Vector3 gyro;

    /// <summary>Average microphone meter, normally in [0, 1].</summary>
    [InputControl(layout = "Axis", name = "microphoneLevel", format = "FLT")]
    [FieldOffset(48)] public float microphoneLevel;

    /// <summary>Peak microphone meter, normally in [0, 1].</summary>
    [InputControl(layout = "Axis", name = "microphonePeak", format = "FLT")]
    [FieldOffset(52)] public float microphonePeak;

    /// <summary>Touch coordinates in SwiftUI points in the emitting control's coordinate space.</summary>
    [InputControl(layout = "Vector2", name = "touchPosition", format = "VC2F")]
    [FieldOffset(56)] public Vector2 touchPosition;

    /// <summary>Generic scalar with a source-defined range.</summary>
    [InputControl(layout = "Axis", name = "axis0", format = "FLT")]
    [FieldOffset(64)] public float axis0;

    /// <summary>Normalized raw right-handed quaternion; use identity when untracked.</summary>
    [InputControl(layout = "Quaternion", name = "attitude", format = "QUAT")]
    [InputControl(name = "attitude/w", defaultState = 1f)]
    [FieldOffset(72)] public Quaternion attitude;

    /// <summary>Held attitude-validity flag encoded in the low bit; this is not a gesture pulse.</summary>
    [InputControl(layout = "Button", name = "attitudeTracked", format = "BIT")]
    [FieldOffset(88)] public byte attitudeTracked;

    // IntegerControl is signed. INT preserves all epoch bits without its UINT
    // range assertion; consumers use unchecked((uint)attitudeReference.ReadValue()).
    /// <summary>Motion-session reference bits; zero means untracked. The exposed IntegerControl reads a signed int.</summary>
    [InputControl(layout = "Integer", name = "attitudeReference", format = "INT")]
    [FieldOffset(92)] public uint attitudeReference;

    /// <summary>Gets the event format required by <see cref="IInputStateTypeInfo"/>.</summary>
    public FourCC format => Format;

    /// <summary>Gets the fixed Input System state size in bytes, not the native transport size.</summary>
    public int sizeInBytes => 96;
}
