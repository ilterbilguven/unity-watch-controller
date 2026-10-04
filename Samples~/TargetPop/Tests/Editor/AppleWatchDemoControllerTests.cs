using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public sealed class AppleWatchDemoControllerTests : InputTestFixture
{
    private Type controllerType;
    private MonoBehaviour controller;
    private GameObject host;
    private Transform actor, dial, rig, gyro, marker, bar, pillar;
    private readonly List<string> events = new();

    [SetUp]
    public void CreateDemo()
    {
        controllerType = typeof(AppleWatchDemoController);
        Assert.That(controllerType, Is.Not.Null, "The scene needs a Watch-input demo controller.");
        host = new GameObject("Demo test");
        host.SetActive(false);
        actor = Child("Actor", new Vector3(2f, 1f, -1f));
        dial = Child("Dial", Vector3.zero);
        rig = Child("Rig", Vector3.zero);
        gyro = Child("Gyro", Vector3.zero);
        marker = Child("Touch", new Vector3(-2f, 0.1f, 2f));
        bar = Child("Audio", Vector3.zero);
        pillar = Child("Value", Vector3.zero);
        bar.localScale = new Vector3(0.5f, 2f, 0.5f);
        pillar.localScale = new Vector3(0.5f, 3f, 0.5f);
        controller = (MonoBehaviour)host.AddComponent(controllerType);
        SetField("target", actor);
        SetField("crownDial", dial);
        SetField("motionRig", rig);
        SetField("gyroIndicator", gyro);
        SetField("touchMarker", marker);
        SetField("audioBar", bar);
        SetField("valueIndicator", pillar);
        controllerType.GetEvent("DemoEvent").AddEventHandler(controller, (Action<string>)events.Add);
        Invoke("OnEnable");
    }

    [TearDown]
    public override void TearDown()
    {
        if (controller != null)
        {
            Invoke("OnDisable");
            Invoke("OnDestroy");
        }
        if (host != null) Object.DestroyImmediate(host);
        events.Clear();
        // Manager disposal preserves device indices. Remove explicitly so the
        // transport's static Current cannot outlive this fixture's input manager.
        foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
            InputSystem.RemoveDevice(device);
        base.TearDown();
    }

    [TestCase(SleepTimeout.SystemSetting, SleepTimeout.NeverSleep)]
    [TestCase(SleepTimeout.NeverSleep, SleepTimeout.SystemSetting)]
    public void EnabledDemoKeepsScreenAwakeUntilDisabled(int previousTimeout, int nextTimeout)
    {
        WithSleepTimeout(previousTimeout, () =>
        {
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep),
                "Watch input must keep the foreground game awake without touching the phone.");
            Invoke("OnEnable");
            NotifyApplication("OnApplicationFocus", true);
            Invoke("OnDisable");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(previousTimeout),
                "Repeated enable must not overwrite the original timeout with NeverSleep.");
            Screen.sleepTimeout = nextTimeout;
            Invoke("OnDisable");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(nextTimeout), "Repeated release must leave the new owner alone.");
        });
    }

    [TestCase("OnApplicationPause", true)]
    [TestCase("OnApplicationFocus", false)]
    public void LeavingForegroundRestoresTimeoutAndReturningReacquiresIt(string callback, bool backgroundValue)
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            NotifyApplication(callback, backgroundValue);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting), "Background games must release their keep-awake request.");
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            NotifyApplication(callback, backgroundValue);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep), "Repeated background notification must not restore stale state.");
            NotifyApplication(callback, !backgroundValue);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep));
            Invoke("OnDisable");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep), "Resume must capture the latest previous timeout.");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ResumeWaitsForBothFocusAndPauseRegardlessOfCallbackOrder(bool focusFirst)
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            NotifyApplication("OnApplicationFocus", false);
            NotifyApplication("OnApplicationPause", true);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting));
            NotifyApplication(focusFirst ? "OnApplicationFocus" : "OnApplicationPause", focusFirst);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting), "The other background condition is still active.");
            NotifyApplication(focusFirst ? "OnApplicationPause" : "OnApplicationFocus", !focusFirst);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep));
        });
    }

    [Test]
    public void DisabledDemoDoesNotReacquireOnForegroundCallbacks()
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            Invoke("OnDisable");
            NotifyApplication("OnApplicationPause", false);
            NotifyApplication("OnApplicationFocus", true);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting));
        });
    }

    [Test]
    public void ReenableWhilePausedWaitsForResume()
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            NotifyApplication("OnApplicationPause", true);
            Invoke("OnDisable");
            Invoke("OnEnable");
            NotifyApplication("OnApplicationFocus", true);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting));
            NotifyApplication("OnApplicationPause", false);
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep));
        });
    }

    [Test]
    public void DestroyRestoresPreviousTimeoutWithoutRequiringDisable()
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            Invoke("OnDestroy");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting));
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            NotifyApplication("OnApplicationFocus", true);
            NotifyApplication("OnApplicationPause", false);
            Invoke("OnDestroy");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.NeverSleep));
        });
    }

    [Test]
    public void EnableSamplesCurrentApplicationFocusBeforeReceivingCallbacks()
    {
        WithSleepTimeout(SleepTimeout.SystemSetting, () =>
        {
            Invoke("OnDisable");
            var focused = Application.isFocused;
            NotifyApplication("OnApplicationFocus", !focused);
            Invoke("OnEnable");
            Assert.That(Screen.sleepTimeout, Is.EqualTo(focused ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting),
                "Enabling in the background must not depend on receiving another focus callback.");
        });
    }

    [Test]
    public void MoveUsesAuthoredOriginBoundsAndNeutralStopsMotion()
    {
        Submit(new AppleWatchControllerState { primaryStick = Vector2.right });
        Tick(0.5f);
        Assert.That(actor.position.x, Is.EqualTo(3.5f).Within(0.001f));
        Tick(10f);
        Assert.That(actor.position.x, Is.EqualTo(5.5f).Within(0.001f));
        Submit(default);
        var stopped = actor.position;
        Tick(1f);
        Assert.That(actor.position, Is.EqualTo(stopped));
    }

    [Test]
    public void RepeatedTapPulsesInOneInputUpdateAreAllDelivered()
    {
        var device = AppleWatchControllerDevice.Current;
        InputSystem.QueueStateEvent(device, new AppleWatchControllerState { tap = 1 });
        InputSystem.QueueStateEvent(device, default(AppleWatchControllerState));
        InputSystem.QueueStateEvent(device, new AppleWatchControllerState { tap = 1 });
        InputSystem.Update();
        Tick(0.1f);
        Assert.That(events, Is.EqualTo(new[] { "Tap", "Tap" }));
        Assert.That(actor.position.y, Is.GreaterThan(1f));
    }

    [TestCase("primaryButton", "PrimaryButton")]
    [TestCase("primaryAction", "PrimaryAction")]
    [TestCase("doubleTap", "DoubleTap")]
    public void PressedActionProducesOneVisibleHop(string control, string eventName)
    {
        var state = new AppleWatchControllerState();
        object boxed = state;
        typeof(AppleWatchControllerState).GetField(control).SetValue(boxed, (byte)1);
        Submit((AppleWatchControllerState)boxed);
        Tick(0.1f);
        Assert.That(actor.position.y, Is.GreaterThan(1f));
        Assert.That(events, Is.EqualTo(new[] { eventName }));
        Tick(1f);
        Assert.That(actor.position.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(events.Count, Is.EqualTo(1), "A held button is not a new pulse.");
    }

    [Test]
    public void CrownConsumesEachDeltaOnceInsteadOfIntegratingHeldValue()
    {
        Submit(new AppleWatchControllerState { crown = 0.5f });
        Tick(0.1f);
        var rotation = dial.localRotation;
        Tick(5f);
        Assert.That(Get<float>("CrownTotal"), Is.EqualTo(0.5f));
        Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(0f, 0.5f, 0f)), Is.LessThan(0.01f),
            "The default demo gain is one degree per raw Crown unit.");
        Assert.That(dial.localRotation, Is.EqualTo(rotation));
        Submit(default);
        Submit(new AppleWatchControllerState { crown = -0.25f });
        Assert.That(Get<float>("CrownTotal"), Is.EqualTo(0.25f));
        Assert.That(events, Is.EqualTo(new[] { "Crown", "Crown" }));
    }

    [TestCase(0.25f, 8f, 2f)]
    [TestCase(2f, 3f, 6f)]
    public void CrownUsesConfiguredDegreesPerRawUnit(float gain, float delta, float expectedDegrees)
    {
        var field = controllerType.GetField("crownDegreesPerUnit", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "The demo needs an Inspector-configurable Crown gain.");
        field.SetValue(controller, gain);
        Submit(new AppleWatchControllerState { crown = delta });
        Assert.That(Quaternion.Angle(dial.localRotation, Quaternion.Euler(0f, expectedDegrees, 0f)), Is.LessThan(0.01f));
        Tick(5f);
        Assert.That(Get<float>("CrownTotal"), Is.EqualTo(delta));
        Assert.That(Quaternion.Angle(dial.localRotation, Quaternion.Euler(0f, expectedDegrees, 0f)), Is.LessThan(0.01f),
            "Configured gain must still consume each delta once, not once per frame.");
    }

    [Test]
    public void SwipeDashesAndLongPressRestoresAuthoredPose()
    {
        Submit(new AppleWatchControllerState { swipeRight = 1 });
        Assert.That(actor.position.x, Is.EqualTo(3f).Within(0.001f));
        Submit(default);
        Submit(new AppleWatchControllerState { swipeUp = 1 });
        Assert.That(actor.position.z, Is.EqualTo(0f).Within(0.001f));
        Submit(new AppleWatchControllerState { longPress = 1 });
        Assert.That(actor.position, Is.EqualTo(new Vector3(2f, 1f, -1f)));
        Assert.That(events[events.Count - 1], Is.EqualTo("Reset"));
    }

    [Test]
    public void SensorAndMeterVisualsRespondAndReturnToAuthoredValuesOnNeutral()
    {
        var mode = controllerType.GetProperty("MotionMode");
        Assert.That(mode, Is.Not.Null, "Velocity visualization is now an explicit display mode.");
        mode.SetValue(controller, Enum.Parse(mode.PropertyType, "AngularVelocity"));
        Submit(new AppleWatchControllerState
        {
            acceleration = new Vector3(0.5f, 0f, 0.5f),
            gyro = new Vector3(0f, 1f, 0f), microphoneLevel = 0.8f,
            microphonePeak = 0.9f, axis0 = 0.5f
        });
        Tick(0.5f);
        Assert.That(Quaternion.Angle(rig.localRotation, Quaternion.identity), Is.GreaterThan(1f));
        Assert.That(Quaternion.Angle(gyro.localRotation, Quaternion.identity), Is.GreaterThan(1f));
        Assert.That(bar.localScale.y, Is.GreaterThan(2f));
        Assert.That(pillar.localScale.y, Is.GreaterThan(3f));
        Submit(default);
        Tick(0.5f);
        Assert.That(rig.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(bar.localScale, Is.EqualTo(new Vector3(0.5f, 2f, 0.5f)));
        Assert.That(pillar.localScale, Is.EqualTo(new Vector3(0.5f, 3f, 0.5f)));
    }

    [Test]
    public void TouchPointsUseBoundedVisualizationAndReleaseReturnsMarker()
    {
        Submit(new AppleWatchControllerState { touchPress = 1, touchPosition = new Vector2(10000f, 10000f) });
        Tick(0.1f);
        Assert.That(marker.position.x, Is.InRange(-5.5f, 1.5f));
        Assert.That(marker.position.z, Is.InRange(-1.5f, 5.5f));
        Assert.That(marker.position, Is.Not.EqualTo(new Vector3(-2f, 0.1f, 2f)));
        Submit(default);
        Tick(0.1f);
        Assert.That(marker.position, Is.EqualTo(new Vector3(-2f, 0.1f, 2f)));
    }

    [Test]
    public void DisableRestoresVisualsAndReenableDoesNotDuplicateSubscriptions()
    {
        Submit(new AppleWatchControllerState { primaryStick = Vector2.right, microphoneLevel = 1f });
        Tick(0.5f);
        Invoke("OnDisable");
        Assert.That(actor.position, Is.EqualTo(new Vector3(2f, 1f, -1f)));
        Assert.That(bar.localScale, Is.EqualTo(new Vector3(0.5f, 2f, 0.5f)));
        Submit(new AppleWatchControllerState { tap = 1 });
        Assert.That(events, Is.Empty);
        Submit(default);
        Invoke("OnEnable");
        InputSystem.Update();
        Submit(new AppleWatchControllerState { tap = 1 });
        Assert.That(events, Is.EqualTo(new[] { "Tap" }));
    }

    [Test]
    public void RecordingReceiptDisplaysOnlyFilenameAndUnsubscribesWhenDisabled()
    {
        Invoke("OnDisable");
        var receiver = host.AddComponent<WatchInputReceiver>();
        SetField("receiver", receiver);
        Invoke("OnEnable");
        var receipt = (Action<string>)typeof(WatchInputReceiver)
            .GetField("RecordingReceived", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
        receipt.Invoke("/private/device/recordings/watch-note.m4a");
        Assert.That(Get<string>("LastRecordingName"), Is.EqualTo("watch-note.m4a"));
        Assert.That(Get<int>("RecordingCount"), Is.EqualTo(1));
        Assert.That(events, Is.EqualTo(new[] { "RecordingReceived" }));
        Invoke("OnDisable");
        var afterDisable = (Action<string>)typeof(WatchInputReceiver)
            .GetField("RecordingReceived", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
        afterDisable?.Invoke("/private/device/second.m4a");
        Assert.That(Get<int>("RecordingCount"), Is.EqualTo(1));
    }

    [Test]
    public void ResetRestoresAuthoredRotationsAndScalesWithoutRecenteringAuthoredOrigin()
    {
        Submit(new AppleWatchControllerState { primaryStick = Vector2.up, tap = 1, crown = 0.7f });
        Tick(0.1f);
        Invoke("ResetDemo");
        Assert.That(actor.position, Is.EqualTo(new Vector3(2f, 1f, -1f)));
        Assert.That(actor.localScale, Is.EqualTo(Vector3.one));
        Assert.That(dial.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(Get<float>("CrownTotal"), Is.Zero);
        Assert.That(events[events.Count - 1], Is.EqualTo("Reset"));
    }

    private Transform Child(string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(host.transform);
        child.position = position;
        return child;
    }

    private void Submit(AppleWatchControllerState state)
    {
        InputSystem.QueueStateEvent(AppleWatchControllerDevice.Current, state);
        InputSystem.Update();
    }

    private void Tick(float deltaTime) => Invoke("Tick", deltaTime);

    private void WithSleepTimeout(int previousTimeout, Action assertion)
    {
        Invoke("OnDisable");
        var originalTimeout = Screen.sleepTimeout;
        try
        {
            Screen.sleepTimeout = previousTimeout;
            Assert.That(Screen.sleepTimeout, Is.EqualTo(previousTimeout),
                "This Editor must expose the sleep timeout state for the lifecycle regression.");
            Invoke("OnEnable");
            NotifyApplication("OnApplicationFocus", true);
            assertion();
        }
        finally
        {
            Invoke("OnDisable");
            Screen.sleepTimeout = originalTimeout;
        }
    }

    private void NotifyApplication(string callback, bool value) => controllerType
        .GetMethod(callback, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(controller, new object[] { value });

    private T Get<T>(string name) => (T)controllerType.GetProperty(name).GetValue(controller);
    private void SetField(string name, object value) => controllerType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, value);
    private void Invoke(string name, params object[] args) => controllerType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(controller, args);
}
