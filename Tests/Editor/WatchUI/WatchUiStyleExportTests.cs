using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using WatchUI;

public sealed class WatchUiStyleExportTests
{
    private sealed class StyleTestWindow : EditorWindow { }

    private StyleTestWindow window;
    private string stylesheetPath;

    [TearDown]
    public void TearDown()
    {
        if (window != null)
            window.Close();
        if (!string.IsNullOrEmpty(stylesheetPath))
            AssetDatabase.DeleteAsset(stylesheetPath);
    }

    [UnityTest]
    public IEnumerator AuthoredUssAppearanceReachesSwiftWithoutFreezingAutoLayout()
    {
        var root = new WatchVStack { WatchId = "card", Spacing = 9 };
        root.AddToClassList("card");
        root.Add(new WatchText { Label = "Authored title" });
        yield return Mount(root, @"
            .card {
                background-color: rgb(32, 64, 96);
                color: rgb(255, 128, 0);
                font-size: 19px;
                -unity-font-style: bold;
                -unity-text-align: middle-left;
                opacity: 0.75;
                padding: 7px 11px 13px 17px;
                border-radius: 14px;
                border-width: 2px;
                border-color: rgb(0, 255, 255);
                min-width: 80px;
                min-height: 44px;
            }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(swift, Does.Contain("VStack(spacing: 9)"));
        Assert.That(swift, Does.Contain(".font(.system(size: 19, weight: .bold))"));
        Assert.That(swift, Does.Contain(".multilineTextAlignment(.leading)"));
        Assert.That(swift, Does.Contain(".foregroundStyle(Color(red: 1, green: 0.502, blue: 0, opacity: 1))"));
        Assert.That(swift, Does.Contain(".padding(EdgeInsets(top: 7, leading: 17, bottom: 13, trailing: 11))"));
        Assert.That(swift, Does.Contain(".frame(minWidth: 80, minHeight: 44)"));
        Assert.That(swift, Does.Contain("RoundedRectangle(cornerRadius: 14)"));
        Assert.That(swift, Does.Contain("Color(red: 0.125, green: 0.251, blue: 0.376, opacity: 1)"));
        Assert.That(swift, Does.Contain(".strokeBorder(Color(red: 0, green: 1, blue: 1, opacity: 1), lineWidth: 2)"));
        Assert.That(swift, Does.Contain(".opacity(0.75)"));
        Assert.That(swift, Does.Not.Contain(".frame(width:"));
        Assert.That(swift, Does.Not.Contain(".frame(height:"));
    }

    [UnityTest]
    public IEnumerator AuthoredNavigationLinkStretchFillsLabelBeforePadding()
    {
        var link = new WatchNavigationLink { Title = "Stretch link", Route = "details" };
        yield return VerifyStretchedLabel(link, "NavigationLink(value:");
    }

    [UnityTest]
    public IEnumerator AuthoredButtonStretchFillsLabelBeforePadding()
    {
        var button = new WatchButton { Label = "Stretch button", Action = "send" };
        yield return VerifyStretchedLabel(button, "Button(action:");
    }

    [UnityTest]
    public IEnumerator HorizontalParentStretchIsDiagnosedWithoutInventingWidthStretch()
    {
        var row = new WatchHStack();
        var text = new WatchText { WatchId = "verticalStretch", Label = "Stretch" };
        text.AddToClassList("stretch");
        row.Add(text);
        yield return Mount(row, ".stretch { align-self: stretch; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(row, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("verticalStretch") && error.Contains("align-self")), Is.True);
    }

    [UnityTest]
    public IEnumerator ExplicitMinimumAndMaximumConstraintsStayFlexible()
    {
        var icon = new WatchImage { SystemName = "star.fill" };
        icon.AddToClassList("icon");
        yield return Mount(icon, ".icon { min-width: 22px; max-width: 22px; min-height: 22px; max-height: 22px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(icon, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain(".frame(minWidth: 22, maxWidth: 22, minHeight: 22, maxHeight: 22)"));
        Assert.That(swift, Does.Not.Contain(".frame(width:"));
    }

    [UnityTest]
    public IEnumerator AuthoredMarginsAreDiagnosedInsteadOfSilentlyDroppingSpacing()
    {
        var text = new WatchText { WatchId = "marginText", Label = "Spaced" };
        text.AddToClassList("unsupported");
        yield return Mount(text, ".unsupported { margin-left: 7px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(text, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("marginText") && error.Contains("margin")), Is.True);
    }

    [UnityTest]
    public IEnumerator StylesRemainOnTheirOwnContainerBoundary()
    {
        var root = new WatchVStack();
        root.AddToClassList("outer");
        var text = new WatchText { Label = "Inner" };
        text.AddToClassList("inner");
        root.Add(text);
        yield return Mount(root, ".outer { padding: 23px; } .inner { padding: 5px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out _), Is.True);
        var innerPadding = swift.IndexOf(".padding(EdgeInsets(top: 5,", StringComparison.Ordinal);
        var outerPadding = swift.IndexOf(".padding(EdgeInsets(top: 23,", StringComparison.Ordinal);
        Assert.That(innerPadding, Is.GreaterThan(0));
        Assert.That(outerPadding, Is.GreaterThan(innerPadding));
        Assert.That(swift.Substring(innerPadding, outerPadding - innerPadding), Does.Contain("}"),
            "Outer padding belongs after the stack's closing brace, not on its final child.");
    }

    [UnityTest]
    public IEnumerator UnsupportedAbsolutePositionIsDiagnosed()
    {
        var text = new WatchText { WatchId = "absoluteText", Label = "Positioned" };
        text.AddToClassList("unsupported");
        yield return Mount(text, ".unsupported { position: absolute; left: 20px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(text, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("absoluteText") && error.Contains("position")), Is.True);
    }

    [UnityTest]
    public IEnumerator UnsupportedTransformIsDiagnosed()
    {
        var text = new WatchText { WatchId = "rotatedText", Label = "Rotated" };
        text.AddToClassList("unsupported");
        yield return Mount(text, ".unsupported { rotate: 15deg; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(text, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("rotatedText") && error.Contains("rotate")), Is.True);
    }

    [UnityTest]
    public IEnumerator AuthoredButtonLabelExportsWithoutPreviewImplementationChildren()
    {
        var button = new WatchButton { WatchId = "send", Label = "Unused fallback", Action = "send" };
        var row = new WatchHStack { Spacing = 6 };
        row.Add(new WatchImage { SystemName = "paperplane.fill" });
        row.Add(new WatchText { Label = "Send now" });
        button.Add(row);
        yield return Mount(button, "watch-button { padding: 8px; background-color: blue; }");

        Assert.That(WatchUiElementConverter.TryBuildNode(button, out var node, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(node.children.Select(child => child.kind), Is.EqualTo(new[] { "WatchHStack" }));
        Assert.That(node.children[0].children.Select(child => child.kind), Is.EqualTo(new[] { "WatchImage", "WatchText" }));
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain("HStack(spacing: 6)"));
        Assert.That(swift, Does.Contain("Image(systemName: \"paperplane.fill\")"));
        Assert.That(swift, Does.Contain("Text(\"Send now\")"));
        Assert.That(swift, Does.Not.Contain("Unused fallback"));
        Assert.That(swift, Does.Contain(".buttonStyle(.plain)"));
    }

    [TestCase("WatchGestureArea", ".onTapGesture")]
    [TestCase("WatchTouchpad", ".onTapGesture")]
    [TestCase("WatchCrown", ".onTapGesture")]
    [TestCase("WatchJoystick", ".gesture(")]
    public void AuthoredTouchSurfaceBoundsBelongInsideTheGestureBoundary(string kind, string gesture)
    {
        var node = new WatchUiNode
        {
            kind = kind,
            id = "touchBounds",
            label = "Tap anywhere",
            action = "tap",
            gesture = "tap",
            radius = 36,
            deadZone = 8,
            presentation = new WatchUiStyleSnapshot
            {
                color = Color.white,
                textAlignment = "leading",
                opacity = 1,
                minHeight = 88
            }
        };
        Assert.That(WatchUiSwiftEmitter.TryToSwiftUI(node, out var swift, out _), Is.True);
        var frameIndex = swift.IndexOf(".frame(minHeight: 88)", StringComparison.Ordinal);
        var hitShapeIndex = swift.IndexOf(".contentShape(Rectangle())", StringComparison.Ordinal);
        var gestureIndex = swift.IndexOf(gesture, StringComparison.Ordinal);
        Assert.That(frameIndex, Is.GreaterThan(0));
        Assert.That(hitShapeIndex, Is.GreaterThan(frameIndex));
        Assert.That(gestureIndex, Is.GreaterThan(hitShapeIndex));
    }

    [UnityTest]
    public IEnumerator UnportableImageForegroundIsDiagnosedForMountedAuthoring()
    {
        var image = new WatchImage { WatchId = "namedAssetColor", SystemName = "star.fill", ForegroundColor = "NativeOnlyColorAsset" };
        image.AddToClassList("icon");
        yield return Mount(image, ".icon { color: white; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(image, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("namedAssetColor") && error.Contains("foreground-color")), Is.True);
    }

    [UnityTest]
    public IEnumerator ExplicitImageForegroundIsNotOverriddenByInheritedUssColor()
    {
        var image = new WatchImage { SystemName = "star.fill", ForegroundColor = "#ff0000" };
        image.AddToClassList("icon");
        yield return Mount(image, ".icon { color: white; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(image, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain(".foregroundStyle(Color(red: 1, green: 0, blue: 0, opacity: 1))"));
        Assert.That(swift, Does.Not.Contain(".foregroundStyle(Color(red: 1, green: 1, blue: 1, opacity: 1))"));
    }

    [Test]
    public void CustomNavigationLabelReplacesFallbackTitle()
    {
        var link = new WatchNavigationLink { Title = "Unused fallback", Route = "details" };
        link.Add(new WatchText { Label = "Authored destination" });

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(link, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain("NavigationLink(value: \"details\")"));
        Assert.That(swift, Does.Contain("Text(\"Authored destination\")"));
        Assert.That(swift, Does.Not.Contain("Unused fallback"));
        Assert.That(swift, Does.Contain(".buttonStyle(.plain)"));
    }

    [Test]
    public void NavigationDestinationsAreRegisteredOnStableContentInsideTheirStack()
    {
        var root = new WatchNavigationStack();
        root.Add(new WatchText { Label = "Header with } and \"quoted { text\"" });
        var scroll = new WatchScrollView();
        scroll.Add(new WatchNavigationLink { WatchId = "link}", Title = "Controls", Route = "controls" });
        root.Add(scroll);
        var destination = new WatchNavigationDestination { Route = "controls" };
        destination.Add(new WatchText { Label = "Controls page" });
        root.Add(destination);

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out _), Is.True);
        var stackIndex = swift.IndexOf("NavigationStack(path:", StringComparison.Ordinal);
        var stackOpen = swift.IndexOf('{', stackIndex);
        var stackClose = FindSwiftClosingBrace(swift, stackOpen);
        var modifierIndex = swift.IndexOf(".navigationDestination(for: String.self)", stackOpen, StringComparison.Ordinal);
        Assert.That(modifierIndex, Is.GreaterThan(stackOpen));
        Assert.That(modifierIndex, Is.LessThan(stackClose),
            "Value-based links need their destination registration inside the matching NavigationStack, not on its outside.");

        var contentIndex = swift.IndexOf("VStack(spacing: 0)", stackOpen, StringComparison.Ordinal);
        Assert.That(contentIndex, Is.LessThan(modifierIndex));
        Assert.That(contentIndex, Is.GreaterThan(stackOpen),
            "One stable non-lazy content view must own destination registration for every authored root child.");
        var contentOpen = swift.IndexOf('{', contentIndex);
        var contentClose = FindSwiftClosingBrace(swift, contentOpen);
        Assert.That(contentClose, Is.LessThan(modifierIndex));
        Assert.That(swift.IndexOf("Text(\"Header with", contentOpen, StringComparison.Ordinal), Is.InRange(contentOpen + 1, contentClose - 1));
        Assert.That(swift.IndexOf("NavigationLink(value: \"controls\")", contentOpen, StringComparison.Ordinal), Is.InRange(contentOpen + 1, contentClose - 1));
        Assert.That(swift.Substring(contentClose + 1, modifierIndex - contentClose - 1).Trim(), Is.Empty,
            "Destination registration must modify the shared content view, outside its nested scroll content.");
        Assert.That(FindSwiftClosingBrace(swift, swift.IndexOf('{', modifierIndex)), Is.LessThan(stackClose));
    }

    [Test]
    public void MultipleNavigationDestinationsEmitTheirCompleteRouteBranches()
    {
        var root = new WatchNavigationStack();
        var first = new WatchNavigationDestination { Route = "first" };
        first.Add(new WatchText { Label = "First page" });
        var second = new WatchNavigationDestination { Route = "second" };
        second.Add(new WatchText { Label = "Second page" });
        root.Add(first);
        root.Add(second);
        var swift = string.Empty;

        Assert.DoesNotThrow(() =>
            Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out swift, out _), Is.True));
        Assert.That(swift, Does.Contain("if route == \"first\" {"));
        Assert.That(swift, Does.Contain("} else if route == \"second\" {"));
        Assert.That(swift, Does.Contain("Text(\"First page\")"));
        Assert.That(swift, Does.Contain("Text(\"Second page\")"));
    }

    [Test]
    public void TextFieldBindingKeyIsNotShownAsInitialUserText()
    {
        var field = new WatchTextField
        {
            WatchId = "playerName",
            Title = "Name",
            TextBinding = "profile.displayName",
            Prompt = "Enter your name"
        };

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(field, out var swift, out _), Is.True);
        Assert.That(swift, Does.Contain("@State private var playerNameText: String = \"\""));
        Assert.That(swift, Does.Not.Contain("String = \"profile.displayName\""));
        Assert.That(swift, Does.Contain("TextField(\"Name\", text: $playerNameText, prompt: Text(\"Enter your name\"))"));
        Assert.That(swift, Does.Contain("\"value\": newValue"));
    }

    [Test]
    public void UnmountedElementsDoNotInventResolvedAppearance()
    {
        var text = new WatchText { Label = "Unmounted" };
        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(text, out var swift, out var diagnostics), Is.True);
        Assert.That(swift, Does.Not.Contain(".font(.system(size:"));
        Assert.That(swift, Does.Not.Contain(".foregroundStyle("));
        Assert.That(diagnostics.Warnings, Is.Empty);
    }

    [UnityTest]
    public IEnumerator HiddenAuthoredSubtreeRequiresLayoutPreparationBeforeConversion()
    {
        var root = new WatchVStack { WatchId = "hiddenLayout" };
        root.AddToClassList("hidden-source");
        root.Add(new WatchText { Label = "Hidden page content" });
        yield return Mount(root, ".hidden-source { display: none; padding: 17px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("hiddenLayout") && error.Contains("layout")), Is.True);
    }

    [UnityTest]
    public IEnumerator PickerOptionsMayInheritTheirPickerTypographyAndColor()
    {
        var picker = CreateStyledPicker();
        yield return Mount(picker, ".picker { color: cyan; font-size: 18px; -unity-font-style: bold; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(picker, out var swift, out var diagnostics),
            Is.True, string.Join("\n", diagnostics.Errors));
        Assert.That(swift, Does.Contain(".font(.system(size: 18, weight: .bold))"));
        Assert.That(swift, Does.Contain(".foregroundStyle(Color(red: 0, green: 1, blue: 1, opacity: 1))"));
        Assert.That(swift, Does.Contain(".tag(\"one\")"));
    }

    [UnityTest]
    public IEnumerator PickerOptionSpecificColorIsRejectedWithItsSourceId()
    {
        var picker = CreateStyledPicker();
        yield return Mount(picker, ".picker { color: cyan; } .option { color: red; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(picker, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("styledOption") && error.Contains("option presentation")), Is.True);
    }

    [UnityTest]
    public IEnumerator PickerOptionSpecificPaddingIsRejectedWithItsSourceId()
    {
        var picker = CreateStyledPicker();
        yield return Mount(picker, ".option { padding: 4px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(picker, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("styledOption") && error.Contains("option presentation")), Is.True);
    }

    [UnityTest]
    public IEnumerator ExplicitInlineWidthIsDiagnosed()
    {
        yield return VerifyInlineSizeRejected(true);
    }

    [UnityTest]
    public IEnumerator ExplicitInlineHeightIsDiagnosed()
    {
        yield return VerifyInlineSizeRejected(false);
    }

    [UnityTest]
    public IEnumerator MountedConversionWarnsOnceThatUssWidthAndHeightAreNotExported()
    {
        var root = new WatchVStack();
        root.AddToClassList("sized");
        root.Add(new WatchText { Label = "One" });
        root.Add(new WatchText { Label = "Two" });
        yield return Mount(root, ".sized { width: 55px; height: 24px; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out var diagnostics), Is.True);
        Assert.That(diagnostics.Warnings.Count(warning => warning.Contains("width/height") && warning.Contains("min/max")), Is.EqualTo(1));
        Assert.That(swift, Does.Not.Contain(".frame(width:"));
        Assert.That(swift, Does.Not.Contain(".frame(height:"));
    }

    private static int FindSwiftClosingBrace(string source, int openingBrace)
    {
        Assert.That(openingBrace, Is.GreaterThanOrEqualTo(0));
        var depth = 0;
        var inString = false;
        var inComment = false;
        for (var index = openingBrace; index < source.Length; index++)
        {
            var character = source[index];
            if (inComment)
            {
                if (character == '\n')
                    inComment = false;
                continue;
            }
            if (inString)
            {
                if (character == '\\')
                    index++;
                else if (character == '"')
                    inString = false;
                continue;
            }
            if (character == '"')
                inString = true;
            else if (character == '/' && index + 1 < source.Length && source[index + 1] == '/')
                inComment = true;
            else if (character == '{')
                depth++;
            else if (character == '}' && --depth == 0)
                return index;
        }
        Assert.Fail("Generated Swift contains an unclosed structural brace.");
        return -1;
    }

    private static WatchPicker CreateStyledPicker()
    {
        var picker = new WatchPicker { Label = "Mode", Selection = "one" };
        picker.AddToClassList("picker");
        var option = new WatchPickerOption { WatchId = "styledOption", Value = "one", Label = "One" };
        option.AddToClassList("option");
        picker.Add(option);
        return picker;
    }

    private IEnumerator VerifyInlineSizeRejected(bool width)
    {
        var text = new WatchText { WatchId = "inlineSize", Label = "Sized" };
        if (width)
            text.style.width = 75;
        else
            text.style.height = 40;
        yield return Mount(text, ".unused { color: white; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(text, out var swift, out var diagnostics), Is.False);
        Assert.That(swift, Is.Empty);
        Assert.That(diagnostics.Errors.Any(error => error.Contains("inlineSize") && error.Contains(width ? "width" : "height")), Is.True);
    }

    private IEnumerator VerifyStretchedLabel(WatchUiElement control, string nativeControl)
    {
        var root = new WatchVStack { Alignment = "leading" };
        control.AddToClassList("stretch");
        control.Add(new WatchText { Label = "Authored stretched content" });
        root.Add(control);
        yield return Mount(root, ".stretch { align-self: stretch; padding: 12px; background-color: blue; }");

        Assert.That(WatchUiElementConverter.TryGenerateSwiftUI(root, out var swift, out _), Is.True);
        var controlIndex = swift.IndexOf(nativeControl, StringComparison.Ordinal);
        var frameIndex = swift.IndexOf(".frame(maxWidth: .infinity)", controlIndex, StringComparison.Ordinal);
        var paddingIndex = swift.IndexOf(".padding(EdgeInsets(top: 12,", controlIndex, StringComparison.Ordinal);
        var backgroundIndex = swift.IndexOf(".background(", controlIndex, StringComparison.Ordinal);
        var buttonStyleIndex = swift.IndexOf(".buttonStyle(.plain)", controlIndex, StringComparison.Ordinal);
        Assert.That(frameIndex, Is.GreaterThan(controlIndex));
        Assert.That(paddingIndex, Is.GreaterThan(frameIndex));
        Assert.That(backgroundIndex, Is.GreaterThan(paddingIndex));
        Assert.That(buttonStyleIndex, Is.GreaterThan(backgroundIndex));
        Assert.That(swift, Does.Not.Contain(".frame(width:"));
    }

    private IEnumerator Mount(WatchUiElement element, string uss)
    {
        stylesheetPath = "Assets/WatchStyleExport-" + Guid.NewGuid().ToString("N") + ".uss";
        File.WriteAllText(stylesheetPath, uss);
        AssetDatabase.ImportAsset(stylesheetPath, ImportAssetOptions.ForceSynchronousImport);
        var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylesheetPath);
        Assert.That(stylesheet, Is.Not.Null);
        window = ScriptableObject.CreateInstance<StyleTestWindow>();
        window.position = new Rect(0, 0, 240, 320);
        window.Show();
        window.rootVisualElement.styleSheets.Add(stylesheet);
        window.rootVisualElement.Add(element);
        PreparePickerSourceLayout(element);
        for (var frame = 0; frame < 5; frame++)
        {
            window.Repaint();
            yield return null;
        }
        Assert.That(element.panel, Is.Not.Null);
        if (element.resolvedStyle.display != DisplayStyle.None)
            Assert.That(float.IsNaN(element.resolvedStyle.width), Is.False);
    }

    private static void PreparePickerSourceLayout(WatchUiElement element)
    {
        if (element is WatchPicker picker)
            picker.contentContainer.style.display = DisplayStyle.Flex;
        foreach (var child in element.GetAuthoredChildren().OfType<WatchUiElement>())
            PreparePickerSourceLayout(child);
    }
}
