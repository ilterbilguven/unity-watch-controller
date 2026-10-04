using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace WatchUI
{
    /// <summary>Copies the supported resolved USS presentation values for one Watch control.</summary>
    /// <remarks>
    /// This snapshot is not a second native theme or a complete USS export.
    /// Supported USS: color, background-color, font-size, -unity-font-style,
    /// horizontal -unity-text-align, opacity, padding, uniform border-radius,
    /// uniform border-width/color, finite min/max dimensions, and align-self:
    /// stretch under a vertical parent. Lengths use UI Toolkit points.
    /// Watch control properties still define layout direction, alignment, and gaps.
    /// Width/height are deliberately not captured: their resolved values include
    /// auto layout and must not become fixed SwiftUI frames. Explicit inline
    /// dimensions are rejected; USS dimensions receive a conversion-level warning
    /// because the public API does not expose their original declarations.
    /// Picker options may inherit their picker's text styling but cannot override
    /// their own presentation. Margins, absolute
    /// positioning, transforms, custom fonts, images, and nonuniform borders are
    /// diagnosed. Platform system fonts and native controls may render differently.
    /// </remarks>
    [Serializable]
    public sealed class WatchUiStyleSnapshot
    {
        /// <summary>The resolved text or foreground color, including alpha.</summary>
        public Color color;
        /// <summary>The resolved background color. Zero alpha leaves the background transparent.</summary>
        public Color backgroundColor;
        /// <summary>The resolved system-font size in UI Toolkit points.</summary>
        public float fontSize;
        /// <summary>Whether the resolved font style requests bold text.</summary>
        public bool bold;
        /// <summary>Whether the resolved font style requests italic text.</summary>
        public bool italic;
        /// <summary>Horizontal text alignment as leading, center, or trailing. Vertical text alignment is not retained.</summary>
        public string textAlignment;
        /// <summary>The resolved opacity, normally from 0 (transparent) to 1 (opaque).</summary>
        public float opacity;
        /// <summary>Top content padding in points.</summary>
        public float paddingTop;
        /// <summary>Right content padding in points.</summary>
        public float paddingRight;
        /// <summary>Bottom content padding in points.</summary>
        public float paddingBottom;
        /// <summary>Left content padding in points.</summary>
        public float paddingLeft;
        /// <summary>The uniform corner radius in points. Nonuniform radii produce a conversion error.</summary>
        public float cornerRadius;
        /// <summary>The uniform border width in points. Zero omits the visible border.</summary>
        public float borderWidth;
        /// <summary>The uniform border color, including alpha.</summary>
        public Color borderColor;
        /// <summary>The positive minimum width in points, or -1 when no supported constraint was resolved.</summary>
        public float minWidth = -1;
        /// <summary>The positive maximum width in points, or -1 when no supported constraint was resolved.</summary>
        public float maxWidth = -1;
        /// <summary>The positive minimum height in points, or -1 when no supported constraint was resolved.</summary>
        public float minHeight = -1;
        /// <summary>The positive maximum height in points, or -1 when no supported constraint was resolved.</summary>
        public float maxHeight = -1;
        /// <summary>Whether align-self requests width stretch. Export supports this only under a vertical parent.</summary>
        public bool stretchWidth;

        /// <summary>Captures supported presentation values after the live panel completes its style and layout pass.</summary>
        /// <param name="element">The Watch element to inspect. Null or detached elements return null.</param>
        /// <param name="diagnostics">The non-null collection that receives unsupported-style errors.</param>
        /// <returns>A snapshot, or null for a missing panel or a display-none branch whose layout cannot be trusted.</returns>
        /// <remarks>
        /// A non-null snapshot can accompany errors; callers must inspect diagnostics before export.
        /// Lay out every authored navigation branch before capture, including branches hidden by the normal preview.
        /// This copies resolved values. It does not preserve arbitrary USS rules, fixed dimensions, or native pixel parity.
        /// </remarks>
        public static WatchUiStyleSnapshot Capture(WatchUiElement element, WatchUiConversionDiagnostics diagnostics)
        {
            if (element == null || element.panel == null)
                return null;

            for (VisualElement ancestor = element; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (ancestor.resolvedStyle.display != DisplayStyle.None)
                    continue;
                Unsupported(element, diagnostics, "display: none (unresolved layout)",
                    "Use the export host to lay out all authored branches before conversion; hidden layout values cannot be captured reliably.");
                return null;
            }

            var style = element.resolvedStyle;
            var snapshot = new WatchUiStyleSnapshot
            {
                color = style.color,
                backgroundColor = style.backgroundColor,
                fontSize = style.fontSize,
                bold = style.unityFontStyleAndWeight == FontStyle.Bold || style.unityFontStyleAndWeight == FontStyle.BoldAndItalic,
                italic = style.unityFontStyleAndWeight == FontStyle.Italic || style.unityFontStyleAndWeight == FontStyle.BoldAndItalic,
                textAlignment = HorizontalAlignment(style.unityTextAlign),
                opacity = style.opacity,
                paddingTop = style.paddingTop,
                paddingRight = style.paddingRight,
                paddingBottom = style.paddingBottom,
                paddingLeft = style.paddingLeft,
                cornerRadius = style.borderTopLeftRadius,
                borderWidth = style.borderTopWidth,
                borderColor = style.borderTopColor,
                minWidth = Dimension(style.minWidth),
                maxWidth = Dimension(style.maxWidth),
                minHeight = Dimension(style.minHeight),
                maxHeight = Dimension(style.maxHeight),
                stretchWidth = style.alignSelf == Align.Stretch
            };

            ValidateSupportedStyle(element, diagnostics);
            if (element is WatchPickerOption)
                ValidatePickerOptionPresentation(element, snapshot, diagnostics);
            return snapshot;
        }

        private static float Dimension(StyleFloat value)
        {
            return value.keyword == StyleKeyword.Undefined && float.IsFinite(value.value) && value.value > 0
                ? value.value
                : -1;
        }

        private static string HorizontalAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return "center";
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return "trailing";
                default:
                    return "leading";
            }
        }

        private static void ValidateSupportedStyle(WatchUiElement element, WatchUiConversionDiagnostics diagnostics)
        {
            var style = element.resolvedStyle;
            if (element.style.width.keyword == StyleKeyword.Undefined)
                Unsupported(element, diagnostics, "inline width", "Use min-width/max-width constraints or supported align-self: stretch instead.");
            if (element.style.height.keyword == StyleKeyword.Undefined)
                Unsupported(element, diagnostics, "inline height", "Use min-height/max-height constraints instead.");
            if (element is WatchImage image && !string.IsNullOrWhiteSpace(image.ForegroundColor)
                && !ColorUtility.TryParseHtmlString(image.ForegroundColor, out _))
                Unsupported(element, diagnostics, "foreground-color",
                    "Use an HTML color name or #RRGGBB/#RRGGBBAA value that UI Toolkit can also render, or a USS color declaration.");
            if (style.alignSelf == Align.Stretch && element.parent != null
                && (element.parent.resolvedStyle.flexDirection == FlexDirection.Row
                    || element.parent.resolvedStyle.flexDirection == FlexDirection.RowReverse))
                Unsupported(element, diagnostics, "align-self: stretch",
                    "Only width stretch under a vertical parent is supported; horizontal-parent cross-axis stretch is not exported.");
            if (style.position == Position.Absolute)
                Unsupported(element, diagnostics, "position: absolute", "Use WatchZStack or semantic stack layout.");
            if (!Mathf.Approximately(style.rotate.angle.value, 0))
                Unsupported(element, diagnostics, "rotate", "Transforms are not exported.");
            if (style.scale.value != Vector3.one)
                Unsupported(element, diagnostics, "scale", "Transforms are not exported.");
            if (!Mathf.Approximately(style.translate.x, 0) || !Mathf.Approximately(style.translate.y, 0) || !Mathf.Approximately(style.translate.z, 0))
                Unsupported(element, diagnostics, "translate", "Transforms are not exported.");
            if (!Mathf.Approximately(style.marginTop, 0) || !Mathf.Approximately(style.marginRight, 0)
                || !Mathf.Approximately(style.marginBottom, 0) || !Mathf.Approximately(style.marginLeft, 0))
                Unsupported(element, diagnostics, "margin", "Use the parent Watch stack's spacing or this element's padding.");

            if (!Uniform(style.borderTopLeftRadius, style.borderTopRightRadius, style.borderBottomRightRadius, style.borderBottomLeftRadius))
                Unsupported(element, diagnostics, "border-radius", "Only a uniform corner radius is supported.");
            if (!Uniform(style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth)
                || (style.borderTopWidth > 0 && (style.borderTopColor != style.borderRightColor
                    || style.borderTopColor != style.borderBottomColor || style.borderTopColor != style.borderLeftColor)))
                Unsupported(element, diagnostics, "border", "Only a uniform border width and color are supported.");

            var background = style.backgroundImage;
            if (background.texture != null || background.sprite != null || background.vectorImage != null || background.renderTexture != null)
                Unsupported(element, diagnostics, "background-image", "Use WatchImage for semantic images.");

            var panelStyle = element.panel.visualTree.resolvedStyle;
            if (style.unityFont != panelStyle.unityFont || !style.unityFontDefinition.Equals(panelStyle.unityFontDefinition))
                Unsupported(element, diagnostics, "-unity-font / -unity-font-definition", "Custom font assets are not exported; use system typography.");
        }

        private static void ValidatePickerOptionPresentation(WatchUiElement element, WatchUiStyleSnapshot snapshot,
            WatchUiConversionDiagnostics diagnostics)
        {
            var picker = element.GetFirstAncestorOfType<WatchPicker>();
            if (picker == null)
                return;

            var inherited = picker.resolvedStyle;
            var option = element.resolvedStyle;
            var overridesInheritedText = option.color != inherited.color
                || !Mathf.Approximately(option.fontSize, inherited.fontSize)
                || option.unityFontStyleAndWeight != inherited.unityFontStyleAndWeight
                || HorizontalAlignment(option.unityTextAlign) != HorizontalAlignment(inherited.unityTextAlign);
            var hasBoxPresentation = snapshot.backgroundColor.a > 0 || !Mathf.Approximately(snapshot.opacity, 1)
                || snapshot.paddingTop != 0 || snapshot.paddingRight != 0 || snapshot.paddingBottom != 0 || snapshot.paddingLeft != 0
                || snapshot.cornerRadius != 0 || snapshot.borderWidth != 0
                || snapshot.minWidth >= 0 || snapshot.maxWidth >= 0 || snapshot.minHeight >= 0 || snapshot.maxHeight >= 0
                || snapshot.stretchWidth;
            if (overridesInheritedText || hasBoxPresentation)
                Unsupported(element, diagnostics, "option presentation",
                    "Style the WatchPicker instead. Per-option presentation is not supported by both the UI Toolkit popup and native picker renderer.");
        }

        private static bool Uniform(float first, float second, float third, float fourth)
        {
            return Mathf.Approximately(first, second) && Mathf.Approximately(first, third) && Mathf.Approximately(first, fourth);
        }

        private static void Unsupported(WatchUiElement element, WatchUiConversionDiagnostics diagnostics, string property, string guidance)
        {
            var id = string.IsNullOrEmpty(element.WatchId) ? element.name : element.WatchId;
            diagnostics.Errors.Add(string.Format("{0} '{1}': unsupported USS {2}. {3}", element.GetType().Name, id, property, guidance));
        }
    }
}
