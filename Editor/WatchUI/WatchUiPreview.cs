using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WatchUI
{
    /// <summary>Builds local UI Toolkit visuals and interaction feedback for one authored Watch control.</summary>
    /// <remarks>
    /// This preview does not send WatchConnectivity messages or Input System events.
    /// Internal controls are marked so export can recover only authored children.
    /// Hardware sensors and native watchOS rendering require separate validation.
    /// </remarks>
    internal sealed class WatchUiPreview
    {
        /// <summary>Marks preview-only controls that authored-child traversal must exclude.</summary>
        internal const string InternalClass = "watch-preview-internal";
        /// <summary>Marks preview wrappers whose authored children must be unwrapped during export.</summary>
        internal const string OverlaySlotClass = "watch-preview-overlay-slot";
        private const string HiddenClass = "watch-preview-hidden";
        private static StyleSheet styleSheet;
        private readonly WatchUiElement owner;
        private readonly List<VisualElement> gaps = new List<VisualElement>();
        private readonly List<WatchPickerOption> options = new List<WatchPickerOption>();
        private readonly List<TextElement> nativeText = new List<TextElement>();
        private readonly Stack<string> routes = new Stack<string>();
        private Label caption;
        private Label detail;
        private Image image;
        private Button button;
        private Button decrease;
        private Button increase;
        private Toggle toggle;
        private Slider slider;
        private TextField textField;
        private PopupField<string> picker;
        private ProgressBar progress;
        private ScrollView scroll;
        private VisualElement surface;
        private VisualElement thumb;
        private VisualElement navigationBar;
        private bool refreshing;
        private string loadedImagePath;
        private string interactionFeedback;
        private float gesturePeakDistance;
        private bool longPressRecognized;
        private bool longPressCancelled;
        private bool hasStyleSnapshot;
        private Color previousColor;
        private float previousFontSize;
        private FontStyle previousFontStyle;
        private TextAnchor previousTextAlignment;

        /// <summary>The visual container that accepts children added through the owner's public API.</summary>
        internal VisualElement ContentContainer { get; private set; }
        /// <summary>The local navigation route, or empty for root content.</summary>
        internal string CurrentRoute { get; private set; } = string.Empty;

        /// <summary>Creates control-specific preview visuals without replacing authored children.</summary>
        /// <param name="owner">The non-null Watch control whose values drive the preview.</param>
        internal WatchUiPreview(WatchUiElement owner)
        {
            this.owner = owner;
            ContentContainer = owner;
            if (styleSheet == null)
                styleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    AppleWatchController.Editor.WatchPackagePaths.AssetPath("Editor/WatchUI/Resources/WatchUiPreview.uss"))
                    ?? Resources.Load<StyleSheet>("WatchUiPreview");
            if (styleSheet != null)
                owner.styleSheets.Add(styleSheet);

            switch (owner)
            {
                case WatchText _:
                case WatchPickerOption _:
                    caption = Add(new Label(), "watch-preview-text");
                    break;
                case WatchLabel _:
                case WatchImage _:
                    var imageRow = Add(new VisualElement(), "watch-preview-image-row");
                    image = Add(new Image(), "watch-preview-image", imageRow);
                    caption = Add(new Label(), "watch-preview-text", imageRow);
                    break;
                case WatchButton _:
                    button = Add(new Button(() => Report(((WatchButton)owner).Action)), "watch-preview-button");
                    ContentContainer = button;
                    break;
                case WatchNavigationLink _:
                    button = Add(new Button(() =>
                    {
                        var link = (WatchNavigationLink)owner;
                        var stack = link.GetFirstAncestorOfType<WatchNavigationStack>();
                        if (stack == null || !stack.NavigateTo(link.Route))
                            Report("Unavailable route: " + link.Route);
                    }), "watch-preview-button");
                    ContentContainer = button;
                    break;
                case WatchToggle _:
                    toggle = Add(new Toggle(), "watch-preview-toggle");
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        ((WatchToggle)owner).Value = evt.newValue;
                        Report(((WatchToggle)owner).Action);
                    });
                    break;
                case WatchSlider _:
                case WatchCrown _:
                    slider = Add(new Slider(), "watch-preview-slider");
                    detail = Add(new Label(), "watch-preview-detail");
                    slider.RegisterValueChangedCallback(OnSliderChanged);
                    break;
                case WatchStepper _:
                    var stepper = Add(new VisualElement(), "watch-preview-stepper");
                    caption = Add(new Label(), "watch-preview-text", stepper);
                    decrease = Add(new Button(() => Step(-1)) { text = "−" }, "watch-preview-step-button", stepper);
                    detail = Add(new Label(), "watch-preview-value", stepper);
                    increase = Add(new Button(() => Step(1)) { text = "+" }, "watch-preview-step-button", stepper);
                    break;
                case WatchTextField _:
                    textField = Add(new TextField(), "watch-preview-textfield");
                    textField.RegisterValueChangedCallback(evt => ((WatchTextField)owner).PreviewText = evt.newValue);
                    textField.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                            Report(((WatchTextField)owner).SubmitAction);
                    });
                    break;
                case WatchPicker _:
                    picker = Add(new PopupField<string>(), "watch-preview-picker");
                    picker.RegisterValueChangedCallback(evt =>
                    {
                        var index = picker.choices.IndexOf(evt.newValue);
                        if (index >= 0 && index < options.Count)
                        {
                            ((WatchPicker)owner).Selection = options[index].Value;
                            Report(((WatchPicker)owner).Action);
                        }
                    });
                    ContentContainer = Add(new VisualElement(), "watch-preview-options");
                    break;
                case WatchProgressView _:
                case WatchGauge _:
                    progress = Add(new ProgressBar(), "watch-preview-progress");
                    break;
                case WatchScrollView _:
                case WatchList _:
                    scroll = Add(new ScrollView(), "watch-preview-scroll");
                    ContentContainer = scroll.contentContainer;
                    ContentContainer.AddToClassList("watch-preview-scroll-content");
                    break;
                case WatchNavigationStack _:
                    navigationBar = Add(new VisualElement(), "watch-preview-navigation-bar");
                    button = Add(new Button(() => NavigateBack()) { text = "Back" }, "watch-preview-back", navigationBar);
                    caption = Add(new Label(), "watch-preview-route", navigationBar);
                    ContentContainer = Add(new VisualElement(), "watch-preview-navigation-content");
                    break;
                case WatchTouchpad _:
                case WatchGestureArea _:
                case WatchJoystick _:
                    caption = Add(new Label(), "watch-preview-text");
                    surface = Add(new VisualElement(), "watch-preview-surface");
                    if (owner is WatchJoystick)
                    {
                        surface.AddToClassList("watch-preview-joystick");
                        thumb = Add(new VisualElement(), "watch-preview-thumb", surface);
                    }
                    detail = Add(new Label(), "watch-preview-detail");
                    surface.AddManipulator(new WatchPreviewPointer(OnPointer, owner is WatchGestureArea));
                    break;
                case WatchMotion _:
                case WatchMicrophone _:
                case WatchStatus _:
                    caption = Add(new Label(), "watch-preview-text");
                    detail = Add(new Label(), "watch-preview-detail");
                    if (!(owner is WatchStatus))
                        button = Add(new Button(SimulateSensor), "watch-preview-simulation");
                    break;
                case WatchDivider _:
                    Add(new VisualElement(), "watch-preview-divider");
                    break;
                case WatchSpacer _:
                    Add(new VisualElement(), "watch-preview-spacer");
                    break;
            }

            if (owner is WatchButton || owner is WatchNavigationLink)
                owner.AddManipulator(button.clickable);
        }

        /// <summary>Applies current authoring values to local preview controls without recursively refreshing.</summary>
        internal void Refresh()
        {
            if (refreshing)
                return;
            refreshing = true;
            try
            {
                owner.SetEnabled(owner.EnabledControl);
                switch (owner)
                {
                    case WatchText text:
                        caption.text = text.Label;
                        break;
                    case WatchLabel label:
                        RefreshImage(label.SystemImage, string.Empty, label.Title, true);
                        break;
                    case WatchImage watchImage:
                        RefreshImage(watchImage.SystemName, First(watchImage.Source, watchImage.Name), string.Empty, false);
                        break;
                    case WatchButton watchButton:
                        button.text = owner.GetAuthoredChildren().Any() ? string.Empty : watchButton.Label;
                        break;
                    case WatchNavigationLink link:
                        button.text = owner.GetAuthoredChildren().Any() ? string.Empty : link.Title;
                        break;
                    case WatchToggle watchToggle:
                        toggle.label = watchToggle.Label;
                        toggle.SetValueWithoutNotify(watchToggle.Value);
                        break;
                    case WatchSlider watchSlider:
                        slider.label = watchSlider.Label;
                        slider.lowValue = watchSlider.Minimum;
                        slider.highValue = Mathf.Max(watchSlider.Minimum, watchSlider.Maximum);
                        slider.SetValueWithoutNotify(watchSlider.Value);
                        detail.text = Number(watchSlider.Value);
                        break;
                    case WatchCrown crown:
                        slider.label = crown.Label;
                        slider.lowValue = -1;
                        slider.highValue = 1;
                        detail.text = "Editor simulation · " + Number(slider.value);
                        break;
                    case WatchStepper stepper:
                        caption.text = stepper.Label;
                        detail.text = Number(stepper.Value);
                        decrease.SetEnabled(stepper.Value > stepper.Minimum);
                        increase.SetEnabled(stepper.Value < stepper.Maximum);
                        break;
                    case WatchTextField field:
                        textField.label = field.Title;
                        textField.textEdition.placeholder = field.Prompt;
                        textField.SetValueWithoutNotify(field.PreviewText);
                        break;
                    case WatchPicker watchPicker:
                        RefreshPicker(watchPicker);
                        break;
                    case WatchPickerOption option:
                        caption.text = First(option.Label, option.Value);
                        break;
                    case WatchProgressView watchProgress:
                        progress.lowValue = 0;
                        progress.highValue = Mathf.Max(0.0001f, watchProgress.Total);
                        progress.value = float.IsNaN(watchProgress.Value) ? 0 : watchProgress.Value;
                        progress.title = watchProgress.Label + (float.IsNaN(watchProgress.Value) ? " · waiting" : string.Empty);
                        break;
                    case WatchGauge gauge:
                        progress.lowValue = gauge.Minimum;
                        progress.highValue = Mathf.Max(gauge.Minimum + 0.0001f, gauge.Maximum);
                        progress.value = gauge.Value;
                        progress.title = gauge.Label + "  " + First(gauge.CurrentValueLabel, Number(gauge.Value));
                        break;
                    case WatchScrollView watchScroll:
                        scroll.mode = watchScroll.Axes == "horizontal" ? ScrollViewMode.Horizontal :
                            watchScroll.Axes == "both" ? ScrollViewMode.VerticalAndHorizontal : ScrollViewMode.Vertical;
                        scroll.horizontalScrollerVisibility = watchScroll.ShowsIndicators && scroll.mode != ScrollViewMode.Vertical
                            ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
                        scroll.verticalScrollerVisibility = watchScroll.ShowsIndicators && scroll.mode != ScrollViewMode.Horizontal
                            ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
                        break;
                    case WatchList list:
                        scroll.mode = ScrollViewMode.Vertical;
                        scroll.EnableInClassList("watch-preview-list-bordered", list.Style != "plain");
                        break;
                    case WatchVStack vertical:
                        LayoutStack(false, vertical.Spacing, vertical.Alignment);
                        break;
                    case WatchHStack horizontal:
                        LayoutStack(true, horizontal.Spacing, horizontal.Alignment);
                        break;
                    case WatchGrid grid:
                        LayoutStack(false, grid.VerticalSpacing, grid.Alignment);
                        foreach (var gridRow in owner.GetAuthoredChildren().OfType<WatchGridRow>())
                            gridRow.RefreshPreview();
                        break;
                    case WatchGridRow row:
                        LayoutStack(true, row.GetFirstAncestorOfType<WatchGrid>()?.HorizontalSpacing ?? 8, row.Alignment);
                        break;
                    case WatchZStack overlay:
                        LayoutOverlay(overlay.Alignment);
                        break;
                    case WatchNavigationStack _:
                        RefreshNavigation();
                        break;
                    case WatchSpacer spacer:
                        var filler = owner.hierarchy[0];
                        var parent = owner.GetFirstAncestorOfType<WatchUiElement>();
                        var horizontalSpacer = parent is WatchHStack || parent is WatchGridRow;
                        filler.style.minWidth = horizontalSpacer ? Mathf.Max(0, spacer.MinLength) : 0;
                        filler.style.minHeight = horizontalSpacer ? 0 : Mathf.Max(0, spacer.MinLength);
                        break;
                    case WatchTouchpad pad:
                        caption.text = pad.Label;
                        if (string.IsNullOrEmpty(detail.text)) detail.text = "Editor simulation · drag to test";
                        break;
                    case WatchGestureArea gesture:
                        caption.text = gesture.Label;
                        detail.text = interactionFeedback ?? "Editor simulation · " + gesture.Gesture;
                        break;
                    case WatchJoystick joystick:
                        caption.text = joystick.Label;
                        surface.style.width = Mathf.Max(1, joystick.Radius) * 2;
                        surface.style.height = Mathf.Max(1, joystick.Radius) * 2;
                        if (string.IsNullOrEmpty(detail.text)) detail.text = "Editor simulation · drag to test";
                        break;
                    case WatchMotion motion:
                        caption.text = motion.Label;
                        detail.text = interactionFeedback ?? "Watch motion unavailable in Editor";
                        button.text = "Simulate motion";
                        break;
                    case WatchMicrophone microphone:
                        caption.text = microphone.Label;
                        detail.text = interactionFeedback ?? "Watch microphone unavailable in Editor";
                        button.text = "Simulate clap";
                        break;
                    case WatchStatus status:
                        caption.text = status.Label;
                        detail.text = "Watch connection unavailable in Editor";
                        break;
                }
                SynchronizeStyle();
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>Copies resolved owner typography and tint into preview-only text and image controls.</summary>
        /// <remarks>Does nothing while detached. This is visual synchronization, not an export style snapshot.</remarks>
        internal void SynchronizeStyle()
        {
            if (owner.panel == null)
                return;

            var style = owner.resolvedStyle;
            if (!hasStyleSnapshot || previousColor != style.color || previousFontSize != style.fontSize ||
                previousFontStyle != style.unityFontStyleAndWeight || previousTextAlignment != style.unityTextAlign)
            {
                previousColor = style.color;
                previousFontSize = style.fontSize;
                previousFontStyle = style.unityFontStyleAndWeight;
                previousTextAlignment = style.unityTextAlign;
                hasStyleSnapshot = true;
                foreach (var text in nativeText)
                {
                    text.style.color = previousColor;
                    if (!text.ClassListContains("watch-preview-detail") && previousFontSize > 0)
                        text.style.fontSize = previousFontSize;
                    text.style.unityFontStyleAndWeight = previousFontStyle;
                    if (!(text is Button))
                        text.style.unityTextAlign = previousTextAlignment;
                }
            }
            SynchronizeImageTint();
        }

        private void SynchronizeImageTint()
        {
            if (image == null)
                return;
            var color = owner.panel != null ? owner.resolvedStyle.color :
                owner.style.color.keyword == StyleKeyword.Undefined ? owner.style.color.value : Color.white;
            if (owner is WatchImage watchImage && ColorUtility.TryParseHtmlString(watchImage.ForegroundColor, out var explicitColor))
                color = explicitColor;
            image.tintColor = color;
        }

        /// <summary>Shows a direct child destination and records the previous local route.</summary>
        /// <param name="route">The exact destination route.</param>
        /// <returns>True for a matching route; false for a missing route or non-navigation owner.</returns>
        internal bool NavigateTo(string route)
        {
            if (!(owner is WatchNavigationStack) || string.IsNullOrWhiteSpace(route) ||
                !owner.GetAuthoredChildren().OfType<WatchNavigationDestination>().Any(destination => destination.Route == route))
                return false;
            if (CurrentRoute == route)
                return true;
            routes.Push(CurrentRoute);
            CurrentRoute = route;
            Refresh();
            return true;
        }

        /// <summary>Restores the previous local route.</summary>
        /// <returns>True if a saved route was restored; otherwise false.</returns>
        internal bool NavigateBack()
        {
            if (routes.Count == 0)
                return false;
            CurrentRoute = routes.Pop();
            Refresh();
            return true;
        }

        private void RefreshNavigation()
        {
            var navigated = !string.IsNullOrEmpty(CurrentRoute);
            navigationBar.EnableInClassList(HiddenClass, !navigated);
            caption.text = CurrentRoute;
            caption.tooltip = CurrentRoute;
            foreach (var child in owner.GetAuthoredChildren())
            {
                var destination = child as WatchNavigationDestination;
                var visible = destination != null ? destination.Route == CurrentRoute && navigated : !navigated;
                child.EnableInClassList(HiddenClass, !visible);
            }
        }

        private void RefreshPicker(WatchPicker watchPicker)
        {
            options.Clear();
            options.AddRange(owner.GetAuthoredChildren().OfType<WatchPickerOption>());
            var labels = options.Select(option => First(option.Label, option.Value)).ToList();
            if (!picker.choices.SequenceEqual(labels))
                picker.choices = labels;
            picker.label = watchPicker.Label;
            var index = options.FindIndex(option => option.Value == watchPicker.Selection);
            picker.SetValueWithoutNotify(index >= 0 ? labels[index] : string.Empty);
            picker.SetEnabled(options.Count > 0);
        }

        private void RefreshImage(string symbol, string path, string title, bool hasTitle)
        {
            var key = (string.IsNullOrEmpty(symbol) ? path : "WatchUiSymbols/" + symbol) ?? string.Empty;
            if (key != loadedImagePath || image.image == null)
            {
                image.image = string.IsNullOrEmpty(key) ? null : (key.StartsWith("Assets/", StringComparison.Ordinal)
                    || key.StartsWith("Packages/", StringComparison.Ordinal))
                    ? AssetDatabase.LoadAssetAtPath<Texture2D>(key)
                    : Resources.Load<Texture2D>(key);
                loadedImagePath = key;
            }
            image.EnableInClassList(HiddenClass, image.image == null);
            caption.text = image.image != null ? title : First(title, "Unsupported image: " + First(symbol, path));
            caption.EnableInClassList(HiddenClass, !hasTitle && image.image != null);
            image.tooltip = First(symbol, path);
            SynchronizeImageTint();
        }

        private void LayoutStack(bool horizontal, float spacing, string alignment)
        {
            ContentContainer.style.flexDirection = horizontal ? FlexDirection.Row : FlexDirection.Column;
            if (!string.IsNullOrEmpty(alignment))
                ContentContainer.style.alignItems = Alignment(alignment);
            else
                ContentContainer.style.alignItems = StyleKeyword.Null;
            var children = owner.GetAuthoredChildren().ToList();
            var gapCount = Mathf.Max(0, children.Count - 1);
            while (gaps.Count > gapCount)
            {
                gaps[gaps.Count - 1].RemoveFromHierarchy();
                gaps.RemoveAt(gaps.Count - 1);
            }
            while (gaps.Count < gapCount)
                gaps.Add(Add(new VisualElement { pickingMode = PickingMode.Ignore }, "watch-preview-gap", ContentContainer));
            for (var index = 0; index < gaps.Count; index++)
            {
                var gap = gaps[index];
                var targetIndex = ContentContainer.hierarchy.IndexOf(children[index]) + 1;
                if (ContentContainer.hierarchy.IndexOf(gap) != targetIndex)
                {
                    gap.RemoveFromHierarchy();
                    targetIndex = ContentContainer.hierarchy.IndexOf(children[index]) + 1;
                    ContentContainer.hierarchy.Insert(targetIndex, gap);
                }
                gap.style.width = horizontal ? new StyleLength(Mathf.Max(0, spacing < 0 ? 8 : spacing)) : new StyleLength(0f);
                gap.style.height = horizontal ? new StyleLength(0f) : new StyleLength(Mathf.Max(0, spacing < 0 ? 8 : spacing));
            }
        }

        private void LayoutOverlay(string alignment)
        {
            foreach (var child in owner.GetAuthoredChildren().ToList())
            {
                if (child.parent != null && child.parent.ClassListContains(OverlaySlotClass))
                    continue;
                var index = owner.hierarchy.IndexOf(child);
                child.RemoveFromHierarchy();
                var wrapper = new VisualElement { pickingMode = PickingMode.Ignore };
                wrapper.AddToClassList(InternalClass);
                wrapper.AddToClassList(OverlaySlotClass);
                owner.hierarchy.Insert(index, wrapper);
                wrapper.Add(child);
            }
            for (var index = 0; index < owner.hierarchy.childCount; index++)
            {
                var wrapper = owner.hierarchy[index];
                wrapper.EnableInClassList("watch-preview-overlay-layer", index > 0);
                wrapper.style.alignItems = Alignment(alignment);
                wrapper.style.justifyContent = alignment != null && alignment.Contains("top") ? Justify.FlexStart :
                    alignment != null && alignment.Contains("bottom") ? Justify.FlexEnd : Justify.Center;
            }
        }

        private void OnSliderChanged(ChangeEvent<float> evt)
        {
            if (owner is WatchSlider watchSlider)
            {
                var value = evt.newValue;
                if (watchSlider.Step > 0)
                    value = watchSlider.Minimum + Mathf.Round((value - watchSlider.Minimum) / watchSlider.Step) * watchSlider.Step;
                watchSlider.Value = Mathf.Clamp(value, watchSlider.Minimum, Mathf.Max(watchSlider.Minimum, watchSlider.Maximum));
                slider.SetValueWithoutNotify(watchSlider.Value);
                Report(watchSlider.Action);
            }
            else if (owner is WatchCrown crown)
            {
                detail.text = "Editor simulation · " + Number(evt.newValue);
                Report(crown.Action);
            }
        }

        private void Step(int direction)
        {
            var stepper = (WatchStepper)owner;
            stepper.Value = Mathf.Clamp(stepper.Value + direction * Mathf.Max(0.0001f, Mathf.Abs(stepper.Step)),
                stepper.Minimum, Mathf.Max(stepper.Minimum, stepper.Maximum));
            Report(stepper.Action);
        }

        private void SimulateSensor()
        {
            if (owner is WatchMotion motion)
            {
                interactionFeedback = "Simulated motion · Watch hardware unavailable";
                detail.text = interactionFeedback;
                Report("simulated:" + motion.Action);
            }
            else if (owner is WatchMicrophone microphone)
            {
                interactionFeedback = "Simulated clap · Watch microphone unavailable";
                detail.text = interactionFeedback;
                Report("simulated:" + microphone.Action);
            }
        }

        private void OnPointer(Vector2 start, Vector2 current, string phase, double duration, int clicks)
        {
            var delta = current - start;
            if (owner is WatchGestureArea gesture)
            {
                if (phase == "begin")
                {
                    gesturePeakDistance = 0;
                    longPressRecognized = false;
                    longPressCancelled = false;
                }
                gesturePeakDistance = Mathf.Max(gesturePeakDistance, delta.magnitude);
                if (phase == "cancel")
                {
                    longPressCancelled = true;
                    return;
                }
                var kind = (gesture.Gesture ?? "tap").Trim().ToLowerInvariant();
                if (kind == "long-press" || kind == "longpress")
                {
                    var maximumDistance = gesture.MaximumDistance;
                    if (float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance < 0)
                        maximumDistance = 20;
                    longPressCancelled |= gesturePeakDistance > maximumDistance;
                    if (!longPressRecognized && !longPressCancelled && owner.enabledInHierarchy
                        && duration >= gesture.MinimumDuration && phase != "begin")
                    {
                        longPressRecognized = true;
                        interactionFeedback = "Simulated long-press recognized";
                        detail.text = interactionFeedback;
                        Report(gesture.Action);
                    }
                    return;
                }
                if (phase != "end")
                    return;
                var distance = delta.magnitude;
                if (kind == "swipe")
                {
                    if (delta == Vector2.zero || Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < gesture.MinimumDistance)
                        return;
                    kind = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                        ? delta.x < 0 ? "swipe-left" : "swipe-right"
                        : delta.y < 0 ? "swipe-up" : "swipe-down";
                    interactionFeedback = "Simulated " + kind + " recognized";
                    detail.text = interactionFeedback;
                    Report(gesture.Action);
                    return;
                }
                var triggered = kind == "tap" && distance < gesture.MinimumDistance ||
                    kind == "double-tap" && clicks >= 2 && distance < gesture.MinimumDistance ||
                    kind == "drag" && distance >= gesture.MinimumDistance ||
                    kind == "swipe-left" && -delta.x >= gesture.MinimumDistance && Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ||
                    kind == "swipe-right" && delta.x >= gesture.MinimumDistance && Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ||
                    kind == "swipe-up" && -delta.y >= gesture.MinimumDistance && Mathf.Abs(delta.y) > Mathf.Abs(delta.x) ||
                    kind == "swipe-down" && delta.y >= gesture.MinimumDistance && Mathf.Abs(delta.y) > Mathf.Abs(delta.x);
                if (triggered)
                {
                    interactionFeedback = "Simulated " + kind + " recognized";
                    detail.text = interactionFeedback;
                    Report(gesture.Action);
                }
                return;
            }

            var width = Mathf.Max(1, surface.contentRect.width);
            var height = Mathf.Max(1, surface.contentRect.height);
            var vector = new Vector2(Mathf.Clamp(current.x / width * 2 - 1, -1, 1), Mathf.Clamp(1 - current.y / height * 2, -1, 1));
            string action;
            if (owner is WatchJoystick joystick)
            {
                vector = Vector2.ClampMagnitude(vector, 1);
                if (vector.magnitude * joystick.Radius < joystick.DeadZone || phase == "cancel" || phase == "end" && joystick.SpringReturn)
                    vector = Vector2.zero;
                thumb.style.translate = new Translate(vector.x * joystick.Radius, -vector.y * joystick.Radius, 0);
                action = joystick.Action;
            }
            else
            {
                action = ((WatchTouchpad)owner).Action;
            }
            detail.text = "Simulated x " + Number(vector.x) + "  y " + Number(vector.y);
            surface.EnableInClassList("watch-preview-active", phase == "begin" || phase == "move");
            Report(action);
        }

        private void Report(string action)
        {
            if (owner.EnabledControl)
                owner.ReportPreviewAction(First(action, owner.WatchId));
        }

        private T Add<T>(T element, string className, VisualElement parent = null) where T : VisualElement
        {
            MarkInternal(element);
            element.AddToClassList("watch-preview-native");
            element.AddToClassList(className);
            (parent ?? owner).hierarchy.Add(element);
            return element;
        }

        private void MarkInternal(VisualElement element)
        {
            element.AddToClassList(InternalClass);
            if (element is TextElement text)
            {
                nativeText.Add(text);
                hasStyleSnapshot = false;
            }
            for (var index = 0; index < element.hierarchy.childCount; index++)
                MarkInternal(element.hierarchy[index]);
        }

        private static Align Alignment(string alignment)
        {
            if (string.IsNullOrEmpty(alignment)) return Align.Center;
            var value = alignment.ToLowerInvariant();
            if (value.Contains("leading") || value == "top") return Align.FlexStart;
            if (value.Contains("trailing") || value == "bottom") return Align.FlexEnd;
            return Align.Center;
        }

        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string First(string preferred, string fallback) => string.IsNullOrEmpty(preferred) ? fallback : preferred;
    }

    /// <summary>Tracks one captured pointer for local touch, joystick, and gesture previews.</summary>
    /// <remarks>Cancellation, capture loss, and detachment end tracking without producing a successful gesture.</remarks>
    internal sealed class WatchPreviewPointer : PointerManipulator
    {
        private readonly Action<Vector2, Vector2, string, double, int> report;
        private Vector2 start;
        private Vector2 current;
        private double beganAt;
        private int pointerId = -1;
        private int clicks;
        private readonly bool trackDuration;
        private IVisualElementScheduledItem durationTick;

        /// <summary>Creates a pointer tracker with optional timed hold reports.</summary>
        /// <param name="report">Required callback receiving local start/current points, phase, elapsed seconds, and click count.</param>
        /// <param name="trackDuration">Whether to report hold phases on a scheduled 16-millisecond tick while captured.</param>
        /// <remarks>Phases are begin, move, hold, end, and cancel. Positions use the target's local UI Toolkit coordinates.</remarks>
        internal WatchPreviewPointer(Action<Vector2, Vector2, string, double, int> report, bool trackDuration = false)
        {
            this.report = report;
            this.trackDuration = trackDuration;
        }

        /// <summary>Registers pointer and panel-lifecycle handlers on the target.</summary>
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove);
            target.RegisterCallback<PointerUpEvent>(OnUp);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.RegisterCallback<PointerCancelEvent>(OnCancel);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        /// <summary>Unregisters handlers and cancels any active pointer capture.</summary>
        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.UnregisterCallback<PointerCancelEvent>(OnCancel);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            Cancel();
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || pointerId >= 0 || !target.enabledInHierarchy)
                return;
            pointerId = evt.pointerId;
            start = current = target.WorldToLocal(evt.position);
            beganAt = Time.realtimeSinceStartupAsDouble;
            clicks = evt.clickCount;
            target.CapturePointer(pointerId);
            report(start, current, "begin", 0, clicks);
            if (trackDuration)
            {
                durationTick ??= target.schedule.Execute(TickDuration).Every(16);
                durationTick.Resume();
            }
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            current = target.WorldToLocal(evt.position);
            report(start, current, "move", Time.realtimeSinceStartupAsDouble - beganAt, clicks);
            evt.StopPropagation();
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            current = target.WorldToLocal(evt.position);
            var releasedId = pointerId;
            pointerId = -1;
            durationTick?.Pause();
            report(start, current, target.enabledInHierarchy ? "end" : "cancel", Time.realtimeSinceStartupAsDouble - beganAt, clicks);
            target.ReleasePointer(releasedId);
            evt.StopPropagation();
        }

        private void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            Cancel();
        }

        private void OnDetach(DetachFromPanelEvent evt) => Cancel();

        private void OnCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != pointerId)
                return;
            Cancel();
            evt.StopPropagation();
        }

        private void TickDuration()
        {
            if (pointerId < 0)
                return;
            if (target.panel == null || !target.enabledInHierarchy || !target.HasPointerCapture(pointerId))
            {
                Cancel();
                return;
            }
            report(start, current, "hold", Time.realtimeSinceStartupAsDouble - beganAt, clicks);
        }

        private void Cancel()
        {
            durationTick?.Pause();
            if (pointerId < 0)
                return;
            var cancelledId = pointerId;
            pointerId = -1;
            report(start, current, "cancel", Time.realtimeSinceStartupAsDouble - beganAt, clicks);
            if (target.HasPointerCapture(cancelledId))
                target.ReleasePointer(cancelledId);
        }
    }
}
