using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;

namespace WatchUI
{
    /// <summary>Converts the supported Watch authoring controls into export nodes and SwiftUI source.</summary>
    /// <remarks>
    /// This is not a general UI Toolkit converter. The root and every authored child must use a supported
    /// WatchUiElement type. Preview-only children are excluded. To export resolved USS presentation,
    /// attach the tree to a panel and let all authored branches finish layout before conversion.
    /// </remarks>
    public static class WatchUiElementConverter
    {
        /// <summary>Builds an export tree from supported authoring controls and captures available resolved styles.</summary>
        /// <param name="element">The authored Watch root. Null produces an error.</param>
        /// <param name="node">The resulting root, or null when the root cannot be converted. Errors may leave a partial tree.</param>
        /// <param name="diagnostics">A new collection of conversion errors and warnings.</param>
        /// <returns>True when a root was built without conversion errors; otherwise false.</returns>
        /// <remarks>
        /// A detached tree has no style snapshot. An attached tree also warns that USS width/height are not exported.
        /// This step does not perform all emitter validation. Use TryGenerateSwiftUI for full conversion and emission.
        /// Empty WatchId values receive per-kind fallback IDs in traversal order.
        /// </remarks>
        public static bool TryBuildNode(VisualElement element, out WatchUiNode node,
            out WatchUiConversionDiagnostics diagnostics)
        {
            diagnostics = new WatchUiConversionDiagnostics();
            if (element?.panel != null)
                diagnostics.Warnings.Add("USS width/height are not exported: public resolved styles cannot distinguish authored dimensions from auto layout. Use min/max size constraints or supported align-self: stretch instead.");
            var context = new WatchUiBuildContext();
            node = BuildNode(element, diagnostics, context);

            if (node == null)
                return false;

            return !diagnostics.HasErrors;
        }

        /// <summary>Builds, validates, and emits SwiftUI source from an authored Watch tree.</summary>
        /// <param name="element">The supported authoring root, with resolved panel layout when style export is required.</param>
        /// <param name="swift">Generated source on success; empty when conversion or validation prevents emission.</param>
        /// <param name="diagnostics">Combined conversion and emission errors and warnings.</param>
        /// <returns>True when both stages complete without errors. Warnings alone do not fail generation.</returns>
        /// <remarks>This returns text only. It does not write files, compile Swift, export assets, or validate a Watch device.</remarks>
        public static bool TryGenerateSwiftUI(VisualElement element, out string swift,
            out WatchUiConversionDiagnostics diagnostics)
        {
            if (!TryBuildNode(element, out var node, out diagnostics))
            {
                swift = string.Empty;
                return false;
            }

            var generated = WatchUiSwiftEmitter.TryToSwiftUI(node, out swift, out var emissionDiagnostics);
            diagnostics.Errors.AddRange(emissionDiagnostics.Errors);
            diagnostics.Warnings.AddRange(emissionDiagnostics.Warnings);
            return generated && !diagnostics.HasErrors;
        }

        private sealed class WatchUiBuildContext
        {
            private readonly Dictionary<string, int> kindIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            public string ResolveFallbackId(string kind)
            {
                if (string.IsNullOrWhiteSpace(kind))
                {
                    kind = "watchUi";
                }

                var normalized = SanitizeKindForId(kind);
                var next = 1;
                if (kindIndex.TryGetValue(normalized, out var existing))
                {
                    next = existing + 1;
                }

                kindIndex[normalized] = next;
                return string.Format("{0}_{1}", normalized, next);
            }

            private static string SanitizeKindForId(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return "watchUi";
                }

                var cleaned = new System.Text.StringBuilder();
                foreach (var ch in value)
                {
                    if (char.IsLetterOrDigit(ch))
                    {
                        cleaned.Append(ch);
                        continue;
                    }

                    cleaned.Append('_');
                }

                var sanitized = cleaned.ToString();
                return string.IsNullOrWhiteSpace(sanitized) ? "watchUi" : sanitized;
            }
        }

        private static WatchUiNode BuildNode(VisualElement element, WatchUiConversionDiagnostics diagnostics,
            WatchUiBuildContext context)
        {
            if (element == null)
            {
                diagnostics.Errors.Add("Cannot convert null visual element.");
                return null;
            }

            var node = new WatchUiNode();
            if (!TryFillNodeType(element, node, diagnostics))
            {
                diagnostics.Errors.Add(string.Format("Unsupported visual element '{0}'. Add a WatchUiElement subtype.",
                    element.GetType().Name));
                return null;
            }

            var watchElement = element as WatchUiElement;
            if (watchElement != null)
            {
                watchElement.WriteCommonNode(node);
                node.presentation = WatchUiStyleSnapshot.Capture(watchElement, diagnostics);
                if (string.IsNullOrWhiteSpace(node.id))
                {
                    node.id = context.ResolveFallbackId(node.kind);
                }
            }

            var childNodes = new List<WatchUiNode>();
            foreach (var child in EnumerateChildren(element))
            {
                if (child == null)
                {
                    diagnostics.Warnings.Add(string.Format("Null child under element '{0}'.", element.GetType().Name));
                    continue;
                }

                var childNode = BuildNode(child, diagnostics, context);
                // NOTE: fallback ids are assigned in child traversal to keep one namespace across the tree.
                if (childNode != null)
                {
                    childNodes.Add(childNode);
                }
            }

            if (childNodes.Count > 0)
            {
                node.children = childNodes.ToArray();
            }

            return node;
        }

        private static IEnumerable<VisualElement> EnumerateChildren(VisualElement element)
        {
            if (element == null)
            {
                return Array.Empty<VisualElement>();
            }

            return WatchUiToolkitInterop.GetChildren(element);
        }

        private static bool TryFillNodeType(VisualElement element, WatchUiNode node,
            WatchUiConversionDiagnostics diagnostics)
        {
            if (!(element is WatchUiElement))
            {
                return false;
            }

            if (element is WatchVStack)
            {
                node.kind = "WatchVStack";
                var stack = element as WatchVStack;
                node.alignment = stack.Alignment;
                node.spacing = stack.Spacing;
            }
            else if (element is WatchHStack)
            {
                node.kind = "WatchHStack";
                var stack = element as WatchHStack;
                node.alignment = stack.Alignment;
                node.spacing = stack.Spacing;
            }
            else if (element is WatchZStack)
            {
                node.kind = "WatchZStack";
                var stack = element as WatchZStack;
                node.alignment = stack.Alignment;
            }
            else if (element is WatchScrollView)
            {
                node.kind = "WatchScrollView";
                var scrollView = element as WatchScrollView;
                node.axes = scrollView.Axes;
                node.showsIndicators = scrollView.ShowsIndicators;
            }
            else if (element is WatchList)
            {
                node.kind = "WatchList";
                node.style = (element as WatchList).Style;
            }
            else if (element is WatchGrid)
            {
                node.kind = "WatchGrid";
                var grid = element as WatchGrid;
                node.alignment = grid.Alignment;
                node.horizontalSpacing = grid.HorizontalSpacing;
                node.verticalSpacing = grid.VerticalSpacing;
            }
            else if (element is WatchGridRow)
            {
                node.kind = "WatchGridRow";
                node.alignment = (element as WatchGridRow).Alignment;
            }
            else if (element is WatchText)
            {
                var text = element as WatchText;
                node.kind = "WatchText";
                node.label = text.Label;
            }
            else if (element is WatchSpacer)
            {
                node.kind = "WatchSpacer";
                var spacer = element as WatchSpacer;
                node.minLength = spacer.MinLength;
            }
            else if (element is WatchNavigationStack)
            {
                node.kind = "WatchNavigationStack";
            }
            else if (element is WatchNavigationLink)
            {
                node.kind = "WatchNavigationLink";
                var navigationLink = element as WatchNavigationLink;
                node.title = navigationLink.Title;
                node.route = navigationLink.Route;
            }
            else if (element is WatchNavigationDestination)
            {
                node.kind = "WatchNavigationDestination";
                var destination = element as WatchNavigationDestination;
                node.route = destination.Route;
            }
            else if (element is WatchLabel)
            {
                node.kind = "WatchLabel";
                var label = element as WatchLabel;
                node.title = label.Title;
                node.systemImage = label.SystemImage;
            }
            else if (element is WatchDivider)
            {
                node.kind = "WatchDivider";
            }
            else if (element is WatchTextField)
            {
                node.kind = "WatchTextField";
                var textField = element as WatchTextField;
                node.title = textField.Title;
                node.textBinding = textField.TextBinding;
                node.prompt = textField.Prompt;
                node.action = textField.SubmitAction;
                node.autocorrection = textField.Autocorrection;
            }
            else if (element is WatchImage)
            {
                node.kind = "WatchImage";
                var image = element as WatchImage;
                node.imageName = image.Name;
                node.imageSystemName = image.SystemName;
                node.imageSource = image.Source;
                node.foregroundColor = image.ForegroundColor;
            }
            else if (element is WatchProgressView)
            {
                node.kind = "WatchProgressView";
                var progress = element as WatchProgressView;
                node.label = progress.Label;
                node.value = progress.Value;
                node.total = progress.Total;
                node.style = progress.Style;
            }
            else if (element is WatchButton)
            {
                var button = element as WatchButton;
                node.kind = "WatchButton";
                node.label = button.Label;
                node.action = button.Action;
            }
            else if (element is WatchTouchpad)
            {
                node.kind = "WatchTouchpad";
                var touchpad = element as WatchTouchpad;
                node.label = touchpad.Label;
                node.action = touchpad.Action;
            }
            else if (element is WatchGestureArea)
            {
                node.kind = "WatchGestureArea";
                var gesture = element as WatchGestureArea;
                node.label = gesture.Label;
                node.gesture = gesture.Gesture;
                node.action = gesture.Action;
                node.minimumDistance = gesture.MinimumDistance;
                node.minimumDuration = gesture.MinimumDuration;
                node.maximumDistance = gesture.MaximumDistance;
                node.coordinateSpace = gesture.CoordinateSpace;
            }
            else if (element is WatchCrown)
            {
                node.kind = "WatchCrown";
                var crown = element as WatchCrown;
                node.label = crown.Label;
                node.action = crown.Action;
            }
            else if (element is WatchMotion)
            {
                node.kind = "WatchMotion";
                var motion = element as WatchMotion;
                node.label = motion.Label;
                node.action = motion.Action;
                node.updateInterval = motion.UpdateInterval;
                node.includeAcceleration = motion.IncludeAcceleration;
                node.includeGyro = motion.IncludeGyro;
                node.includeAttitude = motion.IncludeAttitude;
            }
            else if (element is WatchMicrophone)
            {
                node.kind = "WatchMicrophone";
                var microphone = element as WatchMicrophone;
                node.label = microphone.Label;
                node.action = microphone.Action;
                node.updateInterval = microphone.UpdateInterval;
            }
            else if (element is WatchStatus)
            {
                node.kind = "WatchStatus";
                var status = element as WatchStatus;
                node.label = status.Label;
            }
            else if (element is WatchToggle)
            {
                node.kind = "WatchToggle";
                var toggle = element as WatchToggle;
                node.label = toggle.Label;
                node.boolValue = toggle.Value;
                node.action = toggle.Action;
            }
            else if (element is WatchSlider)
            {
                node.kind = "WatchSlider";
                var slider = element as WatchSlider;
                node.label = slider.Label;
                node.minimum = slider.Minimum;
                node.maximum = slider.Maximum;
                node.value = slider.Value;
                node.step = slider.Step;
                node.action = slider.Action;
            }
            else if (element is WatchStepper)
            {
                node.kind = "WatchStepper";
                var stepper = element as WatchStepper;
                node.label = stepper.Label;
                node.minimum = stepper.Minimum;
                node.maximum = stepper.Maximum;
                node.value = stepper.Value;
                node.step = stepper.Step;
                node.action = stepper.Action;
            }
            else if (element is WatchPicker)
            {
                node.kind = "WatchPicker";
                var picker = element as WatchPicker;
                node.label = picker.Label;
                node.selection = picker.Selection;
                node.style = picker.Style;
                node.action = picker.Action;
            }
            else if (element is WatchPickerOption)
            {
                node.kind = "WatchPickerOption";
                var option = element as WatchPickerOption;
                node.selection = option.Value;
                node.label = option.Label;
                node.systemImage = option.SystemImage;
            }
            else if (element is WatchGauge)
            {
                node.kind = "WatchGauge";
                var gauge = element as WatchGauge;
                node.label = gauge.Label;
                node.value = gauge.Value;
                node.minimum = gauge.Minimum;
                node.maximum = gauge.Maximum;
                node.style = gauge.Style;
                node.currentValueLabel = gauge.CurrentValueLabel;
            }
            else if (element is WatchJoystick)
            {
                node.kind = "WatchJoystick";
                var joystick = element as WatchJoystick;
                node.label = joystick.Label;
                node.action = joystick.Action;
                node.radius = joystick.Radius;
                node.deadZone = joystick.DeadZone;
                node.springReturn = joystick.SpringReturn;
            }
            else
            {
                diagnostics.Errors.Add(string.Format("Unsupported WatchUiElement type '{0}'.", element.GetType().Name));
                return false;
            }

            return true;
        }
    }

    /// <summary>Reads child elements and class lists across supported UI Toolkit API shapes.</summary>
    /// <remarks>Reflection fallbacks are local compatibility helpers, not permission to export arbitrary UI Toolkit types.</remarks>
    public static class WatchUiToolkitInterop
    {
        private static readonly Func<VisualElement, object> ChildrenAccessor = BuildChildrenAccessor();
        private static readonly Func<VisualElement, object> ClassListAccessor = BuildClassListAccessor();

        /// <summary>Gets authored children for Watch controls and ordinary children for other visual elements.</summary>
        /// <param name="root">The parent element, or null for an empty result.</param>
        /// <returns>Children in enumeration order, excluding Watch preview-only hierarchy details.</returns>
        public static IEnumerable<VisualElement> GetChildren(VisualElement root)
        {
            if (root == null)
            {
                return Array.Empty<VisualElement>();
            }

            if (root is WatchUiElement watchElement)
                return watchElement.GetAuthoredChildren();

            var directChildren = ReadChildrenFromAccessor(root);
            if (directChildren != null)
            {
                return directChildren;
            }

            return ReadChildrenFromHierarchy(root);
        }

        /// <summary>Reads an element's USS class list through the available API accessor.</summary>
        /// <param name="element">The element to inspect, or null for an empty result.</param>
        /// <returns>Class-list entries, or an empty sequence if the element or accessor is unavailable or reading fails.</returns>
        /// <remarks>This method does not remove preview class names; WatchUiElement filters those before export.</remarks>
        public static IEnumerable<object> GetClassList(VisualElement element)
        {
            if (element == null)
            {
                return Array.Empty<object>();
            }

            if (ClassListAccessor == null)
            {
                return Array.Empty<object>();
            }

            try
            {
                var value = ClassListAccessor(element);
                if (value == null)
                {
                    return Array.Empty<object>();
                }

                if (value is string classText)
                {
                    if (string.IsNullOrWhiteSpace(classText))
                    {
                        return Array.Empty<object>();
                    }

                    return classText.Split();
                }

                if (value is System.Collections.IEnumerable enumerable)
                {
                    return enumerable.Cast<object>();
                }
            }
            catch (Exception)
            {
                return Array.Empty<object>();
            }

            return Array.Empty<object>();
        }

        private static void TryIgnoreReflectionFailure(Exception ex)
        {
            _ = ex;
        }

        private static IEnumerable<VisualElement> ReadChildrenFromAccessor(VisualElement root)
        {
            if (ChildrenAccessor == null)
            {
                return null;
            }

            try
            {
                var rawChildren = ChildrenAccessor(root);
                if (rawChildren == null)
                {
                    return null;
                }

                if (rawChildren is IEnumerable<VisualElement> typedChildren)
                {
                    return typedChildren;
                }

                if (rawChildren is System.Collections.IEnumerable genericChildren)
                {
                    return genericChildren.Cast<VisualElement>();
                }
            }
            catch (Exception ex)
            {
                TryIgnoreReflectionFailure(ex);
                return null;
            }

            return null;
        }

        private static IEnumerable<VisualElement> ReadChildrenFromHierarchy(VisualElement root)
        {
            try
            {
                var hierarchyProperty =
                    typeof(VisualElement).GetProperty("hierarchy", BindingFlags.Instance | BindingFlags.Public);
                if (hierarchyProperty == null)
                {
                    return Array.Empty<VisualElement>();
                }

                var hierarchy = hierarchyProperty.GetValue(root);
                if (hierarchy == null)
                {
                    return Array.Empty<VisualElement>();
                }

                if (hierarchy is System.Collections.IEnumerable hierarchyEnumerable)
                {
                    return hierarchyEnumerable.Cast<VisualElement>();
                }

                var hierarchyType = hierarchy.GetType();
                var getChildrenMethod =
                    hierarchyType.GetMethod(
                        "GetChildren",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                if (getChildrenMethod != null)
                {
                    try
                    {
                        var byMethod = getChildrenMethod.Invoke(hierarchy, null) as IEnumerable;
                        if (byMethod != null)
                        {
                            return byMethod.Cast<VisualElement>();
                        }
                    }
                    catch (Exception ex)
                    {
                        TryIgnoreReflectionFailure(ex);
                    }
                }

                var getChildCountMethod =
                    hierarchyType.GetMethod(
                        "GetChildCount",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                var childCountProperty =
                    hierarchyType.GetProperty("childCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var indexerProperty = hierarchyType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var getAtMethod =
                    hierarchyType.GetMethod(
                        "GetAt",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(int) },
                        null);

                int childCount;
                if (getChildCountMethod != null)
                {
                    childCount = (int)(getChildCountMethod.Invoke(hierarchy, null) ?? 0);
                }
                else
                {
                    childCount = (int)(childCountProperty?.GetValue(hierarchy) ?? 0);
                }

                if (childCount > 0)
                {
                    var children = new List<VisualElement>(childCount);
                    for (var i = 0; i < childCount; i++)
                    {
                        var child = indexerProperty != null
                            ? indexerProperty.GetValue(hierarchy, new object[] { i }) as VisualElement
                            : getAtMethod != null
                                ? getAtMethod.Invoke(hierarchy, new object[] { i }) as VisualElement
                                : null;

                        if (child != null)
                        {
                            children.Add(child);
                        }
                    }

                    if (children.Count > 0)
                    {
                        return children;
                    }

                    return Array.Empty<VisualElement>();
                }
            }
            catch (Exception ex)
            {
                TryIgnoreReflectionFailure(ex);
                return Array.Empty<VisualElement>();
            }

            return Array.Empty<VisualElement>();
        }

        private static Func<VisualElement, object> BuildChildrenAccessor()
        {
            Func<VisualElement, object> TryBuildFromMethod(IEnumerable<string> names)
            {
                foreach (var name in names)
                {
                    var method = typeof(VisualElement)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(methodInfo =>
                            methodInfo.Name == name &&
                            methodInfo.GetParameters().Length == 0 &&
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(methodInfo.ReturnType) &&
                            methodInfo.ReturnType != typeof(string));
                    if (method == null)
                        continue;

                    return element =>
                    {
                        try
                        {
                            return method.Invoke(element, null);
                        }
                        catch (Exception ex)
                        {
                            TryIgnoreReflectionFailure(ex);
                            return null;
                        }
                    };
                }

                return null;
            }

            Func<VisualElement, object> TryBuildFromProperty(IEnumerable<string> names)
            {
                foreach (var name in names)
                {
                    var property = typeof(VisualElement).GetProperty(
                        name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property == null)
                        continue;

                    return element =>
                    {
                        try
                        {
                            return property.GetValue(element);
                        }
                        catch (Exception ex)
                        {
                            TryIgnoreReflectionFailure(ex);
                            return null;
                        }
                    };
                }

                return null;
            }

            var methodAccessor = TryBuildFromMethod(new[] { "Children", "GetChildren" });
            if (methodAccessor != null)
            {
                return methodAccessor;
            }

            var propertyAccessor = TryBuildFromProperty(new[] { "children", "Children", "Hierarchy" });
            if (propertyAccessor != null)
            {
                return propertyAccessor;
            }

            return null;
        }

        private static Func<VisualElement, object> BuildClassListAccessor()
        {
            try
            {
                var property = typeof(VisualElement).GetProperty(
                    "classList",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null)
                {
                    property = typeof(VisualElement).GetProperty(
                        "ClassList",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (property != null)
                {
                    return element =>
                    {
                        try
                        {
                            return property.GetValue(element);
                        }
                        catch (Exception ex)
                        {
                            TryIgnoreReflectionFailure(ex);
                            return null;
                        }
                    };
                }

                var method = typeof(VisualElement)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(info =>
                        (info.Name == "GetClassList" || info.Name == "GetClasses" || info.Name == "GetStyles") &&
                        info.GetParameters().Length == 0 &&
                        typeof(System.Collections.IEnumerable).IsAssignableFrom(info.ReturnType) &&
                        info.ReturnType != typeof(string));

                if (method == null)
                {
                    return null;
                }

                return element =>
                {
                    try
                    {
                        return method.Invoke(element, null);
                    }
                    catch (Exception ex)
                    {
                        TryIgnoreReflectionFailure(ex);
                        return null;
                    }
                };
            }
            catch (Exception ex)
            {
                TryIgnoreReflectionFailure(ex);
                return null;
            }
        }
    }
}
