using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class AppleWatchDemoMotionTests : InputTestFixture
{
    private sealed class HudTestWindow : EditorWindow { }
    private GameObject host;
    private AppleWatchDemoController controller;
    private Transform indicator;
    private PanelSettings panel;
    private HudTestWindow window;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        InputSystem.RegisterLayout<AppleWatchControllerDevice>();
        host = new GameObject("Motion demo test");
        host.SetActive(false);
        var actor = new GameObject("Actor").transform;
        actor.SetParent(host.transform);
        indicator = new GameObject("Motion indicator").transform;
        indicator.SetParent(host.transform);
        controller = host.AddComponent<AppleWatchDemoController>();
        Set(controller, "target", actor);
        Set(controller, "gyroIndicator", indicator);
        Call(controller, "OnEnable");
    }

    [TearDown]
    public override void TearDown()
    {
        if (window != null) window.Close();
        if (controller != null)
        {
            Call(controller, "OnDisable");
            Call(controller, "OnDestroy");
        }
        if (host != null) Object.DestroyImmediate(host);
        if (panel != null) Object.DestroyImmediate(panel);
        foreach (var device in InputSystem.devices.OfType<AppleWatchControllerDevice>().ToArray())
            InputSystem.RemoveDevice(device);
        base.TearDown();
    }

    [Test]
    public void FirstTrackedPoseCentersWithoutChangingRawPoseOrGyro()
    {
        var pose = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        Submit(pose, true, 1, Vector3.up);
        Assert.That(Read<object>(controller, "MotionMode").ToString(), Is.EqualTo("Orientation"));
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(pose));
        Assert.That(controller.Gyro, Is.EqualTo(Vector3.up));
        Assert.That(Read<bool>(controller, "AttitudeTracked"), Is.True);
        AssertRotation(Quaternion.identity);
        controller.Tick(5f);
        AssertRotation(Quaternion.identity);
    }

    [Test]
    public void TrackedIdentityIsARealPoseEvenWhenTheQuaternionActionIsAtItsDefault()
    {
        Submit(Quaternion.identity, true, 1);
        Assert.That(Read<bool>(controller, "AttitudeTracked"), Is.True);
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(Quaternion.identity));
        Assert.That(Read<bool>(controller, "CanCenterOrientation"), Is.True);
    }

    [Test]
    public void CoreMotionBasisIsConvertedExactlyOnce()
    {
        const float s = 0.70710678f;
        Submit(Quaternion.identity, true, 1);
        var cases = new[]
        {
            (new Quaternion(s, 0, 0, s), Vector3.up, Vector3.back),
            (new Quaternion(0, s, 0, s), Vector3.forward, Vector3.left),
            (new Quaternion(0, 0, s, s), Vector3.right, Vector3.up)
        };
        foreach (var (pose, direction, expected) in cases)
        {
            Submit(pose, true, 1);
            Assert.That(Vector3.Distance(indicator.localRotation * direction, expected), Is.LessThan(0.0001f));
        }
    }

    [Test]
    public void CompoundPoseUsesInverseReferenceBeforeCurrentAndAfterAuthoredRotation()
    {
        var authored = Quaternion.Euler(0, 20, 0);
        Call(controller, "OnDisable");
        indicator.localRotation = authored;
        Set(controller, "capturedPose", false);
        Call(controller, "OnEnable");
        const float s = 0.70710678f;
        Submit(new Quaternion(0, 0, s, s), true, 7);
        Submit(new Quaternion(0.5f, 0.5f, 0.5f, 0.5f), true, 7);
        AssertRotation(authored * Quaternion.Euler(-90, 0, 0));
    }

    [Test]
    public void QuaternionSignAndMagnitudeDoNotChangeTheDisplayedOrientation()
    {
        Submit(Quaternion.identity, true, 1);
        var pose = new Quaternion(0, 1.41421356f, 0, 1.41421356f);
        Submit(pose, true, 1);
        AssertRotation(Quaternion.Euler(0, -90, 0));
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(pose), "Raw input must not be overwritten with the mapped quaternion.");
        Submit(new Quaternion(0, -1.41421356f, 0, -1.41421356f), true, 1);
        AssertRotation(Quaternion.Euler(0, -90, 0));
    }

    [TestCase(false, 1)]
    [TestCase(true, 0)]
    public void UntrackedOrZeroEpochCannotProvideOrientation(bool tracked, int epoch)
    {
        Submit(Quaternion.Euler(10, 20, 30), tracked, (uint)epoch, Vector3.up);
        Assert.That(Read<bool>(controller, "AttitudeTracked"), Is.False);
        Assert.That(Read<bool>(controller, "CanCenterOrientation"), Is.False);
        Assert.That(Call(controller, "CenterOrientation"), Is.False);
        AssertRotation(Quaternion.identity);
    }

    [Test]
    public void BriefTrackingGapKeepsReferenceButNewEpochRecenters()
    {
        Submit(Quaternion.identity, true, 1);
        var pose = new Quaternion(0, 0.70710678f, 0, 0.70710678f);
        Submit(pose, true, 1);
        AssertRotation(Quaternion.Euler(0, -90, 0));
        Submit(Quaternion.identity, false, 0);
        AssertRotation(Quaternion.identity);
        Submit(pose, true, 1);
        AssertRotation(Quaternion.Euler(0, -90, 0));
        Submit(pose, true, 2);
        AssertRotation(Quaternion.identity);
    }

    [Test]
    public void CenterUsesTheCurrentPoseWithoutMutatingInput()
    {
        Submit(Quaternion.identity, true, 1);
        var pose = new Quaternion(0.70710678f, 0, 0, 0.70710678f);
        Submit(pose, true, 1, Vector3.right);
        Assert.That(Call(controller, "CenterOrientation"), Is.True);
        AssertRotation(Quaternion.identity);
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(pose));
        Assert.That(controller.Gyro, Is.EqualTo(Vector3.right));
        Assert.That(controller.LastEvent, Is.EqualTo("OrientationCentered"));
    }

    [Test]
    public void DisplayModesSeparateRealPoseFromVelocityWithoutIntegratingTime()
    {
        Submit(Quaternion.identity, true, 1);
        var pose = new Quaternion(0, 0.70710678f, 0, 0.70710678f);
        Submit(pose, true, 1, Vector3.up);
        AssertRotation(Quaternion.Euler(0, -90, 0));
        SetMode("AngularVelocity");
        AssertRotation(Quaternion.Euler(0, 45, 0));
        controller.Tick(0.01f);
        controller.Tick(5f);
        AssertRotation(Quaternion.Euler(0, 45, 0));
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(pose));
        Assert.That(controller.Gyro, Is.EqualTo(Vector3.up));
        SetMode("Orientation");
        AssertRotation(Quaternion.Euler(0, -90, 0));
        SetMode("AngularVelocity");
        Submit(Quaternion.identity, false, 0, Vector3.up);
        AssertRotation(Quaternion.Euler(0, 45, 0), "Legacy/editor gyro remains useful without a tracked pose.");
        Submit(Quaternion.identity, false, 0);
        AssertRotation(Quaternion.identity);
    }

    [Test]
    public void DeliveredLongPressShowsResetEvenWhenTheGameIsAlreadyAtItsStart()
    {
        var game = host.AddComponent<AppleWatchDemoGame>();
        Set(game, "controller", controller);
        Call(game, "OnEnable");
        Assert.That(game.LastResult, Is.EqualTo("Button fires • Crown aims"));
        SubmitButton(1);
        Assert.That(game.LastResult, Is.EqualTo("Game reset"));
        Assert.That(Read<int>(game, "ResetCount"), Is.EqualTo(1));
        SubmitButton(1);
        Assert.That(Read<int>(game, "ResetCount"), Is.EqualTo(1), "A held state is not another delivered reset pulse.");
        SubmitButton(0);
        SubmitButton(1);
        Assert.That(Read<int>(game, "ResetCount"), Is.EqualTo(2));
        Assert.That(controller.LastEvent, Is.EqualTo("Reset"));
    }

    [Test]
    public void HudCountsActualUpdateCadenceBeforeItsVisualRefreshGate()
    {
        var hud = CreateHud();
        var nextRefresh = typeof(AppleWatchDemoHud).GetField("nextRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
        nextRefresh.SetValue(hud, Convert.ChangeType(10000, nextRefresh.FieldType));
        Call(hud, "UpdateHud", 0d);
        for (var i = 1; i <= 20; i++) Call(hud, "UpdateHud", i / 20d);
        Assert.That(Read<float>(hud, "UpdateFramesPerSecond"), Is.EqualTo(20f).Within(0.01f));
        for (var i = 1; i <= 10; i++) Call(hud, "UpdateHud", 1d + i / 10d);
        Assert.That(Read<float>(hud, "UpdateFramesPerSecond"), Is.EqualTo(10f).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator HudSelectorAndCenterUseRealUiToolkitCallbacksAndAvailability()
    {
        var hud = CreateHud();
        var root = host.GetComponent<UIDocument>().rootVisualElement;
        window = ScriptableObject.CreateInstance<HudTestWindow>();
        window.Show();
        window.rootVisualElement.Add(root);
        yield return null;
        var orientation = root.Q<Button>("orientation-mode");
        var velocity = root.Q<Button>("angular-velocity-mode");
        var center = root.Q<Button>("center-orientation");
        Assert.That(orientation, Is.Not.Null);
        Assert.That(velocity, Is.Not.Null);
        Assert.That(center, Is.Not.Null);
        Call(hud, "RefreshValues");
        Assert.That(center.enabledSelf, Is.False);
        Submit(Quaternion.identity, true, 1);
        var pose = new Quaternion(0, 0.70710678f, 0, 0.70710678f);
        Submit(pose, true, 1, Vector3.up);
        Call(hud, "RefreshValues");
        Assert.That(center.enabledSelf, Is.True);
        Click(velocity);
        Assert.That(Read<object>(controller, "MotionMode").ToString(), Is.EqualTo("AngularVelocity"));
        Assert.That(velocity.ClassListContains("is-selected"), Is.True);
        Assert.That(root.Q<Label>("gyro-label").text, Does.Contain("rad/s"));
        Assert.That(root.Q<Label>("motion-description").text, Does.Contain("still"));
        Click(orientation);
        Assert.That(Read<object>(controller, "MotionMode").ToString(), Is.EqualTo("Orientation"));
        Click(center);
        AssertRotation(Quaternion.identity);
        Assert.That(Read<Quaternion>(controller, "Attitude"), Is.EqualTo(pose));
        Submit(Quaternion.identity, false, 0);
        Call(hud, "RefreshValues");
        Assert.That(center.enabledSelf, Is.False);
        Assert.That(root.Q<Label>("orientation-status").text, Does.Contain("unavailable"));
        Assert.That(root.Q<Label>("transport-stats").text, Does.Contain("simulation"));
    }

    private AppleWatchDemoHud CreateHud()
    {
        var document = host.AddComponent<UIDocument>();
        panel = ScriptableObject.CreateInstance<PanelSettings>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Samples/Apple Watch Controller/0.1.0-preview.1/Target Pop/Demo/WatchDemoHud.uxml");
        var hud = host.AddComponent<AppleWatchDemoHud>();
        Set(hud, "controller", controller);
        host.SetActive(true);
        Call(hud, "OnEnable");
        return hud;
    }

    private void Submit(Quaternion pose, bool tracked, uint reference, Vector3 velocity = default)
    {
        object state = new AppleWatchControllerState { gyro = velocity };
        SetState(state, "attitude", pose);
        SetState(state, "attitudeTracked", tracked ? 1 : 0);
        SetState(state, "attitudeReference", reference);
        InputSystem.QueueStateEvent(AppleWatchControllerDevice.Current, (AppleWatchControllerState)state);
        InputSystem.Update();
        controller.Tick(0.02f);
    }

    private void SubmitButton(byte pressed)
    {
        InputSystem.QueueStateEvent(AppleWatchControllerDevice.Current, new AppleWatchControllerState { longPress = pressed });
        InputSystem.Update();
    }

    private void SetMode(string mode)
    {
        var property = typeof(AppleWatchDemoController).GetProperty("MotionMode");
        Assert.That(property, Is.Not.Null, "The demo must expose both motion display modes.");
        property.SetValue(controller, Enum.Parse(property.PropertyType, mode));
    }

    private void AssertRotation(Quaternion expected, string message = null) =>
        Assert.That(Quaternion.Angle(indicator.localRotation, expected), Is.LessThan(0.02f), message);

    private static void Click(Button button)
    {
        button.Focus();
        using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
    }

    private static void SetState(object state, string name, object value)
    {
        var field = state.GetType().GetField(name);
        Assert.That(field, Is.Not.Null, "Motion transport state must provide " + name);
        field.SetValue(state, field.FieldType == value.GetType() ? value : Convert.ChangeType(value, field.FieldType));
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static T Read<T>(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.That(property, Is.Not.Null, "Expected public demo state: " + name);
        return (T)property.GetValue(target);
    }

    private static object Call(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Expected demo behavior: " + name);
        return method.Invoke(target, arguments);
    }
}
