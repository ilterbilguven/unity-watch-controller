using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using WatchUI;

public sealed class WatchUiPreviewTests
{
    private sealed class PreviewTestWindow : EditorWindow { }
    private PreviewTestWindow window;
    private EditorWindow toolWindow;
    private string toolFixtureFolder;

    [TearDown]
    public void TearDown()
    {
        if (window != null)
            window.Close();
        if (toolWindow != null)
            toolWindow.Close();
        if (!string.IsNullOrEmpty(toolFixtureFolder))
            AssetDatabase.DeleteAsset(toolFixtureFolder);
    }

    [UnityTest]
    public IEnumerator GenericSwipePreviewClassifiesOneDirectionPerRelease()
    {
        var area = new WatchGestureArea { Gesture = "swipe", MinimumDistance = 32, Action = "swipe" };
        yield return Mount(area);
        var surface = area.Q(className: "watch-preview-surface");
        var count = 0;
        area.PreviewAction += _ => count++;
        foreach (var sample in new[]
        {
            (new Vector2(-32, 0), "left"), (new Vector2(32, 0), "right"),
            (new Vector2(0, -32), "up"), (new Vector2(0, 32), "down"),
            (new Vector2(-40, 32), "left"), (new Vector2(32, -40), "up"),
            (new Vector2(32, 32), "right"), (new Vector2(31, 31), ""), (Vector2.zero, "")
        })
        {
            var before = count;
            var origin = new Vector2(60, 40);
            Pointer(surface, EventType.MouseDown, origin);
            yield return null;
            Pointer(surface, EventType.MouseDrag, origin + sample.Item1);
            yield return null;
            Assert.That(count, Is.EqualTo(before), "Swipes must not emit during movement.");
            Pointer(surface, EventType.MouseUp, origin + sample.Item1);
            yield return null;
            Assert.That(count, Is.EqualTo(before + (sample.Item2.Length == 0 ? 0 : 1)));
            if (sample.Item2.Length != 0)
                Assert.That(area.Query<Label>().ToList().Any(label => label.text.Contains("swipe-" + sample.Item2)), Is.True);
        }
        var accepted = count;
        Pointer(surface, EventType.MouseDown, Vector2.zero);
        yield return null;
        Pointer(surface, EventType.MouseDrag, new Vector2(50, 0));
        yield return null;
        Pointer(surface, EventType.MouseUp, new Vector2(3, 0));
        yield return null;
        Assert.That(count, Is.EqualTo(accepted), "A swipe that returns below threshold must not fire.");
    }

    [UnityTest]
    public IEnumerator HoldPreviewRecognizesWhilePressedExactlyOnce()
    {
        var area = new WatchGestureArea { Gesture = "long-press", MinimumDuration = 0.05f, Action = "reset" };
        yield return Mount(area);
        var surface = area.Q(className: "watch-preview-surface");
        var count = 0;
        area.PreviewAction += _ => count++;
        Pointer(surface, EventType.MouseDown, new Vector2(40, 30));
        yield return new WaitForSecondsRealtime(0.15f);
        Assert.That(count, Is.EqualTo(1), "Recognition must occur while the finger is still down.");
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(count, Is.EqualTo(1));
        Pointer(surface, EventType.MouseUp, new Vector2(40, 30));
        yield return null;
        Assert.That(count, Is.EqualTo(1), "Release must not emit a second reset.");
    }

    [UnityTest]
    public IEnumerator HoldPreviewRejectsPeakDriftEvenWhenThePointerReturns()
    {
        var area = new WatchGestureArea { Gesture = "long-press", MinimumDuration = 0.05f, Action = "reset" };
        yield return Mount(area);
        var surface = area.Q(className: "watch-preview-surface");
        var count = 0;
        area.PreviewAction += _ => count++;
        Pointer(surface, EventType.MouseDown, Vector2.zero);
        Pointer(surface, EventType.MouseDrag, new Vector2(30, 0));
        Pointer(surface, EventType.MouseDrag, Vector2.zero);
        yield return new WaitForSecondsRealtime(0.15f);
        Pointer(surface, EventType.MouseUp, Vector2.zero);
        yield return null;
        Assert.That(count, Is.Zero);
    }

    [UnityTest] public IEnumerator HoldPreviewCancelsOnEarlyRelease() => CancelHold("release");
    [UnityTest] public IEnumerator HoldPreviewCancelsOnDisable() => CancelHold("disable");
    [UnityTest] public IEnumerator HoldPreviewCancelsOnDetach() => CancelHold("detach");
    [UnityTest] public IEnumerator HoldPreviewCancelsOnCaptureLoss() => CancelHold("capture");
    [UnityTest] public IEnumerator HoldPreviewRejectsExplicitPointerCancel() => ExplicitPointerCancel("long-press");
    [UnityTest] public IEnumerator SwipePreviewRejectsExplicitPointerCancel() => ExplicitPointerCancel("swipe");

    private IEnumerator ExplicitPointerCancel(string gesture)
    {
        var area = new WatchGestureArea { Gesture = gesture, MinimumDuration = 0.1f, MinimumDistance = 32, Action = "gesture" };
        yield return Mount(area);
        var surface = area.Q(className: "watch-preview-surface");
        var count = 0;
        area.PreviewAction += _ => count++;
        Pointer(surface, EventType.MouseDown, Vector2.zero);
        var end = gesture == "swipe" ? new Vector2(50, 0) : Vector2.zero;
        Pointer(surface, EventType.MouseDrag, end);
        using (var cancel = PointerCancelEvent.GetPooled(new Event
               { type = EventType.MouseUp, button = 0, mousePosition = surface.LocalToWorld(end) }))
        {
            Assert.That(cancel.pointerType, Is.EqualTo(UnityEngine.UIElements.PointerType.mouse));
            cancel.target = surface;
            surface.SendEvent(cancel);
        }
        yield return new WaitForSecondsRealtime(0.2f);
        Pointer(surface, EventType.MouseUp, end);
        yield return null;
        Assert.That(count, Is.Zero, "An explicit pointer cancel must suppress both scheduled recognition and a later release.");
    }

    private IEnumerator CancelHold(string reason)
    {
        var area = new WatchGestureArea { Gesture = "long-press", MinimumDuration = 0.08f, Action = "reset" };
        yield return Mount(area);
        var surface = area.Q(className: "watch-preview-surface");
        var count = 0;
        area.PreviewAction += _ => count++;
        Pointer(surface, EventType.MouseDown, Vector2.zero);
        if (reason == "release") Pointer(surface, EventType.MouseUp, Vector2.zero);
        if (reason == "disable") area.EnabledControl = false;
        if (reason == "detach") area.RemoveFromHierarchy();
        if (reason == "capture") surface.ReleasePointer(PointerId.mousePointerId);
        yield return new WaitForSecondsRealtime(0.15f);
        if (reason == "detach") window.rootVisualElement.Add(area);
        if (reason == "disable") area.EnabledControl = true;
        yield return new WaitForSecondsRealtime(0.1f);
        Assert.That(count, Is.Zero, "An obsolete press must never fire after " + reason + ".");
    }

    private static void Pointer(VisualElement target, EventType type, Vector2 localPoint)
    {
        var input = new Event { type = type, button = 0, clickCount = 1, mousePosition = target.LocalToWorld(localPoint) };
        if (type == EventType.MouseDown)
        {
            using var evt = PointerDownEvent.GetPooled(input);
            evt.target = target;
            target.SendEvent(evt);
        }
        else if (type == EventType.MouseUp)
        {
            using var evt = PointerUpEvent.GetPooled(input);
            evt.target = target;
            target.SendEvent(evt);
        }
        else
        {
            using var evt = PointerMoveEvent.GetPooled(input);
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    [UnityTest]
    public IEnumerator PreviewWindowChromeFitsAtMinimumWidthWithLongSourceName()
    {
        yield return OpenToolFixture(false);
        var root = toolWindow.rootVisualElement;
        var toolbar = root.Q<Toolbar>();
        var field = toolbar.Q<ObjectField>();
        var reload = toolbar.Query<ToolbarButton>().ToList().Single(button => button.text == "Reload");
        var export = toolbar.Query<ToolbarButton>().ToList().Single(button => button.text == "Export");
        var footer = root.Children().OfType<Label>().Single();

        Assert.That(root.resolvedStyle.width, Is.EqualTo(320).Within(0.1f));
        Assert.That(field.worldBound.width, Is.GreaterThan(40));
        Assert.That(field.worldBound.xMax, Is.LessThanOrEqualTo(reload.worldBound.xMin + 0.1f));
        Assert.That(export.worldBound.xMax, Is.LessThanOrEqualTo(root.worldBound.xMax + 0.1f),
            "The source field must shrink before either toolbar action leaves the window.");
        Assert.That(footer.worldBound.width, Is.LessThanOrEqualTo(root.worldBound.width));
        Assert.That(footer.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis));
        Assert.That(footer.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.NoWrap));
        Assert.That(footer.tooltip, Is.EqualTo(footer.text), "The full source/status remains available when truncated.");
        Assert.That(root.Q("watch-export-overlay"), Is.Null, "Ordinary preview must remain interactive.");
    }

    [UnityTest]
    public IEnumerator NavigationHeaderSeparatesBackAndLongRouteWithoutBlockingBack()
    {
        const string route = "a-long-route-that-must-not-push-the-back-button-outside-the-watch";
        var stack = new WatchNavigationStack();
        stack.Add(new WatchText { Label = "Home" });
        stack.Add(new WatchNavigationDestination { Route = route });
        yield return Mount(stack);
        Assert.That(stack.NavigateTo(route), Is.True);
        yield return null;
        yield return null;
        var back = stack.Q<Button>(className: "watch-preview-back");
        var caption = stack.Q<Label>(className: "watch-preview-route");
        Assert.That(back.worldBound.width, Is.GreaterThanOrEqualTo(44));
        Assert.That(caption.worldBound.xMin, Is.GreaterThanOrEqualTo(back.worldBound.xMax + 6));
        Assert.That(caption.worldBound.xMax, Is.LessThanOrEqualTo(stack.worldBound.xMax + 0.1f));
        Assert.That(caption.resolvedStyle.textOverflow, Is.EqualTo(TextOverflow.Ellipsis));
        window.Focus();
        back.Focus();
        yield return null;
        using (var submit = NavigationSubmitEvent.GetPooled())
            back.SendEvent(submit);
        yield return null;
        Assert.That(stack.CurrentRoute, Is.Empty, "The real Back button still activates normal preview navigation.");
    }

    [UnityTest]
    public IEnumerator ExportMeasurementMaskCoversLayoutWithoutChangingGeneratedContent()
    {
        yield return OpenToolFixture(true);
        var root = toolWindow.rootVisualElement;
        var screen = root.Q("watch-screen");
        var overlay = root.Q("watch-export-overlay");
        Assert.That(overlay, Is.Not.Null, "Export measurement must be presented as preparation, not an interactive Watch screen.");
        Assert.That(overlay.parent, Is.SameAs(root));
        Assert.That(overlay.resolvedStyle.backgroundColor.a, Is.EqualTo(1));
        Assert.That(overlay.worldBound.xMin, Is.LessThanOrEqualTo(screen.worldBound.xMin));
        Assert.That(overlay.worldBound.xMax, Is.GreaterThanOrEqualTo(screen.worldBound.xMax));
        Assert.That(overlay.worldBound.yMin, Is.LessThanOrEqualTo(screen.worldBound.yMin));
        Assert.That(overlay.worldBound.yMax, Is.GreaterThanOrEqualTo(screen.worldBound.yMax));
        Assert.That(overlay.Q<Label>().text, Is.EqualTo("Preparing Watch UI export…"));
        Assert.That(screen.resolvedStyle.opacity, Is.EqualTo(1));
        Assert.That(screen.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
        var watchRoot = screen.Q<WatchNavigationStack>();
        var measured = watchRoot.Query<WatchText>().ToList().Single(text => text.Label == "Measured text");
        Assert.That(measured.resolvedStyle.fontSize, Is.EqualTo(31));
        Assert.That(measured.resolvedStyle.paddingLeft, Is.EqualTo(17));
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(watchRoot, out var masked, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));

        overlay.RemoveFromHierarchy();
        yield return null;
        yield return null;
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(watchRoot, out var unmasked, out _), Is.True);
        Assert.That(unmasked, Is.EqualTo(masked), "A sibling export mask must not affect authored styles or generated Swift bytes.");
    }

    private IEnumerator OpenToolFixture(bool forExport)
    {
        var folderName = "WatchPreviewChrome_" + Guid.NewGuid().ToString("N");
        toolFixtureFolder = "Assets/" + folderName;
        AssetDatabase.CreateFolder("Assets", folderName);
        var source = toolFixtureFolder + "/AnExtremelyLongWatchControllerSourceNameThatCannotFitInThePreviewToolbar.watch.uxml";
        File.WriteAllText(toolFixtureFolder + "/Chrome.uss", ".measured { font-size: 31px; padding: 5px 11px 13px 17px; }");
        AssetDatabase.ImportAsset(toolFixtureFolder + "/Chrome.uss", ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(source,
            "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:watch=\"WatchUI\"><ui:Style src=\"Chrome.uss\" />"
            + "<watch:watch-navigation-stack><watch:watch-text label=\"Home\" />"
            + "<watch:watch-navigation-destination route=\"details\"><watch:watch-text label=\"Measured text\" class=\"measured\" />"
            + "</watch:watch-navigation-destination></watch:watch-navigation-stack></ui:UXML>");
        AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceSynchronousImport);
        var type = Type.GetType("AppleWatchController.Editor.WatchUiPreviewWindow, AppleWatchController.Editor", true);
        Task task;
        if (forExport)
        {
            task = (Task)type.GetMethod("CreateExportHostAsync", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { source });
        }
        else
        {
            toolWindow = (EditorWindow)ScriptableObject.CreateInstance(type);
            toolWindow.ShowUtility();
            toolWindow.position = new Rect(100, 100, 320, 340);
            task = (Task)type.GetMethod("LoadSourceAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(toolWindow, new object[] { source, null, false });
        }
        var deadline = EditorApplication.timeSinceStartup + 30;
        while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline)
            yield return null;
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
        if (forExport)
            toolWindow = (EditorWindow)task.GetType().GetProperty("Result").GetValue(task);
        yield return null;
        yield return null;
    }

    [TestCase(typeof(WatchText), typeof(Label))]
    [TestCase(typeof(WatchLabel), typeof(Label))]
    [TestCase(typeof(WatchButton), typeof(Button))]
    [TestCase(typeof(WatchToggle), typeof(Toggle))]
    [TestCase(typeof(WatchSlider), typeof(Slider))]
    [TestCase(typeof(WatchStepper), typeof(Button))]
    [TestCase(typeof(WatchPicker), typeof(PopupField<string>))]
    [TestCase(typeof(WatchTextField), typeof(TextField))]
    [TestCase(typeof(WatchScrollView), typeof(ScrollView))]
    [TestCase(typeof(WatchList), typeof(ScrollView))]
    [TestCase(typeof(WatchNavigationLink), typeof(Button))]
    [TestCase(typeof(WatchProgressView), typeof(ProgressBar))]
    [TestCase(typeof(WatchGauge), typeof(ProgressBar))]
    [TestCase(typeof(WatchCrown), typeof(Slider))]
    [TestCase(typeof(WatchImage), typeof(Label))]
    [TestCase(typeof(WatchTouchpad), typeof(Label))]
    [TestCase(typeof(WatchGestureArea), typeof(Label))]
    [TestCase(typeof(WatchJoystick), typeof(Label))]
    [TestCase(typeof(WatchMotion), typeof(Label))]
    [TestCase(typeof(WatchMicrophone), typeof(Label))]
    [TestCase(typeof(WatchStatus), typeof(Label))]
    public void ControlsRenderNativePreviewBodies(Type watchType, Type nativeType)
    {
        var element = (WatchUiElement)Activator.CreateInstance(watchType);
        Assert.That(element.Query<VisualElement>().ToList().Any(child => child != element && nativeType.IsInstanceOfType(child)),
            Is.True, watchType.Name + " must be visible and editable in UI Builder, not an empty metadata element.");
    }

    [Test]
    public void LabelChangesUpdateExistingPreviewWithoutReplacingFocusedControls()
    {
        var element = new WatchButton { Label = "Before" };
        var button = element.Q<Button>();
        Assert.That(button, Is.Not.Null);
        element.Label = "After";
        Assert.That(element.Q<Button>(), Is.SameAs(button));
        Assert.That(button.text, Is.EqualTo("After"));
        element.EnabledControl = false;
        Assert.That(button.enabledInHierarchy, Is.False);
    }

    [Test]
    public void NavigationCustomLabelLivesInsideTheClickableBody()
    {
        var link = new WatchNavigationLink { Title = "Fallback", Route = "details" };
        var label = new WatchHStack();
        label.Add(new WatchText { Label = "Details" });
        link.Add(label);
        Refresh(link);
        var button = link.Q<Button>();
        Assert.That(label.hierarchy.parent, Is.SameAs(button), "The entire authored navigation label must be clickable.");
        Assert.That(button.text, Is.Empty);
        CollectionAssert.AreEqual(new VisualElement[] { label }, Authored(link));
    }

    [UnityTest]
    public IEnumerator AuthoredButtonPaddingIsPartOfItsClickableSurface()
    {
        var element = new WatchButton { Action = "primary" };
        SetPaddedSurface(element);
        var activations = 0;
        element.PreviewAction += _ => activations++;
        yield return Mount(element);
        var point = new Vector2(element.worldBound.xMin + 2, element.worldBound.center.y);
        Assert.That(element.Q<Button>().worldBound.Contains(point), Is.False, "The fixture must click authored padding, not the inner button.");
        yield return ClickAt(element, point);
        Assert.That(element.LastPreviewAction, Is.EqualTo("primary"));
        Assert.That(activations, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ClickingNestedButtonLabelActivatesExactlyOnce()
    {
        var element = new WatchButton { Action = "primary" };
        SetPaddedSurface(element);
        var row = new WatchHStack();
        var text = new WatchText { Label = "Press" };
        row.Add(text);
        element.Add(row);
        var activations = 0;
        element.PreviewAction += _ => activations++;
        yield return Mount(element);
        yield return ClickAt(text.Q<Label>(), text.worldBound.center);
        Assert.That(activations, Is.EqualTo(1));
        Assert.That(element.LastPreviewAction, Is.EqualTo("primary"));
    }

    [UnityTest]
    public IEnumerator AuthoredNavigationPaddingOpensTheRoute()
    {
        var root = new WatchNavigationStack();
        var link = new WatchNavigationLink { Title = "Details", Route = "details" };
        SetPaddedSurface(link);
        root.Add(link);
        root.Add(new WatchNavigationDestination { Route = "details" });
        yield return Mount(root);
        var point = new Vector2(link.worldBound.xMin + 2, link.worldBound.center.y);
        Assert.That(link.Q<Button>().worldBound.Contains(point), Is.False);
        yield return ClickAt(link, point);
        Assert.That(root.CurrentRoute, Is.EqualTo("details"));
    }

    [Test]
    public void RemovingImageForegroundOverrideDoesNotLeaveThePreviousTint()
    {
        var element = new WatchImage { SystemName = "wifi", ForegroundColor = "#0000ff" };
        var image = element.Q<Image>();
        Assert.That(image.tintColor, Is.EqualTo(Color.blue));
        element.ForegroundColor = string.Empty;
        Assert.That(image.tintColor, Is.Not.EqualTo(Color.blue));
    }

    [UnityTest]
    public IEnumerator ToggleInteractionUpdatesSemanticValue()
    {
        var element = new WatchToggle();
        yield return Mount(element);
        var toggle = element.Q<Toggle>();
        Assert.That(toggle, Is.Not.Null);
        toggle.value = true;
        Assert.That(element.Value, Is.True);
        element.Value = false;
        Assert.That(toggle.value, Is.False);
    }

    [UnityTest]
    public IEnumerator SliderInteractionHonorsStepAndUpdatesSemanticValue()
    {
        var element = new WatchSlider { Minimum = 0, Maximum = 10, Step = 2, Value = 0 };
        yield return Mount(element);
        var slider = element.Q<Slider>();
        Assert.That(slider, Is.Not.Null);
        slider.value = 7.2f;
        Assert.That(element.Value, Is.EqualTo(8));
        element.Value = 4;
        Assert.That(slider.value, Is.EqualTo(4));
    }

    [UnityTest]
    public IEnumerator TextInputKeepsBindingIdentifierAndEditableTextSeparate()
    {
        var element = new WatchTextField { Title = "Player", TextBinding = "playerName", Prompt = "Your name" };
        yield return Mount(element);
        var field = element.Q<TextField>();
        Assert.That(field, Is.Not.Null);
        Assert.That(field.label, Is.EqualTo("Player"));
        field.value = "Ada";
        Assert.That(element.TextBinding, Is.EqualTo("playerName"));
        Assert.That(element.GetType().GetProperty("PreviewText")?.GetValue(element), Is.EqualTo("Ada"));
    }

    [UnityTest]
    public IEnumerator PickerMapsDisplayLabelsBackToAuthoredOptionValues()
    {
        var element = new WatchPicker { Selection = "easy" };
        var easy = new WatchPickerOption { Value = "easy", Label = "Easy mode" };
        var hard = new WatchPickerOption { Value = "hard", Label = "Hard mode" };
        element.Add(easy);
        element.Add(hard);
        yield return Mount(element);
        Refresh(element);
        var picker = element.Q<PopupField<string>>();
        Assert.That(picker, Is.Not.Null);
        CollectionAssert.AreEqual(new[] { "Easy mode", "Hard mode" }, picker.choices);
        picker.value = "Hard mode";
        Assert.That(element.Selection, Is.EqualTo("hard"));
        CollectionAssert.AreEqual(new VisualElement[] { easy, hard }, Authored(element));
    }

    [Test]
    public void StackSpacingDoesNotChangeAuthoredChildMarginsOrExportDecoration()
    {
        var stack = new WatchHStack { Spacing = 12 };
        var first = new WatchText { Label = "One" };
        var second = new WatchText { Label = "Two" };
        first.style.marginRight = 3;
        stack.Add(first);
        stack.Add(second);
        Refresh(stack);
        CollectionAssert.AreEqual(new VisualElement[] { first, second }, Authored(stack));
        Assert.That(first.style.marginRight.value.value, Is.EqualTo(3));
        var spacers = stack.Query<VisualElement>(className: "watch-preview-gap").ToList();
        Assert.That(spacers.Count, Is.EqualTo(1));
        Assert.That(spacers[0].style.width.value.value, Is.EqualTo(12));
        Refresh(stack);
        Assert.That(stack.Query<VisualElement>(className: "watch-preview-gap").ToList().Count, Is.EqualTo(1));
    }

    [Test]
    public void ClearingSemanticAlignmentRestoresStylesheetControl()
    {
        var stack = new WatchVStack { Alignment = "leading" };
        Assert.That(stack.style.alignItems.value, Is.EqualTo(Align.FlexStart));
        stack.Alignment = string.Empty;
        Assert.That(stack.style.alignItems.keyword, Is.EqualTo(StyleKeyword.Null));
    }

    [Test]
    public void ScrollViewKeepsAuthoredChildrenInItsActualViewport()
    {
        var element = new WatchScrollView { Axes = "horizontal", ShowsIndicators = false };
        var text = new WatchText { Label = "Scrollable" };
        element.Add(text);
        Refresh(element);
        var scroll = element.Q<ScrollView>();
        Assert.That(scroll, Is.Not.Null);
        Assert.That(text.hierarchy.parent, Is.SameAs(scroll.contentContainer));
        Assert.That(scroll.mode, Is.EqualTo(ScrollViewMode.Horizontal));
        Assert.That(scroll.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
        CollectionAssert.AreEqual(new VisualElement[] { text }, Authored(element));
    }

    [TestCase("vertical", true, ScrollerVisibility.Hidden, ScrollerVisibility.Auto)]
    [TestCase("horizontal", true, ScrollerVisibility.Auto, ScrollerVisibility.Hidden)]
    [TestCase("both", true, ScrollerVisibility.Auto, ScrollerVisibility.Auto)]
    [TestCase("vertical", false, ScrollerVisibility.Hidden, ScrollerVisibility.Hidden)]
    public void ScrollIndicatorsRespectTheAuthoredAxes(string axes, bool indicators,
        ScrollerVisibility horizontal, ScrollerVisibility vertical)
    {
        var element = new WatchScrollView { Axes = axes, ShowsIndicators = indicators };
        var scroll = element.Q<ScrollView>();
        Assert.That(scroll.horizontalScrollerVisibility, Is.EqualTo(horizontal));
        Assert.That(scroll.verticalScrollerVisibility, Is.EqualTo(vertical));
    }

    [UnityTest]
    public IEnumerator VerticalScrollKeepsFlexibleNavigationContentInsideItsViewport()
    {
        var element = new WatchScrollView { Axes = "vertical" };
        var page = new WatchVStack { Alignment = "leading", Spacing = 8 };
        var link = new WatchNavigationLink();
        link.style.alignSelf = Align.Stretch;
        link.style.paddingLeft = 10;
        link.style.paddingRight = 10;
        var row = new WatchHStack { Spacing = 9, Alignment = "center" };
        var labels = new WatchVStack { Alignment = "leading" };
        labels.Add(new WatchText { Label = "A deliberately long navigation subtitle that should wrap inside the screen" });
        row.Add(labels);
        row.Add(new WatchSpacer { MinLength = 0 });
        var chevron = new WatchImage { SystemName = "chevron.right" };
        chevron.style.width = 10;
        chevron.style.height = 10;
        row.Add(chevron);
        link.Add(row);
        page.Add(link);
        element.Add(page);
        yield return Mount(element);
        Refresh(page);
        Refresh(link);
        Refresh(row);
        yield return null;
        yield return null;
        var scroll = element.Q<ScrollView>();
        Assert.That(scroll.contentContainer.resolvedStyle.width,
            Is.LessThanOrEqualTo(scroll.contentViewport.resolvedStyle.width + 0.1f));
        Assert.That(chevron.worldBound.xMax, Is.LessThanOrEqualTo(scroll.contentViewport.worldBound.xMax + 0.1f));
        Assert.That(labels.resolvedStyle.height, Is.GreaterThan(20), "The long subtitle should wrap, not push the row beyond the viewport.");
    }

    [UnityTest]
    public IEnumerator MicrophoneSimulationFeedbackSurvivesPreviewRefresh()
    {
        yield return VerifySensorFeedback(new WatchMicrophone { Action = "clap" }, "simulated:clap");
    }

    [UnityTest]
    public IEnumerator MotionSimulationFeedbackSurvivesPreviewRefresh()
    {
        yield return VerifySensorFeedback(new WatchMotion { Action = "motion" }, "simulated:motion");
    }

    [Test]
    public void NavigationChangesRouteWithoutDestroyingAuthoredDestinations()
    {
        var stack = new WatchNavigationStack();
        var home = new WatchText { Label = "Home" };
        var destination = new WatchNavigationDestination { Route = "details" };
        destination.Add(new WatchText { Label = "Details" });
        stack.Add(home);
        stack.Add(destination);
        Refresh(stack);
        var navigate = typeof(WatchNavigationStack).GetMethod("NavigateTo");
        Assert.That(navigate, Is.Not.Null);
        Assert.That(navigate.Invoke(stack, new object[] { "details" }), Is.EqualTo(true));
        Assert.That(typeof(WatchNavigationStack).GetProperty("CurrentRoute")?.GetValue(stack), Is.EqualTo("details"));
        Assert.That(navigate.Invoke(stack, new object[] { "missing" }), Is.EqualTo(false));
        CollectionAssert.AreEqual(new VisualElement[] { home, destination }, Authored(stack));
        typeof(WatchNavigationStack).GetMethod("NavigateBack")?.Invoke(stack, null);
        Assert.That(typeof(WatchNavigationStack).GetProperty("CurrentRoute")?.GetValue(stack), Is.Empty);
        Assert.That(destination.GetFirstAncestorOfType<WatchNavigationStack>(), Is.SameAs(stack));
    }

    [TestCase(typeof(WatchMotion))]
    [TestCase(typeof(WatchMicrophone))]
    [TestCase(typeof(WatchStatus))]
    public void HardwarePreviewsDoNotPresentFabricatedDeviceReadings(Type watchType)
    {
        var element = (WatchUiElement)Activator.CreateInstance(watchType);
        var labels = string.Join(" ", element.Query<Label>().ToList().Select(label => label.text));
        Assert.That(labels.ToLowerInvariant(), Does.Contain("unavailable"));
    }

    [Test]
    public void UxmlDeserializationUpdatesNativePreviewProperties()
    {
        using var fixture = new WatchUiTestAssets("<watch:watch-v-stack>"
            + "<watch:watch-button watch-id=\"fire\" label=\"TAP\" />"
            + "<watch:watch-slider watch-id=\"volume\" value=\"0.55\" />"
            + "</watch:watch-v-stack>");
        var asset = fixture.Source;
        Assert.That(asset, Is.Not.Null);
        var root = asset.CloneTree();
        var fire = root.Query<WatchButton>().ToList().Single(element => element.WatchId == "fire");
        Assert.That(fire.Q<Button>(), Is.Not.Null);
        Assert.That(fire.Q<Button>().text, Is.EqualTo("TAP"));
        var volume = root.Query<WatchSlider>().ToList().Single(element => element.WatchId == "volume");
        Assert.That(volume.Q<Slider>().value, Is.EqualTo(0.55f).Within(0.001f));
    }

    private IEnumerator Mount(WatchUiElement element)
    {
        window = ScriptableObject.CreateInstance<PreviewTestWindow>();
        window.Show();
        window.rootVisualElement.style.width = 211;
        window.rootVisualElement.style.height = 257;
        window.rootVisualElement.Add(element);
        yield return null;
        element.RefreshPreview();
        yield return null;
        Assert.That(element.panel, Is.Not.Null);
    }

    private IEnumerator VerifySensorFeedback(WatchUiElement element, string action)
    {
        yield return Mount(element);
        var button = element.Q<Button>();
        Assert.That(button, Is.Not.Null);
        window.Focus();
        button.Focus();
        yield return null;
        Assert.That(window.rootVisualElement.focusController.focusedElement, Is.SameAs(button));
        using (var submit = NavigationSubmitEvent.GetPooled())
            button.SendEvent(submit);
        Assert.That(element.LastPreviewAction, Is.EqualTo(action), "The real native button must invoke the simulation callback.");
        element.RefreshPreview();
        var status = string.Join(" ", element.Query<Label>().ToList().Select(label => label.text));
        Assert.That(status.ToLowerInvariant(), Does.Contain("simulated"));
        Assert.That(status.ToLowerInvariant(), Does.Contain("unavailable"));
    }

    private static void SetPaddedSurface(WatchUiElement element)
    {
        element.style.width = 180;
        element.style.height = 64;
        element.style.paddingLeft = 12;
        element.style.paddingRight = 12;
        element.style.paddingTop = 12;
        element.style.paddingBottom = 12;
    }

    private static IEnumerator ClickAt(VisualElement target, Vector2 point)
    {
        using (var down = PointerDownEvent.GetPooled(new Event
               { type = EventType.MouseDown, button = 0, clickCount = 1, mousePosition = point }))
        {
            down.target = target;
            target.SendEvent(down);
        }
        yield return null;
        using (var up = PointerUpEvent.GetPooled(new Event
               { type = EventType.MouseUp, button = 0, clickCount = 1, mousePosition = point }))
        {
            up.target = target;
            target.SendEvent(up);
        }
        yield return null;
    }

    private static void Refresh(WatchUiElement element)
    {
        var method = typeof(WatchUiElement).GetMethod("RefreshPreview", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, "WatchUiElement must expose the preview refresh contract.");
        method.Invoke(element, null);
    }

    private static IEnumerable<VisualElement> Authored(WatchUiElement element)
    {
        var method = typeof(WatchUiElement).GetMethod("GetAuthoredChildren", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, "Export must not confuse native preview controls with authored UXML children.");
        return (IEnumerable<VisualElement>)method.Invoke(element, null);
    }
}
