using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WatchUI;

public sealed class WatchUiSwiftGeneratorTests
{
    [Test]
    public void AuthoredMotionExportsOptInAttitudeAtTwentyHertz()
    {
        using var fixture = new WatchUiTestAssets("<watch:watch-motion include-attitude=\"true\" include-acceleration=\"true\" include-gyro=\"true\" update-interval=\"0.05\" />");
        var motion = fixture.Source.CloneTree().Q<WatchMotion>();
        Assert.That(motion.IncludeAttitude, Is.True);
        Assert.That(motion.IncludeAcceleration && motion.IncludeGyro, Is.True);
        Assert.That(motion.UpdateInterval, Is.EqualTo(0.05f));
        Assert.That(new WatchMotion().IncludeAttitude, Is.False, "Attitude remains opt-in for existing authored controls.");
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(motion, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain("interval: 0.05, includeAcceleration: true, includeGyro: true, includeAttitude: true)"));
    }

    [Test]
    public void GenericSwipeEmitsOneReleaseClassifierWithExistingDirectionalKinds()
    {
        var node = CreateNode("WatchGestureArea");
        node.gesture = "swipe";
        node.minimumDistance = 32;
        var swift = WatchUiSwiftEmitter.ToSwiftUI(node, out _);
        Assert.That(Regex.Matches(swift, @"\.onEnded \{ value in").Count, Is.EqualTo(1));
        Assert.That(swift, Does.Contain("max(abs(dx), abs(dy)) >= 32"));
        Assert.That(swift, Does.Contain("abs(dx) >= abs(dy)"));
        foreach (var direction in new[] { "left", "right", "up", "down" })
            Assert.That(swift, Does.Contain("kind: \"WatchGestureArea/swipe-" + direction + "\""));
        Assert.That(swift, Does.Not.Contain("WatchGestureArea/fallbackTap"));
    }

    [Test]
    public void LongPressExportsExplicitAuthoredMaximumDistance()
    {
        var node = JsonUtility.FromJson<WatchUiNode>("{\"kind\":\"WatchGestureArea\",\"id\":\"hold\",\"gesture\":\"long-press\",\"minimumDuration\":1,\"maximumDistance\":30}");
        var swift = WatchUiSwiftEmitter.ToSwiftUI(node, out _);
        Assert.That(swift, Does.Contain(".onLongPressGesture(minimumDuration: 1, maximumDistance: 30)"));
        Assert.That(swift, Does.Contain("\"longPress\": 1"));
    }

    [TestCase("WatchButton")]
    [TestCase("WatchToggle")]
    [TestCase("WatchNavigationLink")]
    [TestCase("WatchTextField")]
    public void HandShortcutUsesTheNativeControlsExistingPrimaryInteraction(string kind)
    {
        var node = CreateHandShortcutNode(kind, "PrimaryAction");
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(Regex.Matches(swift, @"\.handGestureShortcut\(\.primaryAction\)").Count, Is.EqualTo(1));
        Assert.That(swift, Does.Contain("            .handGestureShortcut(.primaryAction)"),
            "The modifier belongs to the concrete root control, not to a nested label or surrounding container.");

        var direct = CreateHandShortcutNode(kind, "None");
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(direct, out var directSwift, out _), Is.True);
        Assert.That(swift.Replace("            .handGestureShortcut(.primaryAction)\n", ""), Is.EqualTo(directSwift),
            "Hand activation must reuse the same binding/action/navigation interaction without an origin flag or extra callback.");
    }

    [TestCase("WatchText")]
    [TestCase("WatchCrown")]
    [TestCase("WatchGestureArea")]
    [TestCase("WatchSlider")]
    [TestCase("WatchVStack")]
    public void HandShortcutRejectsNonButtonlikeControls(string kind)
    {
        var node = CreateHandShortcutNode(kind, "PrimaryAction");
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("WATCHUI005") && error.Contains(kind)
            && error.Contains("hand-gesture-shortcut")), Is.True);
    }

    [TestCase("DoubleTap")]
    [TestCase("primaryAction")]
    public void HandShortcutRejectsUnknownValues(string shortcut)
    {
        var node = CreateHandShortcutNode("WatchButton", shortcut);
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("WATCHUI006") && error.Contains(shortcut)), Is.True);
    }

    [Test]
    public void HandShortcutIsUniqueAcrossAllNavigationDestinations()
    {
        var first = CreateHandShortcutNode("WatchButton", "PrimaryAction");
        first.id = "firstPrimary";
        var second = CreateHandShortcutNode("WatchToggle", "PrimaryAction");
        second.id = "secondPrimary";
        var root = new WatchUiNode
        {
            kind = "WatchNavigationStack",
            children = new[]
            {
                new WatchUiNode { kind = "WatchNavigationDestination", route = "first", children = new[] { first } },
                new WatchUiNode { kind = "WatchNavigationDestination", route = "second", children = new[] { second } }
            }
        };

        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(root, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("WATCHUI102") && error.Contains("Scene")
            && error.Contains("firstPrimary") && error.Contains("secondPrimary")), Is.True);
        Assert.That(WatchUiSwiftEmitter.ToSwiftUI(root, out _), Is.Empty,
            "The convenience emitter must not bypass scene-wide shortcut validation.");
    }

    [Test]
    public void HandShortcutWarnsWhenTheSceneAlsoContainsScrolling()
    {
        var root = new WatchUiNode
        {
            kind = "WatchScrollView", axes = "vertical",
            children = new[] { CreateHandShortcutNode("WatchButton", "PrimaryAction") }
        };
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(root, out _, out var diagnostics), Is.True);
        Assert.That(diagnostics.Warnings.Any(warning => warning.Contains("PrimaryAction") && warning.Contains("scroll")), Is.True);
    }

    [Test]
    public void AuthoredHandGesturePageHasOneNativePrimaryButtonWithoutScrolling()
    {
        using var fixture = new WatchUiTestAssets("<watch:watch-navigation-stack>"
            + "<watch:watch-navigation-link route=\"hand-gestures\" title=\"Open\" />"
            + "<watch:watch-navigation-destination route=\"hand-gestures\">"
            + "<watch:watch-button watch-id=\"handPrimary\" label=\"Activate\" action=\"primary\" hand-gesture-shortcut=\"PrimaryAction\" />"
            + "</watch:watch-navigation-destination></watch:watch-navigation-stack>");
        var asset = fixture.Source;
        var root = asset.CloneTree();
        var pages = root.Query<WatchNavigationDestination>().ToList().Where(page => page.Route == "hand-gestures").ToList();
        Assert.That(pages.Count, Is.EqualTo(1));
        var page = pages[0];
        Assert.That(page.Query<WatchScrollView>().ToList(), Is.Empty);
        Assert.That(page.Query<WatchButton>().ToList().Count, Is.EqualTo(1));
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root.Q<WatchNavigationStack>(), out var swift, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(Regex.Matches(swift, @"\.handGestureShortcut\(\.primaryAction\)").Count, Is.EqualTo(1));
        Assert.That(swift, Does.Contain("NavigationLink(value: \"hand-gestures\")"));
        Assert.That(swift, Does.Contain("sourceId: \"handPrimary\", kind: \"WatchButton\""));
    }

    [Test]
    public void CrownReceivesFocusBeforeRotationAndTapOnlyAcquiresFocus()
    {
        var swift = WatchUiSwiftEmitter.ToSwiftUI(CreateNode("WatchCrown"), out _);

        Assert.That(swift, Does.Contain("@FocusState private var watchCrownFocused: Bool"));
        var focusable = swift.IndexOf(".focusable()", StringComparison.Ordinal);
        var focused = swift.IndexOf(".focused($watchCrownFocused)", StringComparison.Ordinal);
        var rotation = swift.IndexOf(".digitalCrownRotation($watchCrownValue)", StringComparison.Ordinal);
        Assert.That(focusable, Is.GreaterThan(0));
        Assert.That(focused, Is.GreaterThan(focusable));
        Assert.That(rotation, Is.GreaterThan(focused));
        Assert.That(swift, Does.Contain(".onTapGesture { watchCrownFocused = true }"));
        Assert.That(swift, Does.Contain(".onAppear { watchCrownFocused = true }"));
        Assert.That(swift, Does.Not.Contain("WatchCrown/press"));
        Assert.That(swift, Does.Not.Contain("\"primaryButton\": 1"));
        Assert.That(swift, Does.Contain("\"crown\": delta, \"axis0\": delta"));
    }

    [Test]
    public void MultipleCrownsKeepIndependentFocusAndRotationHistory()
    {
        var root = new WatchUiNode
        {
            kind = "WatchVStack",
            children = new[] { CreateNode("WatchCrown"), CreateNode("WatchCrown") }
        };
        var swift = WatchUiSwiftEmitter.ToSwiftUI(root, out _);

        foreach (var suffix in new[] { "", "_2" })
        {
            Assert.That(swift, Does.Contain("@State private var watchCrownValue" + suffix + ": Double = 0"));
            Assert.That(swift, Does.Contain("@State private var watchCrownPreviousValue" + suffix + ": Double = 0"));
            Assert.That(swift, Does.Contain("@FocusState private var watchCrownFocused" + suffix + ": Bool"));
            Assert.That(swift, Does.Contain(".digitalCrownRotation($watchCrownValue" + suffix + ")"));
            Assert.That(swift, Does.Contain(".focused($watchCrownFocused" + suffix + ")"));
            Assert.That(swift, Does.Contain(".onChange(of: watchCrownValue" + suffix + ") { _, newValue in"));
            Assert.That(swift, Does.Contain("let delta = Float(newValue - watchCrownPreviousValue" + suffix + ")"));
            Assert.That(swift, Does.Contain("watchCrownPreviousValue" + suffix + " = newValue"));
        }
    }

    [Test]
    public void DisabledCrownDoesNotAcquireFocusOrEmitInput()
    {
        var node = CreateNode("WatchCrown");
        node.enabled = false;
        var swift = WatchUiSwiftEmitter.ToSwiftUI(node, out _);

        Assert.That(swift, Does.Contain(".disabled(true)"));
        Assert.That(swift, Does.Not.Contain(".focusable("));
        Assert.That(swift, Does.Not.Contain(".focused("));
        Assert.That(swift, Does.Not.Contain(".digitalCrownRotation("));
        Assert.That(swift, Does.Not.Contain("WatchCrown/rotate"));
    }

    [Test]
    public void GeneratedAppInitializesAndUpdatesSensorForegroundState()
    {
        var swift = WatchUiSwiftEmitter.ToSwiftUI(CreateNode("WatchMicrophone"), out _);

        Assert.That(swift, Does.Contain("@Environment(\\.scenePhase) private var scenePhase"));
        Assert.That(swift, Does.Contain(".onChange(of: scenePhase, initial: true) { _, phase in"));
        Assert.That(swift, Does.Contain("WatchSensorBridge.shared.setSceneActive(phase == .active)"));
    }

    [TestCase("WatchText")]
    [TestCase("WatchMotion")]
    [TestCase("WatchMicrophone")]
    public void GeneratedSensorTypesHaveTheirFrameworkImports(string kind)
    {
        var swift = WatchUiSwiftEmitter.ToSwiftUI(CreateNode(kind), out _);

        if (swift.Contains("CMMotionManager"))
            Assert.That(swift, Does.Contain("import CoreMotion"));
        if (swift.Contains("AVAudioRecorder"))
            Assert.That(swift, Does.Contain("import AVFoundation"));
    }

    [TestCase("WatchToggle", "control", "\"value\": (newValue ? 1 : 0)")]
    [TestCase("WatchSlider", "controlValue", "\"value\": Float(newValue)")]
    [TestCase("WatchStepper", "controlValue", "\"value\": Float(newValue)")]
    [TestCase("WatchPicker", "controlSelection", "\"value\": newValue")]
    [TestCase("WatchTextField", "controlText", "\"value\": newValue")]
    [TestCase("WatchCrown", "watchCrownValue", "\"crown\": delta")]
    public void ChangeHandlersUseModernCallbackAndPreservePayload(string kind, string state, string payload)
    {
        var swift = WatchUiSwiftEmitter.ToSwiftUI(CreateNode(kind), out _);

        Assert.That(swift, Does.Contain(".onChange(of: " + state + ") { _, newValue in"));
        Assert.That(swift, Does.Contain(payload));
    }

    [TestCase("segmented")]
    [TestCase("menu")]
    [TestCase("unknown")]
    public void UnsupportedPickerStyleFallsBackWithActionableDiagnostic(string style)
    {
        var picker = CreateNode("WatchPicker");
        picker.style = style;

        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(picker, out var swift, out var diagnostics), Is.True);
        Assert.That(swift, Does.Not.Contain(".pickerStyle(." + style + ")"));
        Assert.That(diagnostics.Warnings.Any(warning => warning.Contains(style) && warning.Contains("automatic")), Is.True);

        WatchUiSwiftEmitter.ToSwiftUI(picker, out var hadWarnings);
        Assert.That(hadWarnings, Is.True);
    }

    [TestCase("automatic")]
    [TestCase("wheel")]
    public void SupportedPickerStylePreservesOptionsWithoutWarnings(string style)
    {
        var picker = CreateNode("WatchPicker");
        picker.style = style;

        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(picker, out var swift, out var diagnostics), Is.True);
        Assert.That(swift, Does.Contain(".tag(\"one\")"));
        Assert.That(diagnostics.Warnings, Is.Empty);
        if (style == "wheel")
            Assert.That(swift, Does.Contain(".pickerStyle(.wheel)"));
    }

    [TestCase("WatchSlider")]
    [TestCase("WatchStepper")]
    [TestCase("WatchGauge")]
    public void InvalidLeafRangeReturnsNoSwift(string kind)
    {
        var node = CreateNode(kind);
        node.minimum = 5;
        node.maximum = 1;

        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains(kind) && error.Contains("maximum")), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VisualElementConversionRejectsInvalidRangesAtAnyDepth(bool nested)
    {
        var slider = new WatchSlider { WatchId = "invalidRange", Minimum = 5, Maximum = 1 };
        var root = new WatchVStack();
        root.Add(slider);

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(nested ? root : (WatchUiElement)slider,
            out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("invalidRange") && error.Contains("maximum")), Is.True);
    }

    [Test]
    public void VisualElementConversionRetainsValidationAndEmissionWarnings()
    {
        var root = new WatchVStack { WatchId = "layout", Alignment = "invalidAlignment" };
        var picker = new WatchPicker { WatchId = "mode", Style = "menu" };
        picker.Add(new WatchPickerOption { Value = "one", Label = "One" });
        root.Add(picker);

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out var diagnostics), Is.True);
        Assert.That(swift, Is.Not.Empty);
        Assert.That(diagnostics.Warnings.Any(warning => warning.Contains("invalidAlignment")), Is.True);
        Assert.That(diagnostics.Warnings.Any(warning => warning.Contains("menu") && warning.Contains("automatic")), Is.True);
    }

    [TestCase("WatchButton")]
    [TestCase("WatchToggle")]
    [TestCase("WatchSlider")]
    [TestCase("WatchStepper")]
    [TestCase("WatchPicker")]
    [TestCase("WatchTextField")]
    [TestCase("WatchGestureArea")]
    [TestCase("WatchMotion")]
    [TestCase("WatchMicrophone")]
    [TestCase("WatchVStack")]
    public void DisabledNodesBlockControlsAndCustomHitTesting(string kind)
    {
        var node = CreateNode(kind);
        node.enabled = false;

        var swift = WatchUiSwiftEmitter.ToSwiftUI(node, out _);

        Assert.That(swift, Does.Contain(".disabled(true)"));
        Assert.That(swift, Does.Contain(".allowsHitTesting(false)"));
    }

    [TestCase("WatchMotion", "stopMotion")]
    [TestCase("WatchMicrophone", "stopMicrophone")]
    public void DisabledSensorsStillAttachDisappearanceCleanup(string kind, string stopMethod)
    {
        var node = CreateNode(kind);
        node.enabled = false;

        var swift = WatchUiSwiftEmitter.ToSwiftUI(node, out _);

        Assert.That(swift, Does.Contain(".onDisappear { WatchSensorBridge.shared." + stopMethod + "(sourceId: \"control\") }"));
    }

    private static WatchUiNode CreateNode(string kind)
    {
        return new WatchUiNode
        {
            kind = kind,
            id = "control",
            label = "Control",
            action = "changed",
            minimum = 0,
            maximum = 10,
            step = 1,
            textBinding = "input",
            gesture = "tap",
            includeAcceleration = true,
            updateInterval = 0.1f,
            children = kind == "WatchPicker"
                ? new[] { new WatchUiNode { kind = "WatchPickerOption", selection = "one", label = "One" } }
                : null
        };
    }

    private static WatchUiNode CreateHandShortcutNode(string kind, string shortcut)
    {
        return JsonUtility.FromJson<WatchUiNode>("{\"kind\":\"" + kind + "\",\"id\":\"control\","
            + "\"label\":\"Control\",\"action\":\"primary\",\"route\":\"details\",\"textBinding\":\"input\","
            + "\"enabled\":true,\"minimum\":0,\"maximum\":10,\"gesture\":\"tap\",\"handGestureShortcut\":\"" + shortcut + "\"}");
    }
}
