using System;
using UnityEngine;

/// <summary>Holds one decoded Watch input sample before it enters the Unity Input System.</summary>
/// <remarks>
/// Native button flags and Crown values are pulses. Movement, sensor values, and touch position are state.
/// Attitude uses raw right-handed Core Motion coordinates; the consumer must choose its coordinate conversion.
/// Recording files arrive separately through <see cref="WatchInputReceiver.RecordingReceived"/>.
/// </remarks>
public readonly struct AppleWatchInputFrame
{
    /// <summary>Size of the native binary frame, in bytes, including reserved bytes.</summary>
    public const int ByteLength = 216;
    private const int MinimumRequiredByteLength = 216;

    /// <summary>Horizontal movement; canonical native frames use the range [-1, 1].</summary>
    public readonly float MoveX;
    /// <summary>Vertical movement; canonical native frames use the range [-1, 1].</summary>
    public readonly float MoveY;
    /// <summary>Crown change for this sample, not a rate or an accumulated angle.</summary>
    /// <remarks>Native canonicalization clamps this value to [-1, 1]. Editor simulation preserves finite raw deltas.</remarks>
    public readonly float Crown;
    /// <summary>User acceleration in Watch coordinates, in g, with gravity removed by Core Motion.</summary>
    public readonly Vector3 Acceleration;
    /// <summary>Angular velocity in Watch coordinates, in radians per second; not an orientation.</summary>
    public readonly Vector3 Gyro;
    /// <summary>Average microphone power mapped from decibels to [0, 1], not raw audio samples.</summary>
    public readonly float MicrophoneLevel;
    /// <summary>Peak microphone power mapped from decibels to [0, 1], not linear amplitude.</summary>
    public readonly float MicrophonePeak;
    /// <summary>Touch location in SwiftUI points in the emitting control's coordinate space, not normalized or Unity world coordinates.</summary>
    public readonly Vector2 TouchPosition;
    /// <summary>Generic scalar supplied by the Watch control; the frame does not clamp its range.</summary>
    public readonly float Axis0;
    /// <summary>Native queue age at dequeue, in seconds; this is elapsed time, not a shared-clock timestamp.</summary>
    public readonly float NativeQueueAgeSeconds;
    private readonly Quaternion attitude;
    // Raw CoreMotion coordinates. Consumers choose their own coordinate conversion.
    /// <summary>Gets the normalized raw Core Motion quaternion, or identity when tracking is invalid.</summary>
    /// <remarks>Identity can also be a valid tracked pose. Read <see cref="AttitudeTracked"/> to distinguish it.</remarks>
    public Quaternion Attitude => AttitudeTracked ? attitude : Quaternion.identity;
    /// <summary>Whether the sample has a valid quaternion, nonzero reference, and finite nonnegative attitude age.</summary>
    public readonly bool AttitudeTracked;
    /// <summary>Nonzero motion-session reference used to detect a new orientation basis; zero means untracked.</summary>
    public readonly uint AttitudeReference;
    /// <summary>Seconds between motion samples, or zero when unavailable; this is not the Unity update interval.</summary>
    public readonly float MotionSampleInterval;
    // Native elapsed sample age at dequeue/encoding, including native queue wait.
    /// <summary>Age of the attitude sample in seconds, including native queue wait and time before a packet reused it.</summary>
    public readonly float AttitudeAgeSeconds;
    /// <summary>Primary-button pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool PrimaryButton;
    /// <summary>Touch-press pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool TouchPress;
    /// <summary>Tap pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool Tap;
    /// <summary>Double-tap pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool DoubleTap;
    /// <summary>Shared primary-action pulse in a native frame, or held key state in Editor simulation.</summary>
    /// <remarks>Several control kinds use this channel; it does not identify a hand gesture or other input origin.</remarks>
    public readonly bool PrimaryAction;
    /// <summary>Long-press pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool LongPress;
    /// <summary>Drag-update pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool Drag;
    /// <summary>Upward-swipe pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool SwipeUp;
    /// <summary>Downward-swipe pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool SwipeDown;
    /// <summary>Leftward-swipe pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool SwipeLeft;
    /// <summary>Rightward-swipe pulse in a native frame, or held key state in Editor simulation.</summary>
    public readonly bool SwipeRight;

    private const int MoveXOffset = 0;
    private const int MoveYOffset = 4;
    private const int PrimaryButtonOffset = 8;
    private const int TouchPressOffset = 9;
    private const int TapOffset = 10;
    private const int DoubleTapOffset = 11;
    private const int PrimaryActionOffset = 12;
    private const int LongPressOffset = 13;
    private const int DragOffset = 18;
    private const int SwipeUpOffset = 14;
    private const int SwipeDownOffset = 15;
    private const int SwipeLeftOffset = 16;
    private const int SwipeRightOffset = 17;
    private const int CrownOffset = 20;
    private const int AccelerationXOffset = 24;
    private const int AccelerationYOffset = 28;
    private const int AccelerationZOffset = 32;
    private const int GyroXOffset = 36;
    private const int GyroYOffset = 40;
    private const int GyroZOffset = 44;
    private const int MicLevelOffset = 48;
    private const int MicPeakOffset = 52;
    private const int TouchPosXOffset = 56;
    private const int TouchPosYOffset = 60;
    private const int Axis0Offset = 64;
    private const int NativeQueueAgeOffset = 68;
    private const int AttitudeOffset = 72;
    private const int AttitudeTrackedOffset = 88;
    private const int AttitudeReferenceOffset = 92;
    private const int MotionSampleIntervalOffset = 96;
    private const int AttitudeAgeOffset = 100;

    /// <summary>Zero input with no tracked attitude; its <see cref="Attitude"/> property returns identity.</summary>
    public static readonly AppleWatchInputFrame Neutral = new AppleWatchInputFrame();

    /// <summary>Creates a sample and validates its attitude metadata without clamping other channels.</summary>
    /// <param name="moveX">Horizontal movement, normally in [-1, 1].</param>
    /// <param name="moveY">Vertical movement, normally in [-1, 1].</param>
    /// <param name="primaryButton">Primary-button pulse or Editor held state.</param>
    /// <param name="touchPress">Touch-press pulse or Editor held state.</param>
    /// <param name="tap">Tap pulse or Editor held state.</param>
    /// <param name="doubleTap">Double-tap pulse or Editor held state.</param>
    /// <param name="primaryAction">Shared primary-action pulse or Editor held state; not an input-origin marker.</param>
    /// <param name="longPress">Long-press pulse or Editor held state.</param>
    /// <param name="drag">Drag-update pulse or Editor held state.</param>
    /// <param name="swipeUp">Upward-swipe pulse or Editor held state.</param>
    /// <param name="swipeDown">Downward-swipe pulse or Editor held state.</param>
    /// <param name="swipeLeft">Leftward-swipe pulse or Editor held state.</param>
    /// <param name="swipeRight">Rightward-swipe pulse or Editor held state.</param>
    /// <param name="crown">Crown delta for this sample; not an angular velocity.</param>
    /// <param name="acceleration">Gravity-free acceleration in Watch coordinates, in g.</param>
    /// <param name="gyro">Angular velocity in Watch coordinates, in radians per second.</param>
    /// <param name="microphoneLevel">Average microphone meter, normally in [0, 1].</param>
    /// <param name="microphonePeak">Peak microphone meter, normally in [0, 1].</param>
    /// <param name="touchPosition">Touch position in SwiftUI points in the emitting control's coordinate space.</param>
    /// <param name="axis0">Generic scalar; no range restriction is applied here.</param>
    /// <param name="nativeQueueAgeSeconds">Native elapsed queue age in seconds.</param>
    /// <param name="attitude">Raw right-handed quaternion; finite nonzero values are normalized.</param>
    /// <param name="attitudeTracked">Whether the sender reports an attitude sample.</param>
    /// <param name="attitudeReference">Nonzero motion-session reference required for tracking.</param>
    /// <param name="motionSampleInterval">Sample interval in seconds; invalid or negative values become zero.</param>
    /// <param name="attitudeAgeSeconds">Attitude age in seconds; invalid or negative values disable tracking.</param>
    public AppleWatchInputFrame(
        float moveX,
        float moveY,
        bool primaryButton,
        bool touchPress,
        bool tap,
        bool doubleTap,
        bool primaryAction,
        bool longPress,
        bool drag,
        bool swipeUp,
        bool swipeDown,
        bool swipeLeft,
        bool swipeRight,
        float crown,
        Vector3 acceleration,
        Vector3 gyro,
        float microphoneLevel,
        float microphonePeak,
        Vector2 touchPosition,
        float axis0,
        float nativeQueueAgeSeconds = 0f,
        Quaternion attitude = default,
        bool attitudeTracked = false,
        uint attitudeReference = 0,
        float motionSampleInterval = 0f,
        float attitudeAgeSeconds = 0f)
    {
        MoveX = moveX;
        MoveY = moveY;
        PrimaryButton = primaryButton;
        TouchPress = touchPress;
        Tap = tap;
        DoubleTap = doubleTap;
        PrimaryAction = primaryAction;
        LongPress = longPress;
        Drag = drag;
        SwipeUp = swipeUp;
        SwipeDown = swipeDown;
        SwipeLeft = swipeLeft;
        SwipeRight = swipeRight;
        Crown = crown;
        Acceleration = acceleration;
        Gyro = gyro;
        MicrophoneLevel = microphoneLevel;
        MicrophonePeak = microphonePeak;
        TouchPosition = touchPosition;
        Axis0 = axis0;
        NativeQueueAgeSeconds = nativeQueueAgeSeconds;
        var normalized = Quaternion.identity;
        AttitudeTracked = attitudeTracked && attitudeReference != 0
            && float.IsFinite(attitudeAgeSeconds) && attitudeAgeSeconds >= 0f
            && TryNormalizeAttitude(attitude, out normalized);
        this.attitude = normalized;
        AttitudeReference = AttitudeTracked ? attitudeReference : 0;
        MotionSampleInterval = float.IsFinite(motionSampleInterval) && motionSampleInterval >= 0f ? motionSampleInterval : 0f;
        AttitudeAgeSeconds = AttitudeTracked ? attitudeAgeSeconds : 0f;
    }

    /// <summary>Checks native queue age and clamps movement, Crown, and microphone channels.</summary>
    /// <param name="canonical">The clamped sample, or <see cref="Neutral"/> when queue age is invalid.</param>
    /// <returns>True when queue age is finite and nonnegative; false otherwise.</returns>
    /// <remarks>
    /// Movement and Crown use [-1, 1]; microphone meters use [0, 1]. This is not a complete finite-value
    /// validator for manually constructed frames. Binary decoding sanitizes other scalar channels first.
    /// </remarks>
    public bool TryGetCanonical(out AppleWatchInputFrame canonical)
    {
        if (float.IsNaN(NativeQueueAgeSeconds) || float.IsInfinity(NativeQueueAgeSeconds) || NativeQueueAgeSeconds < 0f)
        {
            canonical = Neutral;
            return false;
        }

        canonical = new AppleWatchInputFrame(
            Mathf.Clamp(MoveX, -1f, 1f),
            Mathf.Clamp(MoveY, -1f, 1f),
            PrimaryButton,
            TouchPress,
            Tap,
            DoubleTap,
            PrimaryAction,
            LongPress,
            Drag,
            SwipeUp,
            SwipeDown,
            SwipeLeft,
            SwipeRight,
            Mathf.Clamp(Crown, -1f, 1f),
            Acceleration,
            Gyro,
            Mathf.Clamp01(MicrophoneLevel),
            Mathf.Clamp01(MicrophonePeak),
            TouchPosition,
            Axis0,
            NativeQueueAgeSeconds,
            Attitude,
            AttitudeTracked,
            AttitudeReference,
            MotionSampleInterval,
            AttitudeAgeSeconds);
        return true;
    }

    /// <summary>Clears every button and gesture flag and sets the Crown delta to zero.</summary>
    /// <returns>A copy that keeps continuous channels and attitude metadata.</returns>
    /// <remarks>The receiver uses this copy between native messages so it does not replay old pulses.</remarks>
    public AppleWatchInputFrame WithDiscretePulsesCleared()
    {
        return new AppleWatchInputFrame(
            MoveX,
            MoveY,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            0f,
            Acceleration,
            Gyro,
            MicrophoneLevel,
            MicrophonePeak,
            TouchPosition,
            Axis0,
            NativeQueueAgeSeconds,
            Attitude,
            AttitudeTracked,
            AttitudeReference,
            MotionSampleInterval,
            AttitudeAgeSeconds);
    }

    /// <summary>Removes attitude tracking without clearing other input channels or the motion interval.</summary>
    /// <returns>A copy with identity attitude, zero reference, and zero attitude age.</returns>
    public AppleWatchInputFrame WithAttitudeCleared()
    {
        return new AppleWatchInputFrame(
            MoveX, MoveY, PrimaryButton, TouchPress, Tap, DoubleTap,
            PrimaryAction, LongPress, Drag, SwipeUp, SwipeDown, SwipeLeft, SwipeRight,
            Crown, Acceleration, Gyro, MicrophoneLevel, MicrophonePeak,
            TouchPosition, Axis0, NativeQueueAgeSeconds,
            motionSampleInterval: MotionSampleInterval);
    }

    /// <summary>Decodes and canonicalizes a native binary frame on the supported little-endian Unity platforms.</summary>
    /// <param name="bytes">Buffer containing the frame at offset zero; null is rejected.</param>
    /// <param name="byteCount">Valid bytes in the buffer; must be at least 216 and no greater than its length.</param>
    /// <param name="frame">Decoded input, or <see cref="Neutral"/> on failure.</param>
    /// <returns>True for a sufficiently large buffer with valid native queue age.</returns>
    /// <remarks>
    /// Extra bytes are ignored. Nonfinite scalar channel values become zero. Invalid attitude or attitude age
    /// disables tracking without rejecting the other channels. Invalid native queue age rejects the whole frame.
    /// </remarks>
    public static bool TryRead(byte[] bytes, int byteCount, out AppleWatchInputFrame frame)
    {
        frame = Neutral;

        if (bytes == null || byteCount < MinimumRequiredByteLength)
            return false;

        if (byteCount > bytes.Length)
            return false;

        var moveX = ReadFloat(bytes, MoveXOffset);
        var moveY = ReadFloat(bytes, MoveYOffset);
        var crown = ReadFloat(bytes, CrownOffset);
        var acceleration = new Vector3(
            ReadFloat(bytes, AccelerationXOffset),
            ReadFloat(bytes, AccelerationYOffset),
            ReadFloat(bytes, AccelerationZOffset));
        var gyro = new Vector3(
            ReadFloat(bytes, GyroXOffset),
            ReadFloat(bytes, GyroYOffset),
            ReadFloat(bytes, GyroZOffset));
        var microphoneLevel = ReadFloat(bytes, MicLevelOffset);
        var microphonePeak = ReadFloat(bytes, MicPeakOffset);
        var touchPosition = new Vector2(
            ReadFloat(bytes, TouchPosXOffset),
            ReadFloat(bytes, TouchPosYOffset));
        var axis0 = ReadFloat(bytes, Axis0Offset);
        // Native elapsed time, not a timestamp shared with the Unity or Watch clocks.
        var nativeQueueAgeSeconds = BitConverter.ToSingle(bytes, NativeQueueAgeOffset);

        var primaryButton = ReadFlag(bytes, PrimaryButtonOffset);
        var touchPress = ReadFlag(bytes, TouchPressOffset);
        var tap = ReadFlag(bytes, TapOffset);
        var doubleTap = ReadFlag(bytes, DoubleTapOffset);
        var primaryAction = ReadFlag(bytes, PrimaryActionOffset);
        var longPress = ReadFlag(bytes, LongPressOffset);
        var drag = ReadFlag(bytes, DragOffset);
        var swipeUp = ReadFlag(bytes, SwipeUpOffset);
        var swipeDown = ReadFlag(bytes, SwipeDownOffset);
        var swipeLeft = ReadFlag(bytes, SwipeLeftOffset);
        var swipeRight = ReadFlag(bytes, SwipeRightOffset);

        var candidate = new AppleWatchInputFrame(
            moveX,
            moveY,
            primaryButton,
            touchPress,
            tap,
            doubleTap,
            primaryAction,
            longPress,
            drag,
            swipeUp,
            swipeDown,
            swipeLeft,
            swipeRight,
            crown,
            acceleration,
            gyro,
            microphoneLevel,
            microphonePeak,
            touchPosition,
            axis0,
            nativeQueueAgeSeconds,
            // Do not sanitize components individually: one invalid component
            // invalidates the complete quaternion, rather than inventing a pose.
            new Quaternion(
                BitConverter.ToSingle(bytes, AttitudeOffset),
                BitConverter.ToSingle(bytes, AttitudeOffset + 4),
                BitConverter.ToSingle(bytes, AttitudeOffset + 8),
                BitConverter.ToSingle(bytes, AttitudeOffset + 12)),
            ReadFlag(bytes, AttitudeTrackedOffset),
            BitConverter.ToUInt32(bytes, AttitudeReferenceOffset),
            ReadFloat(bytes, MotionSampleIntervalOffset),
            // Invalid age must invalidate tracking, not be sanitized to a fresh zero age.
            BitConverter.ToSingle(bytes, AttitudeAgeOffset));

        if (!candidate.TryGetCanonical(out var canonical))
            return false;

        frame = canonical;
        return true;
    }

    /// <summary>Writes one zero-padded 216-byte frame in little-endian format.</summary>
    /// <param name="moveX">Horizontal movement; finite values are not clamped.</param>
    /// <param name="moveY">Vertical movement; finite values are not clamped.</param>
    /// <param name="primaryButton">Primary-button flag.</param>
    /// <param name="touchPress">Touch-press flag.</param>
    /// <param name="tap">Tap flag.</param>
    /// <param name="doubleTap">Double-tap flag.</param>
    /// <param name="primaryAction">Shared primary-action flag; not an input-origin marker.</param>
    /// <param name="longPress">Long-press flag.</param>
    /// <param name="drag">Drag-update flag.</param>
    /// <param name="swipeUp">Upward-swipe flag.</param>
    /// <param name="swipeDown">Downward-swipe flag.</param>
    /// <param name="swipeLeft">Leftward-swipe flag.</param>
    /// <param name="swipeRight">Rightward-swipe flag.</param>
    /// <param name="crown">Crown delta for this sample; finite values are not clamped.</param>
    /// <param name="acceleration">Gravity-free acceleration in g.</param>
    /// <param name="gyro">Angular velocity in radians per second.</param>
    /// <param name="microphoneLevel">Average microphone meter, normally in [0, 1].</param>
    /// <param name="microphonePeak">Peak microphone meter, normally in [0, 1].</param>
    /// <param name="touchPosition">Touch position in SwiftUI points in the emitting control's coordinate space.</param>
    /// <param name="axis0">Generic scalar channel.</param>
    /// <param name="destination">Existing buffer that receives the frame.</param>
    /// <param name="destinationOffset">Byte offset at which to write the frame.</param>
    /// <param name="attitude">Raw right-handed quaternion to normalize before encoding.</param>
    /// <param name="attitudeTracked">Whether to attempt to encode a tracked attitude.</param>
    /// <param name="attitudeReference">Nonzero motion-session reference required for tracking.</param>
    /// <param name="motionSampleInterval">Motion sample interval in seconds; negative or nonfinite values become zero.</param>
    /// <param name="attitudeAgeSeconds">Finite nonnegative attitude age in seconds; invalid values disable tracking.</param>
    /// <exception cref="ArgumentOutOfRangeException">The buffer is null, the offset is negative, or the frame does not fit.</exception>
    /// <remarks>Native queue age is zero in this output. Nonfinite scalar values become zero; canonical range clamping occurs when reading.</remarks>
    public static void WriteBinary(
        float moveX,
        float moveY,
        bool primaryButton,
        bool touchPress,
        bool tap,
        bool doubleTap,
        bool primaryAction,
        bool longPress,
        bool drag,
        bool swipeUp,
        bool swipeDown,
        bool swipeLeft,
        bool swipeRight,
        float crown,
        Vector3 acceleration,
        Vector3 gyro,
        float microphoneLevel,
        float microphonePeak,
        Vector2 touchPosition,
        float axis0,
        byte[] destination,
        int destinationOffset = 0,
        Quaternion attitude = default,
        bool attitudeTracked = false,
        uint attitudeReference = 0,
        float motionSampleInterval = 0f,
        float attitudeAgeSeconds = 0f)
    {
        if (destination == null || destinationOffset < 0 || destinationOffset + ByteLength > destination.Length)
            throw new ArgumentOutOfRangeException(nameof(destination));

        Array.Clear(destination, destinationOffset, ByteLength);
        WriteFloat(destination, destinationOffset + MoveXOffset, moveX);
        WriteFloat(destination, destinationOffset + MoveYOffset, moveY);
        WriteFloat(destination, destinationOffset + CrownOffset, crown);
        WriteFloat(destination, destinationOffset + AccelerationXOffset, acceleration.x);
        WriteFloat(destination, destinationOffset + AccelerationYOffset, acceleration.y);
        WriteFloat(destination, destinationOffset + AccelerationZOffset, acceleration.z);
        WriteFloat(destination, destinationOffset + GyroXOffset, gyro.x);
        WriteFloat(destination, destinationOffset + GyroYOffset, gyro.y);
        WriteFloat(destination, destinationOffset + GyroZOffset, gyro.z);
        WriteFloat(destination, destinationOffset + MicLevelOffset, microphoneLevel);
        WriteFloat(destination, destinationOffset + MicPeakOffset, microphonePeak);
        WriteFloat(destination, destinationOffset + TouchPosXOffset, touchPosition.x);
        WriteFloat(destination, destinationOffset + TouchPosYOffset, touchPosition.y);
        WriteFloat(destination, destinationOffset + Axis0Offset, axis0);
        var normalized = Quaternion.identity;
        var tracked = attitudeTracked && attitudeReference != 0
            && float.IsFinite(attitudeAgeSeconds) && attitudeAgeSeconds >= 0f
            && TryNormalizeAttitude(attitude, out normalized);
        WriteFloat(destination, destinationOffset + AttitudeOffset, normalized.x);
        WriteFloat(destination, destinationOffset + AttitudeOffset + 4, normalized.y);
        WriteFloat(destination, destinationOffset + AttitudeOffset + 8, normalized.z);
        WriteFloat(destination, destinationOffset + AttitudeOffset + 12, normalized.w);
        destination[destinationOffset + AttitudeTrackedOffset] = tracked ? (byte)1 : (byte)0;
        var reference = tracked ? attitudeReference : 0u;
        for (var index = 0; index < 4; index++)
            destination[destinationOffset + AttitudeReferenceOffset + index] = (byte)(reference >> (index * 8));
        WriteFloat(destination, destinationOffset + MotionSampleIntervalOffset, Mathf.Max(0f, motionSampleInterval));
        WriteFloat(destination, destinationOffset + AttitudeAgeOffset, tracked ? attitudeAgeSeconds : 0f);
        destination[destinationOffset + PrimaryButtonOffset] = primaryButton ? (byte)1 : (byte)0;
        destination[destinationOffset + TouchPressOffset] = touchPress ? (byte)1 : (byte)0;
        destination[destinationOffset + TapOffset] = tap ? (byte)1 : (byte)0;
        destination[destinationOffset + DoubleTapOffset] = doubleTap ? (byte)1 : (byte)0;
        destination[destinationOffset + PrimaryActionOffset] = primaryAction ? (byte)1 : (byte)0;
        destination[destinationOffset + LongPressOffset] = longPress ? (byte)1 : (byte)0;
        destination[destinationOffset + DragOffset] = drag ? (byte)1 : (byte)0;
        destination[destinationOffset + SwipeUpOffset] = swipeUp ? (byte)1 : (byte)0;
        destination[destinationOffset + SwipeDownOffset] = swipeDown ? (byte)1 : (byte)0;
        destination[destinationOffset + SwipeLeftOffset] = swipeLeft ? (byte)1 : (byte)0;
        destination[destinationOffset + SwipeRightOffset] = swipeRight ? (byte)1 : (byte)0;
    }

    private static float ReadFloat(byte[] bytes, int offset)
    {
        var value = BitConverter.ToSingle(bytes, offset);
        return (float.IsNaN(value) || float.IsInfinity(value)) ? 0f : value;
    }

    /// <summary>Normalizes a finite, nonzero attitude quaternion without float overflow or underflow.</summary>
    /// <param name="value">The attitude to validate and normalize.</param>
    /// <param name="normalized">The unit attitude on success; identity on failure.</param>
    /// <returns>True for a finite, nonzero quaternion; otherwise false.</returns>
    public static bool TryNormalizeAttitude(Quaternion value, out Quaternion normalized)
    {
        normalized = Quaternion.identity;
        if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || !float.IsFinite(value.w))
            return false;

        // Double precision avoids overflow and underflow for any finite float input.
        var length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y
            + (double)value.z * value.z + (double)value.w * value.w);
        if (length == 0d)
            return false;

        normalized = new Quaternion((float)(value.x / length), (float)(value.y / length),
            (float)(value.z / length), (float)(value.w / length));
        return true;
    }

    private static bool ReadFlag(byte[] bytes, int offset)
    {
        if (offset < 0 || offset >= bytes.Length)
            return false;

        return bytes[offset] > 0;
    }

    private static void WriteFloat(byte[] destination, int offset, float value)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        var sanitized = (float.IsNaN(value) || float.IsInfinity(value)) ? 0f : value;
        var bytes = BitConverter.GetBytes(sanitized);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        Buffer.BlockCopy(bytes, 0, destination, offset, 4);
    }
}
