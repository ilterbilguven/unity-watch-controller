using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class AppleWatchControllerDeviceTests : InputTestFixture
{
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        InputSystem.RegisterLayout<AppleWatchControllerDevice>();
    }

    [TearDown]
    public override void TearDown()
    {
        foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
        {
            InputSystem.RemoveDevice(device);
        }
        base.TearDown();
    }

    [Test]
    public void SubmitStateFeedsTheCustomInputSystemDevice()
    {
        AppleWatchControllerDevice.SubmitState(
            0.6f,
            -0.2f,
            true,
            touchPress: true,
            tap: true,
            doubleTap: true,
            primaryAction: true,
            longPress: true,
            drag: true,
            swipeUp: true,
            swipeDown: true,
            swipeLeft: true,
            swipeRight: true,
            crown: -0.5f,
            acceleration: new Vector3(1f, 2f, 3f),
            gyro: new Vector3(4f, 5f, 6f),
            microphoneLevel: 0.25f,
            microphonePeak: 0.75f,
            touchPosition: new Vector2(-0.3f, 0.4f),
            axis0: 0.9f);
        InputSystem.Update();

        var device = AppleWatchControllerDevice.Current;
        Assert.That(device.primaryStick.ReadValue(), Is.EqualTo(new Vector2(0.6f, -0.2f)));
        Assert.That(device.primaryButton.isPressed, Is.True);
        Assert.That(device.touchPress.isPressed, Is.True);
        Assert.That(device.tap.isPressed, Is.True);
        Assert.That(device.doubleTap.isPressed, Is.True);
        Assert.That(device.primaryAction.isPressed, Is.True);
        Assert.That(device.longPress.isPressed, Is.True);
        Assert.That(device.drag.isPressed, Is.True);
        Assert.That(device.swipeUp.isPressed, Is.True);
        Assert.That(device.swipeDown.isPressed, Is.True);
        Assert.That(device.swipeLeft.isPressed, Is.True);
        Assert.That(device.swipeRight.isPressed, Is.True);
        Assert.That(device.crown.ReadValue(), Is.EqualTo(-0.5f));
        Assert.That(device.acceleration.ReadValue(), Is.EqualTo(new Vector3(1f, 2f, 3f)));
        Assert.That(device.gyro.ReadValue(), Is.EqualTo(new Vector3(4f, 5f, 6f)));
        Assert.That(device.microphoneLevel.ReadValue(), Is.EqualTo(0.25f));
        Assert.That(device.microphonePeak.ReadValue(), Is.EqualTo(0.75f));
        Assert.That(device.touchPosition.ReadValue(), Is.EqualTo(new Vector2(-0.3f, 0.4f)));
        Assert.That(device.axis0.ReadValue(), Is.EqualTo(0.9f));
    }

    [Test]
    public void AttitudeControlStartsAtIdentityBeforeFirstStateEvent()
    {
        var device = AppleWatchControllerDevice.Current;
        var attitude = device.TryGetChildControl<QuaternionControl>("attitude");
        Assert.That(attitude, Is.Not.Null, "Published device must expose raw attitude.");
        Assert.That(attitude.ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(device.GetChildControl<ButtonControl>("attitudeTracked").isPressed, Is.False);
        Assert.That(device.GetChildControl<IntegerControl>("attitudeReference").ReadValue(), Is.Zero);
    }

    [Test]
    public void AttitudeInputActionReadsRawQuaternionAndResetClearsTracking()
    {
        var bytes = new byte[AppleWatchInputFrame.ByteLength];
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(-0.6f), 0, bytes, 80, 4);
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(0.8f), 0, bytes, 84, 4);
        bytes[88] = 1;
        System.Buffer.BlockCopy(System.BitConverter.GetBytes(0xf1234567u), 0, bytes, 92, 4);
        Assert.That(AppleWatchInputFrame.TryRead(bytes, bytes.Length, out var frame), Is.True);
        var device = AppleWatchControllerDevice.Current;
        using var action = new InputAction("Watch raw attitude", InputActionType.Value,
            "<AppleWatchController>/attitude");
        action.Enable();
        typeof(WatchInputReceiver).GetMethod("SubmitFrame", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { frame });
        InputSystem.Update();
        Assert.That(action.controls.Count, Is.EqualTo(1), "Quaternion must be bindable from the public layout.");
        Assert.That(action.ReadValue<Quaternion>(), Is.EqualTo(new Quaternion(0, 0, -0.6f, 0.8f)));
        Assert.That(device.GetChildControl<ButtonControl>("attitudeTracked").isPressed, Is.True);
        Assert.That(unchecked((uint)device.GetChildControl<IntegerControl>("attitudeReference").ReadValue()), Is.EqualTo(0xf1234567u));
        AppleWatchControllerDevice.ResetState();
        InputSystem.Update();
        Assert.That(device.GetChildControl<QuaternionControl>("attitude").ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(device.GetChildControl<ButtonControl>("attitudeTracked").isPressed, Is.False);
    }

    [TestCase(0f, 0f, 0f, 0f)]
    [TestCase(float.NaN, 0f, 0f, 1f)]
    [TestCase(0f, 0f, float.PositiveInfinity, 1f)]
    public void DirectSubmissionCannotMarkInvalidAttitudeAsTracked(float x, float y, float z, float w)
    {
        AppleWatchControllerDevice.SubmitState(0, 0, false,
            attitude: new Quaternion(x, y, z, w), attitudeTracked: true, attitudeReference: 7);
        InputSystem.Update();
        var device = AppleWatchControllerDevice.Current;
        Assert.That(device.attitude.ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(device.attitudeTracked.isPressed, Is.False);
        Assert.That(device.attitudeReference.ReadValue(), Is.Zero);
    }

    [Test]
    public void TrackedIdentityRemainsReadableThroughActionResolvedControl()
    {
        var device = AppleWatchControllerDevice.Current;
        using var action = new InputAction("Tracked Watch identity", InputActionType.Value, "<AppleWatchController>/attitude");
        action.Enable();
        AppleWatchControllerDevice.SubmitState(0, 0, false,
            attitude: Quaternion.identity, attitudeTracked: true, attitudeReference: 7);
        InputSystem.Update();
        Assert.That(action.controls.Count, Is.EqualTo(1));
        Assert.That(((QuaternionControl)action.controls[0]).ReadValue(), Is.EqualTo(Quaternion.identity));
        Assert.That(device.attitudeTracked.isPressed, Is.True);
    }

    [Test]
    public void ResetStateNeutralizesEveryControl()
    {
        AppleWatchControllerDevice.SubmitState(
            1f,
            1f,
            true,
            crown: 1f,
            acceleration: Vector3.one,
            gyro: Vector3.one,
            microphoneLevel: 1f,
            microphonePeak: 1f,
            touchPosition: Vector2.one,
            axis0: 1f);
        InputSystem.Update();

        AppleWatchControllerDevice.ResetState();
        InputSystem.Update();

        var device = AppleWatchControllerDevice.Current;
        Assert.That(device.primaryStick.ReadValue(), Is.EqualTo(Vector2.zero));
        Assert.That(device.primaryButton.isPressed, Is.False);
        Assert.That(device.crown.ReadValue(), Is.Zero);
        Assert.That(device.acceleration.ReadValue(), Is.EqualTo(Vector3.zero));
        Assert.That(device.gyro.ReadValue(), Is.EqualTo(Vector3.zero));
        Assert.That(device.microphoneLevel.ReadValue(), Is.Zero);
        Assert.That(device.microphonePeak.ReadValue(), Is.Zero);
        Assert.That(device.touchPosition.ReadValue(), Is.EqualTo(Vector2.zero));
        Assert.That(device.axis0.ReadValue(), Is.Zero);
    }

    [Test]
    public void PublishedControllerLayoutBindingReceivesInput()
    {
        typeof(AppleWatchControllerDevice).GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        using var action = new InputAction("Published Watch binding", InputActionType.Button,
            "<AppleWatchController>/primaryButton");
        action.Enable();

        AppleWatchControllerDevice.SubmitState(0f, 0f, true);
        InputSystem.Update();

        Assert.That(action.controls.Count, Is.EqualTo(1));
        Assert.That(action.IsPressed(), Is.True);
    }

    [Test]
    public void CurrentReplacesCachedDeviceWhenTheActiveInputManagerChanges()
    {
        typeof(AppleWatchControllerDevice).GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        var original = InputSystem.AddDevice<AppleWatchControllerDevice>();
        Assert.That(AppleWatchControllerDevice.Current, Is.SameAs(original));

        var nested = new InputTestFixture();
        nested.Setup();
        try
        {
            var current = AppleWatchControllerDevice.Current;
            Assert.That(InputSystem.devices, Does.Contain(current), "Current must belong to the active manager, not its saved predecessor.");
            Assert.That(current, Is.Not.SameAs(original));
            using var action = new InputAction("Replacement manager Watch input", InputActionType.Button,
                "<AppleWatchController>/primaryButton");
            action.Enable();
            AppleWatchControllerDevice.SubmitState(0f, 0f, true);
            InputSystem.Update();
            Assert.That(action.IsPressed(), Is.True, "The replacement device must feed actual published-layout actions.");
        }
        finally
        {
            foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
                InputSystem.RemoveDevice(device);
            nested.TearDown();
        }
    }
}
