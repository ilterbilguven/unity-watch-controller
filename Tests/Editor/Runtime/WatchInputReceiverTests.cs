using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class WatchInputReceiverTests : InputTestFixture
{
    private GameObject gameObject;
    private WatchInputReceiver receiver;
    private InputAction buttonAction;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        InputSystem.RegisterLayout<AppleWatchControllerDevice>();
        gameObject = new GameObject("Watch input receiver test");
        receiver = gameObject.AddComponent<WatchInputReceiver>();
        buttonAction = new InputAction("Watch test button", InputActionType.Button);
        buttonAction.AddBinding(AppleWatchControllerDevice.Current.primaryButton);
        buttonAction.Enable();
    }

    [TearDown]
    public override void TearDown()
    {
        buttonAction.Dispose();
        UnityEngine.Object.DestroyImmediate(gameObject);
        foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
            InputSystem.RemoveDevice(device);
        base.TearDown();
    }

    [Test]
    public void ConsecutiveNativePulsesEachPerformTheButtonAction()
    {
        var pressCount = 0;
        buttonAction.performed += _ => pressCount++;

        Consume(true, Frame(primaryButton: true), 10);
        Consume(true, Frame(primaryButton: true), 10.01);

        Assert.That(pressCount, Is.EqualTo(2));
    }

    [Test]
    public void EditorHeldButtonDoesNotBecomeRepeatedPulses()
    {
        var pressCount = 0;
        buttonAction.performed += _ => pressCount++;

        Consume(true, Frame(primaryButton: true), 10, isNativeFrame: false);
        Consume(true, Frame(primaryButton: true), 10.01, isNativeFrame: false);

        Assert.That(pressCount, Is.EqualTo(1));
    }

    [Test]
    public void CrownDeltaIsClearedOnTheNextUpdateWithoutAFrame()
    {
        Consume(true, Frame(crown: 0.4f), 10);
        Assert.That(AppleWatchControllerDevice.Current.crown.ReadValue(), Is.EqualTo(0.4f));

        Consume(false, AppleWatchInputFrame.Neutral, 10.01);

        Assert.That(AppleWatchControllerDevice.Current.crown.ReadValue(), Is.Zero);
    }

    [Test]
    public void ExpiredNativeBacklogCannotTriggerGameplayInput()
    {
        var pressCount = 0;
        buttonAction.performed += _ => pressCount++;

        Consume(true, Frame(moveX: 1f, primaryButton: true, queueAge: 0.3f), 10);

        Assert.That(pressCount, Is.Zero);
        Assert.That(AppleWatchControllerDevice.Current.primaryStick.ReadValue(), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void RetainedStateExpiresFromNativeArrivalInsteadOfDequeue()
    {
        Consume(true, Frame(moveX: 1f, queueAge: 0.2f), 10);
        Assert.That(AppleWatchControllerDevice.Current.primaryStick.ReadValue().x, Is.EqualTo(1f));

        Consume(false, AppleWatchInputFrame.Neutral, 10.06);

        Assert.That(AppleWatchControllerDevice.Current.primaryStick.ReadValue(), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void NativeCadenceCountsOnlyAcceptedDequeuesNotCachedOrEditorFrames()
    {
        for (var index = 0; index < 20; index++)
        {
            Consume(true, Frame(queueAge: 0.02f), 10 + index * 0.05);
            Consume(false, AppleWatchInputFrame.Neutral, 10.01 + index * 0.05);
            Consume(true, Frame(), 10.02 + index * 0.05, isNativeFrame: false);
        }
        Consume(false, AppleWatchInputFrame.Neutral, 11);
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.EqualTo(20));
        Assert.That(Diagnostic<float>("NativeFramesPerSecond"), Is.EqualTo(20).Within(0.001f));
        Assert.That(Diagnostic<float>("LastNativeQueueAgeSeconds"), Is.EqualTo(0.02f));
        Consume(true, Frame(queueAge: 0.3f), 11.1);
        Consume(false, AppleWatchInputFrame.Neutral, 12);
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.EqualTo(20), "Rejected native backlog is not fresh input.");
        Assert.That(Diagnostic<float>("NativeFramesPerSecond"), Is.Zero);
    }

    [Test]
    public void NativeDiagnosticsResetOnDisableAndNeverClaimEditorTraffic()
    {
        Consume(true, Frame(), 10, isNativeFrame: false);
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.Zero);
        Assert.That(Diagnostic<double>("SecondsSinceNativeFrame"), Is.EqualTo(double.PositiveInfinity));
        Consume(true, Frame(queueAge: 0.1f), 10.1);
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.EqualTo(1));
        typeof(WatchInputReceiver).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, null);
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.Zero);
        Assert.That(Diagnostic<float>("NativeFramesPerSecond"), Is.Zero);
        Assert.That(Diagnostic<float>("LastNativeQueueAgeSeconds"), Is.Zero);
        Assert.That(Diagnostic<double>("SecondsSinceNativeFrame"), Is.EqualTo(double.PositiveInfinity));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void MalformedNativeQueueAgeIsNotCountedOrSubmitted(float age)
    {
        var frame = new AppleWatchInputFrame(1, 0, true, false, false, false, false,
            false, false, false, false, false, false, 0, Vector3.zero, Vector3.zero,
            0, 0, Vector2.zero, 0, age);
        Consume(true, frame, 10);
        Assert.That(receiver.NativeFramesReceived, Is.Zero);
        Assert.That(AppleWatchControllerDevice.Current.primaryStick.ReadValue(), Is.EqualTo(Vector2.zero));
        Assert.That(AppleWatchControllerDevice.Current.primaryButton.isPressed, Is.False);
    }

    private T Diagnostic<T>(string name)
    {
        var property = typeof(WatchInputReceiver).GetProperty(name);
        Assert.That(property, Is.Not.Null, $"Receiver must expose {name}.");
        return (T)property.GetValue(receiver);
    }

    [Test]
    public void RetainedAttitudeExpiresToIdentityAndClearsSampleInterval()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        WriteFloat(bytes, 80, -0.6f);
        WriteFloat(bytes, 84, 0.8f);
        bytes[88] = 1;
        bytes[92] = 7;
        WriteFloat(bytes, 96, 0.05f);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        Consume(true, frame, 10);
        var attitude = AppleWatchControllerDevice.Current.TryGetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("attitude");
        Assert.That(attitude, Is.Not.Null);
        Assert.That(attitude.ReadValue(), Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Assert.That(Diagnostic<float>("MotionSampleInterval"), Is.EqualTo(0.05f));
        Consume(false, AppleWatchInputFrame.Neutral, 10.1);
        Assert.That(attitude.ReadValue(), Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Consume(false, AppleWatchInputFrame.Neutral, 10.3);
        Assert.That(attitude.ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(Diagnostic<float>("MotionSampleInterval"), Is.Zero);
    }

    [TestCase(0.25f)]
    [TestCase(0.5f)]
    public void LateUnrelatedFrameDoesNotExtendAttitudeDeadline(float timeout)
    {
        typeof(WatchInputReceiver).GetField("staleTimeoutSeconds", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(receiver, timeout);
        Consume(true, AttitudeFrame(), 10);
        var lateTime = timeout - 0.01f;
        Consume(true, AttitudeFrame(age: lateTime, microphoneLevel: 0.7f), 10 + lateTime);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.True, "Pose is still valid just before its own deadline.");
        Consume(false, AppleWatchInputFrame.Neutral, 10 + timeout + 0.01);
        var device = AppleWatchControllerDevice.Current;
        Assert.That(device.attitudeTracked.isPressed, Is.False, "Unrelated traffic must not extend the original attitude sample's lifetime.");
        Assert.That(device.attitude.ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(device.microphoneLevel.ReadValue(), Is.EqualTo(0.7f), "The newer microphone value must retain its own valid lifetime.");
    }

    [Test]
    public void MultipleUnrelatedPacketsCannotRenewAttitudeWhileUnityCachesThem()
    {
        Consume(true, AttitudeFrame(), 10);
        foreach (var age in new[] { 0.08f, 0.16f, 0.24f })
            Consume(true, AttitudeFrame(age: age, microphoneLevel: 0.7f), 10 + age);
        Consume(false, AppleWatchInputFrame.Neutral, 10.3);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.False);
        Assert.That(AppleWatchControllerDevice.Current.microphoneLevel.ReadValue(), Is.EqualTo(0.7f));
        Assert.That(receiver.NativeFramesReceived, Is.EqualTo(4), "All fresh packets still count as native dequeues.");
    }

    [Test]
    public void AttitudeAgeIncludesQueueDelayExactlyOnce()
    {
        Consume(true, AttitudeFrame(age: 0.2f, queueAge: 0.1f, microphoneLevel: 0.7f), 10);
        Consume(false, AppleWatchInputFrame.Neutral, 10.025);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.True,
            "Subtracting queue age again would expire the pose too early.");
        Consume(false, AppleWatchInputFrame.Neutral, 10.06);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.False,
            "The pose expires after only its remaining 50 ms, not a new full timeout.");
        Assert.That(AppleWatchControllerDevice.Current.microphoneLevel.ReadValue(), Is.EqualTo(0.7f));
    }

    [Test]
    public void ExplicitRepeatedEqualAttitudeStartsANewSampleLifetime()
    {
        Consume(true, AttitudeFrame(), 10);
        Consume(true, AttitudeFrame(), 10.2);
        Consume(false, AppleWatchInputFrame.Neutral, 10.3);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.True);
        Consume(false, AppleWatchInputFrame.Neutral, 10.46);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void InvalidPoseAgeNeutralizesOnlyAttitude(float age)
    {
        Consume(true, AttitudeFrame(age: age, microphoneLevel: 0.7f), 10);
        Assert.That(AppleWatchControllerDevice.Current.attitudeTracked.isPressed, Is.False);
        Assert.That(AppleWatchControllerDevice.Current.microphoneLevel.ReadValue(), Is.EqualTo(0.7f));
    }

    private static AppleWatchInputFrame AttitudeFrame(float age = 0f, float queueAge = 0f, float microphoneLevel = 0f)
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        WriteFloat(bytes, 48, microphoneLevel);
        WriteFloat(bytes, 68, queueAge);
        WriteFloat(bytes, 80, -0.6f);
        WriteFloat(bytes, 84, 0.8f);
        bytes[88] = 1;
        bytes[92] = 7;
        WriteFloat(bytes, 96, 0.05f);
        WriteFloat(bytes, 100, age);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        return frame;
    }

    [Test]
    public void ExplicitEditorPoseReachesDeviceWithoutClaimingNativeTraffic()
    {
        var enabledField = typeof(WatchInputReceiver).GetField("editorAttitudeEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(enabledField, Is.Not.Null, "Pose simulation must be an explicit Inspector opt-in.");
        enabledField.SetValue(receiver, true);
        typeof(WatchInputReceiver).GetField("editorAttitudeEuler", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(receiver, new Vector3(0, 90, 0));
        Consume(true, ReadEditorFrame(), 10, isNativeFrame: false);
        var device = AppleWatchControllerDevice.Current;
        Assert.That(device.GetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("attitude").ReadValue(),
            Is.EqualTo(Quaternion.Euler(0, 90, 0)));
        Assert.That(Diagnostic<long>("NativeFramesReceived"), Is.Zero);
        enabledField.SetValue(receiver, false);
        Consume(true, ReadEditorFrame(), 10.1, isNativeFrame: false);
        Assert.That(device.GetChildControl<UnityEngine.InputSystem.Controls.QuaternionControl>("attitude").ReadValue(), Is.EqualTo(Quaternion.identity));
    }

    [TestCase(Key.Digit0)]
    [TestCase(Key.Digit1)]
    [TestCase(Key.Digit6)]
    [TestCase(Key.W)]
    public void ConfiguredEditorGyroKeyProducesMotionAndReleaseNeutralizesIt(Key key)
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        typeof(WatchInputReceiver).GetField("editorGyroUpKey", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(receiver, key);
        typeof(WatchInputReceiver).GetMethod("ConfigureEditorActions", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, null);
        try
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            Assert.That(ReadEditorFrame().Gyro, Is.EqualTo(Vector3.up), "The configured physical key must reach the Watch simulation frame.");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(ReadEditorFrame().Gyro, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            typeof(WatchInputReceiver).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(receiver, null);
            typeof(WatchInputReceiver).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(receiver, null);
        }
    }

    [TestCase(Key.E, 15f)]
    [TestCase(Key.Q, -15f)]
    public void EditorCrownScalePreservesRawDeltaBeyondNormalizedAxisRange(Key key, float expectedDelta)
    {
        WithEditorCrownScale(15f, keyboard =>
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            InputSystem.Update();
            var frame = ReadEditorFrame();
            Assert.That(frame.Crown, Is.EqualTo(expectedDelta));
            Consume(true, frame, 10, isNativeFrame: false);
            Assert.That(AppleWatchControllerDevice.Current.crown.ReadValue(), Is.EqualTo(expectedDelta));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(ReadEditorFrame().Crown, Is.Zero);
        });
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void NonFiniteEditorCrownScaleCannotSendInvalidDelta(float scale)
    {
        WithEditorCrownScale(scale, keyboard =>
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.Update();
            Assert.That(ReadEditorFrame().Crown, Is.Zero);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(ReadEditorFrame().Crown, Is.Zero);
        });
    }

    private void WithEditorCrownScale(float scale, Action<Keyboard> assertion)
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        typeof(WatchInputReceiver).GetField("editorCrownScale", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(receiver, scale);
        typeof(WatchInputReceiver).GetMethod("ConfigureEditorActions", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, null);
        try
        {
            assertion(keyboard);
        }
        finally
        {
            typeof(WatchInputReceiver).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(receiver, null);
            typeof(WatchInputReceiver).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(receiver, null);
        }
    }

    private AppleWatchInputFrame ReadEditorFrame()
    {
        var arguments = new object[] { AppleWatchInputFrame.Neutral };
        var read = (bool)typeof(WatchInputReceiver).GetMethod("TryReadEditorFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, arguments);
        Assert.That(read, Is.True);
        return (AppleWatchInputFrame)arguments[0];
    }

    private void Consume(bool hasFrame, AppleWatchInputFrame frame, double realtime, bool isNativeFrame = true)
    {
        typeof(WatchInputReceiver).GetMethod("ProcessFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, new object[] { hasFrame, frame, realtime, isNativeFrame });
        InputSystem.Update();
    }

    private static AppleWatchInputFrame Frame(float moveX = 0f, bool primaryButton = false, float crown = 0f, float queueAge = 0f)
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        WriteFloat(bytes, 0, moveX);
        bytes[8] = primaryButton ? (byte)1 : (byte)0;
        WriteFloat(bytes, 20, crown);
        WriteFloat(bytes, 68, queueAge);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        return frame;
    }

    private static void WriteFloat(byte[] destination, int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        Buffer.BlockCopy(bytes, 0, destination, offset, bytes.Length);
    }
}
