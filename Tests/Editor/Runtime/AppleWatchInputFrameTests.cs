using NUnit.Framework;
using UnityEngine;

public sealed class AppleWatchInputFrameTests
{
    [Test]
    public void RawAttitudeWireDecodePreservesHandednessAndPulseClearPreservesMotion()
    {
        var bytes = MotionBytes(0, 0, -3, 4, 0xf1234567u);
        bytes[10] = 1;
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<Quaternion>(frame, "Attitude"), Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Assert.That(Motion<bool>(frame, "AttitudeTracked"), Is.True);
        Assert.That(Motion<uint>(frame, "AttitudeReference"), Is.EqualTo(0xf1234567u));
        Assert.That(Motion<float>(frame, "MotionSampleInterval"), Is.EqualTo(0.05f));
        var continuous = frame.WithDiscretePulsesCleared();
        Assert.That(continuous.Tap, Is.False);
        Assert.That(Motion<Quaternion>(continuous, "Attitude"), Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Assert.That(Motion<uint>(continuous, "AttitudeReference"), Is.EqualTo(0xf1234567u));
        Assert.That(Motion<float>(continuous, "MotionSampleInterval"), Is.EqualTo(0.05f));
    }

    [TestCase(0f, 0f, 0f, 0f)]
    [TestCase(float.NaN, 0f, 0f, 1f)]
    [TestCase(0f, float.PositiveInfinity, 0f, 1f)]
    public void InvalidAttitudeIsAtomicallyNeutralized(float x, float y, float z, float w)
    {
        var bytes = MotionBytes(x, y, z, w, 7);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<Quaternion>(frame, "Attitude"), Is.EqualTo(Quaternion.identity));
        Assert.That(Motion<bool>(frame, "AttitudeTracked"), Is.False);
        Assert.That(Motion<uint>(frame, "AttitudeReference"), Is.Zero);
    }

    [Test]
    public void HugeFiniteAttitudeNormalizesWithoutOverflow()
    {
        var bytes = MotionBytes(float.MaxValue, -float.MaxValue, float.MaxValue, -float.MaxValue, 1);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<Quaternion>(frame, "Attitude"), Is.EqualTo(new Quaternion(0.5f, -0.5f, 0.5f, -0.5f)));
        Assert.That(Motion<bool>(frame, "AttitudeTracked"), Is.True);
    }

    [TestCase(false, 7u)]
    [TestCase(true, 0u)]
    public void TrackingRequiresBothFlagAndNonzeroReference(bool tracked, uint reference)
    {
        var bytes = MotionBytes(0, 0, 1, 0, reference);
        bytes[88] = tracked ? (byte)1 : (byte)0;
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<Quaternion>(frame, "Attitude"), Is.EqualTo(Quaternion.identity));
        Assert.That(Motion<bool>(frame, "AttitudeTracked"), Is.False);
        Assert.That(Motion<uint>(frame, "AttitudeReference"), Is.Zero);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void InvalidMotionIntervalIsFiniteNeutral(float interval)
    {
        var bytes = MotionBytes(0, 0, 0, 1, 7);
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(interval), 0, bytes, 96, 4);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<float>(frame, "MotionSampleInterval"), Is.Zero);
    }

    [Test]
    public void AttitudeSampleAgeSurvivesDecodeCanonicalizationAndPulseClear()
    {
        var bytes = MotionBytes(0, 0, -3, 4, 7, attitudeAge: 0.2f);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(Motion<float>(frame, "AttitudeAgeSeconds"), Is.EqualTo(0.2f));
        Assert.That(frame.TryGetCanonical(out var canonical), Is.True);
        Assert.That(Motion<float>(canonical.WithDiscretePulsesCleared(), "AttitudeAgeSeconds"), Is.EqualTo(0.2f));
        Assert.That(canonical.AttitudeTracked, Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void InvalidAttitudeAgeCannotTurnAnOldPoseIntoFreshTracking(float age)
    {
        var bytes = MotionBytes(0, 0, 1, 0, 7, attitudeAge: age);
        bytes[8] = 1;
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(frame.AttitudeTracked, Is.False);
        Assert.That(frame.Attitude, Is.EqualTo(Quaternion.identity));
        Assert.That(frame.PrimaryButton, Is.True, "Malformed attitude metadata must not discard unrelated valid input.");
        Assert.That(Motion<float>(frame, "AttitudeAgeSeconds"), Is.Zero);
    }

    [Test]
    public void DefaultAndLegacyFramesHaveIdentityWithoutFabricatedTracking()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var legacy), Is.True);
        foreach (var frame in new[] { default(AppleWatchInputFrame), AppleWatchInputFrame.Neutral, legacy })
        {
            Assert.That(Motion<Quaternion>(frame, "Attitude"), Is.EqualTo(Quaternion.identity));
            Assert.That(Motion<bool>(frame, "AttitudeTracked"), Is.False);
            Assert.That(Motion<uint>(frame, "AttitudeReference"), Is.Zero);
        }
    }

    private static T Motion<T>(AppleWatchInputFrame frame, string name)
    {
        var member = typeof(AppleWatchInputFrame).GetProperty(name);
        if (member != null) return (T)member.GetValue(frame);
        var field = typeof(AppleWatchInputFrame).GetField(name);
        Assert.That(field, Is.Not.Null, $"Frame must expose {name}.");
        return (T)field.GetValue(frame);
    }

    private static byte[] MotionBytes(float x, float y, float z, float w, uint reference, float attitudeAge = 0f)
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        var values = new[] { x, y, z, w };
        for (var index = 0; index < values.Length; index++)
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(values[index]), 0, bytes, 72 + index * 4, 4);
        bytes[88] = 1;
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(reference), 0, bytes, 92, 4);
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(0.05f), 0, bytes, 96, 4);
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(attitudeAge), 0, bytes, 100, 4);
        return bytes;
    }

    [Test]
    public void BinaryRoundTripPreservesEveryExposedControl()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];

        AppleWatchInputFrame.WriteBinary(
            0.25f,
            -0.75f,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            -0.4f,
            new Vector3(1f, 2f, 3f),
            new Vector3(4f, 5f, 6f),
            0.35f,
            0.8f,
            new Vector2(-0.2f, 0.6f),
            0.45f,
            bytes);

        Assert.That(System.BitConverter.ToSingle(bytes, 84), Is.EqualTo(1f), "Legacy writers must encode an untracked identity attitude.");

        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(frame.MoveX, Is.EqualTo(0.25f));
        Assert.That(frame.MoveY, Is.EqualTo(-0.75f));
        Assert.That(frame.PrimaryButton, Is.True);
        Assert.That(frame.TouchPress, Is.True);
        Assert.That(frame.Tap, Is.True);
        Assert.That(frame.DoubleTap, Is.True);
        Assert.That(frame.PrimaryAction, Is.True);
        Assert.That(frame.LongPress, Is.True);
        Assert.That(frame.Drag, Is.True);
        Assert.That(frame.SwipeUp, Is.True);
        Assert.That(frame.SwipeDown, Is.True);
        Assert.That(frame.SwipeLeft, Is.True);
        Assert.That(frame.SwipeRight, Is.True);
        Assert.That(frame.Crown, Is.EqualTo(-0.4f));
        Assert.That(frame.Acceleration, Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(frame.Gyro, Is.EqualTo(new Vector3(4f, 5f, 6f)));
        Assert.That(frame.MicrophoneLevel, Is.EqualTo(0.35f));
        Assert.That(frame.MicrophonePeak, Is.EqualTo(0.8f));
        Assert.That(frame.TouchPosition, Is.EqualTo(new Vector2(-0.2f, 0.6f)));
        Assert.That(frame.Axis0, Is.EqualTo(0.45f));
    }

    [Test]
    public void BinaryWriterPreservesTrackedRawAttitudeAtCompatibleOffsets()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        AppleWatchInputFrame.WriteBinary(0.25f, -0.5f, false, false, false, false, false,
            false, false, false, false, false, false, 0f, Vector3.zero, new Vector3(1, 2, -3),
            0f, 0f, Vector2.zero, 0f, bytes,
            attitude: new Quaternion(0, 0, -3, 4), attitudeTracked: true,
            attitudeReference: 0xf1234567u, motionSampleInterval: 0.05f, attitudeAgeSeconds: 0.12f);
        Assert.That(bytes.Length, Is.EqualTo(216));
        Assert.That(System.BitConverter.ToSingle(bytes, 44), Is.EqualTo(-3f));
        Assert.That(System.BitConverter.ToSingle(bytes, 80), Is.EqualTo(-0.6f));
        Assert.That(System.BitConverter.ToSingle(bytes, 84), Is.EqualTo(0.8f));
        Assert.That(bytes[88], Is.EqualTo(1));
        Assert.That(new[] { bytes[92], bytes[93], bytes[94], bytes[95] }, Is.EqualTo(new byte[] { 0x67, 0x45, 0x23, 0xf1 }));
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Assert.That(frame.Attitude, Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Assert.That(frame.AttitudeTracked, Is.True);
        Assert.That(frame.AttitudeReference, Is.EqualTo(0xf1234567u));
        Assert.That(frame.MotionSampleInterval, Is.EqualTo(0.05f));
        Assert.That(frame.AttitudeAgeSeconds, Is.EqualTo(0.12f));
    }

    [Test]
    public void DecodeRejectsTruncatedFrame()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength - 1];

        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.False);
        Assert.That(frame.MoveX, Is.Zero);
        Assert.That(frame.PrimaryButton, Is.False);
    }

    [Test]
    public void PulseClearPreservesContinuousValuesAndNeutralizesDiscreteControls()
    {
        var frame = new AppleWatchInputFrame(
            0.5f,
            -0.25f,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            0.3f,
            Vector3.one,
            Vector3.forward,
            0.4f,
            0.9f,
            new Vector2(0.2f, -0.8f),
            -0.7f);

        var continuous = frame.WithDiscretePulsesCleared();

        Assert.That(continuous.MoveX, Is.EqualTo(frame.MoveX));
        Assert.That(continuous.MoveY, Is.EqualTo(frame.MoveY));
        Assert.That(continuous.Crown, Is.Zero, "Crown is a delta and must not repeat while waiting for the next frame.");
        Assert.That(continuous.Acceleration, Is.EqualTo(frame.Acceleration));
        Assert.That(continuous.Gyro, Is.EqualTo(frame.Gyro));
        Assert.That(continuous.MicrophoneLevel, Is.EqualTo(frame.MicrophoneLevel));
        Assert.That(continuous.MicrophonePeak, Is.EqualTo(frame.MicrophonePeak));
        Assert.That(continuous.TouchPosition, Is.EqualTo(frame.TouchPosition));
        Assert.That(continuous.Axis0, Is.EqualTo(frame.Axis0));
        Assert.That(continuous.PrimaryButton, Is.False);
        Assert.That(continuous.TouchPress, Is.False);
        Assert.That(continuous.Tap, Is.False);
        Assert.That(continuous.DoubleTap, Is.False);
        Assert.That(continuous.PrimaryAction, Is.False);
        Assert.That(continuous.LongPress, Is.False);
        Assert.That(continuous.Drag, Is.False);
        Assert.That(continuous.SwipeUp, Is.False);
        Assert.That(continuous.SwipeDown, Is.False);
        Assert.That(continuous.SwipeLeft, Is.False);
        Assert.That(continuous.SwipeRight, Is.False);
    }

    [TestCase(-0.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void DecodeRejectsInvalidNativeQueueAge(float age)
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        var encodedAge = System.BitConverter.GetBytes(age);
        if (!System.BitConverter.IsLittleEndian)
            System.Array.Reverse(encodedAge);
        System.Buffer.BlockCopy(encodedAge, 0, bytes, 68, encodedAge.Length);

        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out _), Is.False);
    }
}
