using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace WatchUI
{
    /// <summary>Defines the UXML namespace used by the Watch authoring controls.</summary>
    internal static class WatchUiUxml
    {
        /// <summary>The namespace registered for Watch controls in UXML documents.</summary>
        public const string Namespace = "WatchUI";
    }

    /// <summary>Base class for a Watch control authored with UI Toolkit and exported as SwiftUI.</summary>
    /// <remarks>
    /// These controls form a supported authoring subset, not a general UI Toolkit exporter.
    /// Each control owns local preview elements that do not belong in the exported tree.
    /// Preview interactions update local state or raise <see cref="PreviewAction"/>; they do not
    /// send WatchConnectivity messages or feed the live Unity Input System device.
    /// Native watchOS controls can differ in appearance from this Editor preview.
    /// </remarks>
    [UxmlElement]
    public abstract partial class WatchUiElement : VisualElement
    {
        private WatchUiPreview preview;
        private IVisualElementScheduledItem styleSynchronization;

        /// <summary>Gets the container that receives authored children through the UI Toolkit API.</summary>
        /// <remarks>Use <see cref="GetAuthoredChildren"/> for export; the raw hierarchy also contains preview controls.</remarks>
        public override VisualElement contentContainer => preview?.ContentContainer ?? this;
        /// <summary>Gets the last local preview action, or an empty string before any action.</summary>
        public string LastPreviewAction { get; private set; } = string.Empty;
        /// <summary>Raised with local preview feedback when the user interacts with this control.</summary>
        /// <remarks>This event does not dispatch a gameplay input or validate an action binding.</remarks>
        public event Action<string> PreviewAction;
        internal WatchUiPreview Preview => preview;

        /// <summary>Registers preview refresh and style synchronization with the element's panel lifecycle.</summary>
        /// <remarks>Derived constructors must call <see cref="InitializePreview"/> after adding their class names.</remarks>
        protected WatchUiElement()
        {
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                RefreshPreview();
                schedule.Execute(RefreshPreview);
                if (styleSynchronization == null)
                    styleSynchronization = schedule.Execute(() => preview?.SynchronizeStyle()).Every(100);
                else
                    styleSynchronization.Resume();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => styleSynchronization?.Pause());
            RegisterCallback<GeometryChangedEvent>(_ => RefreshPreview());
            RegisterCallback<CustomStyleResolvedEvent>(_ => RefreshPreview());
        }

        /// <summary>Enumerates child elements that the author added, without preview-only controls.</summary>
        /// <returns>Authored children in hierarchy order, with preview overlay containers removed.</returns>
        /// <remarks>The result is a live enumeration. Do not modify the hierarchy while iterating.</remarks>
        public IEnumerable<VisualElement> GetAuthoredChildren()
        {
            var container = contentContainer;
            for (var index = 0; index < container.hierarchy.childCount; index++)
            {
                var child = container.hierarchy[index];
                if (child.ClassListContains(WatchUiPreview.OverlaySlotClass))
                {
                    for (var nested = 0; nested < child.hierarchy.childCount; nested++)
                        yield return child.hierarchy[nested];
                }
                else if (!child.ClassListContains(WatchUiPreview.InternalClass))
                {
                    yield return child;
                }
            }
        }

        /// <summary>Refreshes local visuals from the current authoring properties, if a preview exists.</summary>
        /// <remarks>This does not export Swift or send input to a game.</remarks>
        public void RefreshPreview()
        {
            preview?.Refresh();
        }

        /// <summary>Creates the preview controls and applies the current authoring properties.</summary>
        /// <remarks>Call once from the derived constructor after adding its USS class.</remarks>
        protected void InitializePreview()
        {
            preview = new WatchUiPreview(this);
            RefreshPreview();
        }

        /// <summary>Changes an authoring value and refreshes its preview only when the value differs.</summary>
        /// <typeparam name="T">The property's value type, compared with its default equality comparer.</typeparam>
        /// <param name="field">The backing field to update.</param>
        /// <param name="value">The new authoring value.</param>
        /// <remarks>Picker options and navigation destinations also refresh their owning container.</remarks>
        protected void SetPreviewValue<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            RefreshPreview();
            if (this is WatchPickerOption)
                GetFirstAncestorOfType<WatchPicker>()?.RefreshPreview();
            if (this is WatchNavigationDestination)
                GetFirstAncestorOfType<WatchNavigationStack>()?.RefreshPreview();
        }

        /// <summary>Records a local action and notifies preview listeners without sending live input.</summary>
        /// <param name="action">The feedback text; null becomes an empty string.</param>
        internal void ReportPreviewAction(string action)
        {
            LastPreviewAction = action ?? string.Empty;
            PreviewAction?.Invoke(LastPreviewAction);
        }

        private const string WatchIdAttributeName = "watch-id";
        private const string EnabledControlAttributeName = "watch-enabled";

        private string watchIdValue = string.Empty;

        /// <summary>Gets or sets the stable authoring identifier from <c>watch-id</c>. Defaults to empty.</summary>
        /// <remarks>The converter assigns a traversal-based fallback when empty. Set an explicit ID for stable tracing.</remarks>
        [UxmlAttribute(WatchIdAttributeName)] public string WatchId
        {
            get => watchIdValue;
            set => SetPreviewValue(ref watchIdValue, value);
        }

        private bool enabledControlValue = true;
        private string handGestureShortcutValue = "None";

        /// <summary>Gets or sets <c>hand-gesture-shortcut</c>: <c>None</c> (default) or <c>PrimaryAction</c>.</summary>
        /// <remarks>
        /// Only WatchButton, WatchToggle, WatchNavigationLink, and WatchTextField support PrimaryAction.
        /// The exported scene allows one PrimaryAction across all branches. This is a native hand shortcut,
        /// not a touch gesture or a raw motion gesture classifier.
        /// </remarks>
        [UxmlAttribute("hand-gesture-shortcut")]
        public string HandGestureShortcut
        {
            get => handGestureShortcutValue;
            set => SetPreviewValue(ref handGestureShortcutValue, value);
        }

        /// <summary>Gets or sets <c>watch-enabled</c>, which enables this control. Defaults to true.</summary>
        /// <remarks>This authoring flag is separate from the inherited UI Toolkit enabled state.</remarks>
        [UxmlAttribute(EnabledControlAttributeName)]
        public bool EnabledControl
        {
            get => enabledControlValue;
            set => SetPreviewValue(ref enabledControlValue, value);
        }

        /// <summary>Copies shared authoring values into an export node.</summary>
        /// <param name="node">The node to update. Null is ignored.</param>
        /// <remarks>This writes the ID, enabled flag, shortcut, and authored classes, not children or resolved styles.</remarks>
        public virtual void WriteCommonNode(WatchUiNode node)
        {
            if (node == null)
                return;

            node.id = WatchId;
            node.enabled = EnabledControl;
            node.handGestureShortcut = HandGestureShortcut;
            node.classes = ClassListAsArray();
        }

        /// <summary>Copies USS class names while excluding names reserved for preview controls.</summary>
        /// <returns>The class names, or null when no exportable class remains.</returns>
        protected string[] ClassListAsArray()
        {
            var classes = WatchUiToolkitInterop.GetClassList(this);
            return ToStringArray(classes);
        }

        private static string[] ToStringArray(System.Collections.IEnumerable values)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var value in values)
            {
                if (value != null)
                {
                    var className = value.ToString();
                    if (!className.StartsWith("watch-preview-", StringComparison.Ordinal))
                        list.Add(className);
                }
            }

            return list.Count == 0
                ? null
                : list.ToArray();
        }
    }

    /// <summary>Arranges authored children vertically through the <c>watch-v-stack</c> UXML element.</summary>
    [UxmlElement("watch-v-stack")]
    public sealed partial class WatchVStack : WatchUiElement
    {
        private string alignmentValue = string.Empty;

        /// <summary>Gets or sets horizontal alignment: leading, center, or trailing. Empty uses the native default.</summary>
        /// <remarks>The exporter also accepts left and right as aliases for leading and trailing.</remarks>
        [UxmlAttribute("alignment")] public string Alignment
        {
            get => alignmentValue;
            set => SetPreviewValue(ref alignmentValue, value);
        }

        private float spacingValue = -1f;

        /// <summary>Gets or sets the gap between children in points. The default -1 uses native stack spacing.</summary>
        [UxmlAttribute("spacing")] public float Spacing
        {
            get => spacingValue;
            set => SetPreviewValue(ref spacingValue, value);
        }

        /// <summary>Creates a vertical stack with its default authoring values and local preview.</summary>
        public WatchVStack()
        {
            AddToClassList("watch-v-stack");
            InitializePreview();
        }
    }

    /// <summary>Arranges authored children horizontally through the <c>watch-h-stack</c> UXML element.</summary>
    [UxmlElement("watch-h-stack")]
    public sealed partial class WatchHStack : WatchUiElement
    {
        private string alignmentValue = string.Empty;

        /// <summary>Gets or sets vertical alignment: top, center, or bottom. Empty uses the native default.</summary>
        /// <remarks>The exporter also accepts up, middle, and down as aliases.</remarks>
        [UxmlAttribute("alignment")] public string Alignment
        {
            get => alignmentValue;
            set => SetPreviewValue(ref alignmentValue, value);
        }

        private float spacingValue = -1f;

        /// <summary>Gets or sets the gap between children in points. The default -1 uses native stack spacing.</summary>
        [UxmlAttribute("spacing")] public float Spacing
        {
            get => spacingValue;
            set => SetPreviewValue(ref spacingValue, value);
        }

        /// <summary>Creates a horizontal stack with its default authoring values and local preview.</summary>
        public WatchHStack()
        {
            AddToClassList("watch-h-stack");
            InitializePreview();
        }
    }

    /// <summary>Overlays authored children through the <c>watch-z-stack</c> UXML element.</summary>
    [UxmlElement("watch-z-stack")]
    public sealed partial class WatchZStack : WatchUiElement
    {
        private string alignmentValue = string.Empty;

        /// <summary>Gets or sets the overlay alignment. Empty uses the native default.</summary>
        /// <remarks>Use center, top, bottom, leading, trailing, topLeading, topTrailing, bottomLeading, or bottomTrailing.</remarks>
        [UxmlAttribute("alignment")] public string Alignment
        {
            get => alignmentValue;
            set => SetPreviewValue(ref alignmentValue, value);
        }

        /// <summary>Creates an overlay stack with its default authoring values and local preview.</summary>
        public WatchZStack()
        {
            AddToClassList("watch-z-stack");
            InitializePreview();
        }
    }

    /// <summary>Provides a scrollable authored container through <c>watch-scroll-view</c>.</summary>
    /// <remarks>Keep Crown and direct touch-control pages outside a competing scroll container.</remarks>
    [UxmlElement("watch-scroll-view")]
    public sealed partial class WatchScrollView : WatchUiElement
    {
        private string axesValue = "vertical";

        /// <summary>Gets or sets the scroll axes: vertical (default), horizontal, or both.</summary>
        [UxmlAttribute("axes")] public string Axes
        {
            get => axesValue;
            set => SetPreviewValue(ref axesValue, value);
        }

        private bool showsIndicatorsValue = true;

        /// <summary>Gets or sets whether the native scroll view shows scroll indicators. Defaults to true.</summary>
        [UxmlAttribute("shows-indicators")] public bool ShowsIndicators
        {
            get => showsIndicatorsValue;
            set => SetPreviewValue(ref showsIndicatorsValue, value);
        }

        /// <summary>Creates a vertical scroll view with indicators and a local preview.</summary>
        public WatchScrollView()
        {
            AddToClassList("watch-scroll-view");
            InitializePreview();
        }
    }

    /// <summary>Groups authored rows in a native SwiftUI list through <c>watch-list</c>.</summary>
    [UxmlElement("watch-list")]
    public sealed partial class WatchList : WatchUiElement
    {
        private string styleValue = "plain";

        /// <summary>Gets or sets <c>list-style</c>: plain (default), carousel, or automatic.</summary>
        /// <remarks>Unknown styles produce an export warning and use the native automatic style.</remarks>
        [UxmlAttribute("list-style")] public string Style
        {
            get => styleValue;
            set => SetPreviewValue(ref styleValue, value);
        }

        /// <summary>Creates a plain list with a local preview.</summary>
        public WatchList()
        {
            AddToClassList("watch-list");
            InitializePreview();
        }
    }

    /// <summary>Arranges <see cref="WatchGridRow"/> children in a native grid through <c>watch-grid</c>.</summary>
    [UxmlElement("watch-grid")]
    public sealed partial class WatchGrid : WatchUiElement
    {
        private string alignmentValue = "center";

        /// <summary>Gets or sets horizontal grid alignment: leading, center (default), or trailing.</summary>
        /// <remarks>Left and right are aliases. Unrecognized values use center.</remarks>
        [UxmlAttribute("alignment")] public string Alignment
        {
            get => alignmentValue;
            set => SetPreviewValue(ref alignmentValue, value);
        }

        private float horizontalSpacingValue = 8f;

        /// <summary>Gets or sets the nonnegative column gap in points. Defaults to 8.</summary>
        [UxmlAttribute("horizontal-spacing")] public float HorizontalSpacing
        {
            get => horizontalSpacingValue;
            set => SetPreviewValue(ref horizontalSpacingValue, value);
        }

        private float verticalSpacingValue = 8f;

        /// <summary>Gets or sets the nonnegative row gap in points. Defaults to 8.</summary>
        [UxmlAttribute("vertical-spacing")] public float VerticalSpacing
        {
            get => verticalSpacingValue;
            set => SetPreviewValue(ref verticalSpacingValue, value);
        }

        /// <summary>Creates a centered grid with 8-point gaps and a local preview.</summary>
        public WatchGrid()
        {
            AddToClassList("watch-grid");
            InitializePreview();
        }
    }

    /// <summary>Defines one row of a <see cref="WatchGrid"/> through <c>watch-grid-row</c>.</summary>
    /// <remarks>A grid row cannot contain another grid row.</remarks>
    [UxmlElement("watch-grid-row")]
    public sealed partial class WatchGridRow : WatchUiElement
    {
        private string alignmentValue = "center";

        /// <summary>Gets or sets vertical cell alignment: top, center (default), or bottom.</summary>
        /// <remarks>Up and down are aliases. Unrecognized values use center.</remarks>
        [UxmlAttribute("alignment")] public string Alignment
        {
            get => alignmentValue;
            set => SetPreviewValue(ref alignmentValue, value);
        }

        /// <summary>Creates a centered grid row with a local preview.</summary>
        public WatchGridRow()
        {
            AddToClassList("watch-grid-row");
            InitializePreview();
        }
    }

    /// <summary>Authors a named image or SF Symbol through <c>watch-image</c>.</summary>
    /// <remarks>Export chooses SystemName, then Name, then Source. At least one must be set.</remarks>
    [UxmlElement("watch-image")]
    public sealed partial class WatchImage : WatchUiElement
    {
        private string nameValue = string.Empty;

        /// <summary>Gets or sets the native image asset name. Defaults to empty.</summary>
        /// <remarks>This is image content metadata, not a request to copy an asset into the Watch app.</remarks>
        [UxmlAttribute("name")] public string Name
        {
            get => nameValue;
            set => SetPreviewValue(ref nameValue, value);
        }

        private string systemNameValue = string.Empty;

        /// <summary>Gets or sets an SF Symbols name, such as <c>heart.fill</c>. Defaults to empty.</summary>
        /// <remarks>A nonempty system name takes priority over Name and Source during export.</remarks>
        [UxmlAttribute("system-name")] public string SystemName
        {
            get => systemNameValue;
            set => SetPreviewValue(ref systemNameValue, value);
        }

        private string sourceValue = string.Empty;

        /// <summary>Gets or sets the image reference used when Name and SystemName are empty.</summary>
        /// <remarks>The preview can load a Unity asset path; export treats this string as a native image name.</remarks>
        [UxmlAttribute("source")] public string Source
        {
            get => sourceValue;
            set => SetPreviewValue(ref sourceValue, value);
        }

        private string foregroundColorValue = string.Empty;

        /// <summary>Gets or sets an explicit image tint. Empty uses the supported resolved text color.</summary>
        /// <remarks>Use an HTML color name or #RRGGBB/#RRGGBBAA value that the preview can parse.</remarks>
        [UxmlAttribute("foreground-color")] public string ForegroundColor
        {
            get => foregroundColorValue;
            set => SetPreviewValue(ref foregroundColorValue, value);
        }

        /// <summary>Creates an image element with no image source and a local preview.</summary>
        public WatchImage()
        {
            AddToClassList("watch-image");
            InitializePreview();
        }
    }

    /// <summary>Displays plain text through the <c>watch-text</c> UXML element.</summary>
    [UxmlElement("watch-text")]
    public sealed partial class WatchText : WatchUiElement
    {
        private string labelValue = string.Empty;

        /// <summary>Gets or sets the displayed text from <c>label</c>. Defaults to empty.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        /// <summary>Creates empty text with a local preview.</summary>
        public WatchText()
        {
            AddToClassList("watch-text");
            InitializePreview();
        }
    }

    /// <summary>Reserves flexible space in a stack through <c>watch-spacer</c>.</summary>
    [UxmlElement("watch-spacer")]
    public sealed partial class WatchSpacer : WatchUiElement
    {
        private float minLengthValue = -1f;

        /// <summary>Gets or sets the minimum spacer length in points. The default -1 uses native sizing.</summary>
        [UxmlAttribute("min-length")] public float MinLength
        {
            get => minLengthValue;
            set => SetPreviewValue(ref minLengthValue, value);
        }

        /// <summary>Creates a spacer that uses native minimum sizing, with a local preview.</summary>
        public WatchSpacer()
        {
            AddToClassList("watch-spacer");
            InitializePreview();
        }
    }

    /// <summary>Combines a title and optional SF Symbol through <c>watch-label</c>.</summary>
    [UxmlElement("watch-label")]
    public sealed partial class WatchLabel : WatchUiElement
    {
        private string titleValue = string.Empty;

        /// <summary>Gets or sets the label title. Empty exports the fallback title <c>Label</c>.</summary>
        [UxmlAttribute("title")] public string Title
        {
            get => titleValue;
            set => SetPreviewValue(ref titleValue, value);
        }

        private string systemImageValue = string.Empty;

        /// <summary>Gets or sets the optional SF Symbols name. Empty exports text without an icon.</summary>
        [UxmlAttribute("system-image")] public string SystemImage
        {
            get => systemImageValue;
            set => SetPreviewValue(ref systemImageValue, value);
        }

        /// <summary>Creates a label with empty title and icon fields and a local preview.</summary>
        public WatchLabel()
        {
            AddToClassList("watch-label");
            InitializePreview();
        }
    }

    /// <summary>Displays a native separator through <c>watch-divider</c>.</summary>
    [UxmlElement("watch-divider")]
    public sealed partial class WatchDivider : WatchUiElement
    {
        /// <summary>Creates a divider with a local preview.</summary>
        public WatchDivider()
        {
            AddToClassList("watch-divider");
            InitializePreview();
        }
    }

    /// <summary>Owns root content and named navigation destinations through <c>watch-navigation-stack</c>.</summary>
    /// <remarks>Place WatchNavigationDestination elements directly inside this stack.</remarks>
    [UxmlElement("watch-navigation-stack")]
    public sealed partial class WatchNavigationStack : WatchUiElement
    {
        /// <summary>Gets the route shown by the local preview, or empty when root content is visible.</summary>
        public string CurrentRoute => Preview?.CurrentRoute ?? string.Empty;
        /// <summary>Shows a matching direct child destination in the local preview.</summary>
        /// <param name="route">The destination route, compared exactly and case-sensitively.</param>
        /// <returns>True if the route exists, including when it is already visible; otherwise false.</returns>
        /// <remarks>This changes only the Editor preview, not navigation on a connected Watch.</remarks>
        public bool NavigateTo(string route) => Preview != null && Preview.NavigateTo(route);
        /// <summary>Returns to the previous route in the local preview.</summary>
        /// <returns>True when a previous route exists; otherwise false.</returns>
        public bool NavigateBack() => Preview != null && Preview.NavigateBack();

        /// <summary>Creates a navigation stack at its root route with a local preview.</summary>
        public WatchNavigationStack()
        {
            AddToClassList("watch-navigation-stack");
            InitializePreview();
        }
    }

    /// <summary>Authors a navigation button that opens a named destination through <c>watch-navigation-link</c>.</summary>
    [UxmlElement("watch-navigation-link")]
    public sealed partial class WatchNavigationLink : WatchUiElement
    {
        private string titleValue = "Navigation";

        /// <summary>Gets or sets the button title. Defaults to <c>Navigation</c>.</summary>
        [UxmlAttribute("title")] public string Title
        {
            get => titleValue;
            set => SetPreviewValue(ref titleValue, value);
        }

        private string routeValue = string.Empty;

        /// <summary>Gets or sets the required route of a destination in the owning navigation stack.</summary>
        /// <remarks>The default empty route fails export validation.</remarks>
        [UxmlAttribute("route")] public string Route
        {
            get => routeValue;
            set => SetPreviewValue(ref routeValue, value);
        }

        /// <summary>Creates a navigation link with no target route and a local preview.</summary>
        public WatchNavigationLink()
        {
            AddToClassList("watch-navigation-link");
            InitializePreview();
        }
    }

    /// <summary>Defines the authored content for a named route through <c>watch-navigation-destination</c>.</summary>
    /// <remarks>Place this element directly inside a WatchNavigationStack. Hidden preview content still belongs to export.</remarks>
    [UxmlElement("watch-navigation-destination")]
    public sealed partial class WatchNavigationDestination : WatchUiElement
    {
        private string routeValue = string.Empty;

        /// <summary>Gets or sets the required route matched by navigation links. Defaults to empty.</summary>
        [UxmlAttribute("route")] public string Route
        {
            get => routeValue;
            set => SetPreviewValue(ref routeValue, value);
        }

        /// <summary>Creates a destination with no route and a local preview.</summary>
        public WatchNavigationDestination()
        {
            AddToClassList("watch-navigation-destination");
            InitializePreview();
        }
    }

    /// <summary>Authors a native editable text field through <c>watch-textfield</c>.</summary>
    [UxmlElement("watch-textfield")]
    public sealed partial class WatchTextField : WatchUiElement
    {
        private string previewText = string.Empty;
        /// <summary>Gets or sets local preview text. Null becomes empty.</summary>
        /// <remarks>This is not exported as initial text and does not update a connected Watch.</remarks>
        public string PreviewText
        {
            get => previewText;
            set => SetPreviewValue(ref previewText, value ?? string.Empty);
        }

        private string titleValue = "Input";

        /// <summary>Gets or sets the text-field title. Defaults to <c>Input</c>.</summary>
        [UxmlAttribute("title")] public string Title
        {
            get => titleValue;
            set => SetPreviewValue(ref titleValue, value);
        }

        private string textBindingValue = string.Empty;

        /// <summary>Gets or sets required text-binding metadata. Defaults to empty, which fails export validation.</summary>
        /// <remarks>
        /// The current emitter validates this field but derives local SwiftUI state from WatchId.
        /// This string does not connect a Unity property or establish two-way data binding.
        /// </remarks>
        [UxmlAttribute("text-binding")] public string TextBinding
        {
            get => textBindingValue;
            set => SetPreviewValue(ref textBindingValue, value);
        }

        private string promptValue = string.Empty;

        /// <summary>Gets or sets optional placeholder text. Defaults to empty.</summary>
        [UxmlAttribute("prompt")] public string Prompt
        {
            get => promptValue;
            set => SetPreviewValue(ref promptValue, value);
        }

        private string submitActionValue = string.Empty;

        /// <summary>Gets or sets authoring metadata for text changes and submission. Defaults to empty.</summary>
        /// <remarks>
        /// A nonempty value also emits a native submit callback. The current emitter uses an
        /// unbound source-ID action for these callbacks, not this string as a gameplay route.
        /// </remarks>
        [UxmlAttribute("submit-action")] public string SubmitAction
        {
            get => submitActionValue;
            set => SetPreviewValue(ref submitActionValue, value);
        }

        private bool autocorrectionValue = true;

        /// <summary>Gets or sets whether the native text field allows autocorrection. Defaults to true.</summary>
        [UxmlAttribute("autocorrection")] public bool Autocorrection
        {
            get => autocorrectionValue;
            set => SetPreviewValue(ref autocorrectionValue, value);
        }

        /// <summary>Creates a text field with empty binding and preview text, with autocorrection enabled.</summary>
        public WatchTextField()
        {
            AddToClassList("watch-textfield");
            InitializePreview();
        }
    }

    /// <summary>Displays determinate progress or an activity indicator through <c>watch-progress-view</c>.</summary>
    [UxmlElement("watch-progress-view")]
    public sealed partial class WatchProgressView : WatchUiElement
    {
        private string labelValue = "Progress";

        /// <summary>Gets or sets the progress label. Defaults to <c>Progress</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private float valueValue = float.NaN;

        /// <summary>Gets or sets completed work in the same units as Total. NaN (default) shows an activity indicator.</summary>
        [UxmlAttribute("value")] public float Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private float totalValue = 1f;

        /// <summary>Gets or sets total work. Defaults to 1 and must be positive for determinate progress.</summary>
        [UxmlAttribute("total")] public float Total
        {
            get => totalValue;
            set => SetPreviewValue(ref totalValue, value);
        }

        /// <summary>Gets or sets <c>progress-style</c>: default, circular, or linear.</summary>
        /// <remarks>Empty values become default. Other unknown values warn and use the native default during export.</remarks>
        [UxmlAttribute("progress-style")]
        public string Style
        {
            get => progressStyle;
            set => SetPreviewValue(ref progressStyle, string.IsNullOrWhiteSpace(value) ? "default" : value);
        }

        private string progressStyle = "default";

        /// <summary>Creates an indeterminate progress view with the default style and a local preview.</summary>
        public WatchProgressView()
        {
            AddToClassList("watch-progress-view");
            InitializePreview();
        }
    }

    /// <summary>Authors a native button through <c>watch-button</c>.</summary>
    /// <remarks>Action metadata does not by itself bind a Unity Input Action. Preview clicks remain local.</remarks>
    [UxmlElement("watch-button")]
    public sealed partial class WatchButton : WatchUiElement
    {
        private string labelValue = "Button";

        /// <summary>Gets or sets the button text. Defaults to <c>Button</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets required action metadata. The default empty value fails export validation.</summary>
        /// <remarks>
        /// Preview clicks report this string. The current emitter's button callback uses an unbound
        /// source-ID action instead; setting this value does not create a gameplay binding.
        /// </remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates a button with no action metadata and a local preview.</summary>
        public WatchButton()
        {
            AddToClassList("watch-button");
            InitializePreview();
        }
    }

    /// <summary>Authors a touch surface for position, press, and touch gestures through <c>watch-touchpad</c>.</summary>
    /// <remarks>Keep the surface outside competing scroll containers. The Editor preview does not send live touch input.</remarks>
    [UxmlElement("watch-touchpad")]
    public sealed partial class WatchTouchpad : WatchUiElement
    {
        private string labelValue = "[WatchTouchpad]";

        /// <summary>Gets or sets the surface caption. Defaults to <c>[WatchTouchpad]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for touch events. Defaults to empty.</summary>
        /// <remarks>The generated control's kind and payload carry touch semantics; arbitrary action names are not gameplay routes.</remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates a touch surface with its default caption and a local preview.</summary>
        public WatchTouchpad()
        {
            AddToClassList("watch-touchpad");
            InitializePreview();
        }
    }

    /// <summary>Authors a surface that recognizes one touch-gesture family through <c>watch-gesture-area</c>.</summary>
    /// <remarks>This control handles screen gestures, not raw motion or microphone gesture recognition.</remarks>
    [UxmlElement("watch-gesture-area")]
    public sealed partial class WatchGestureArea : WatchUiElement
    {
        private string labelValue = "[WatchGestureArea]";

        /// <summary>Gets or sets the gesture-surface caption. Defaults to <c>[WatchGestureArea]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string gestureValue = "tap";

        /// <summary>Gets or sets the touch gesture. Defaults to tap.</summary>
        /// <remarks>
        /// Export supports tap, doubleTap, longPress, swipe, swipeUp, swipeDown, swipeLeft, swipeRight,
        /// and drag. Hyphenated names are also accepted. Unknown names fall back to tap.
        /// </remarks>
        [UxmlAttribute("gesture")] public string Gesture
        {
            get => gestureValue;
            set => SetPreviewValue(ref gestureValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata attached to recognized touch gestures. Defaults to empty.</summary>
        /// <remarks>The gesture kind and payload, not an arbitrary action name, define supported input semantics.</remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        private float minimumDistanceValue = 20f;

        /// <summary>Gets or sets the minimum drag or swipe distance in points. Defaults to 20.</summary>
        /// <remarks>Export substitutes 20 for negative or non-finite values.</remarks>
        [UxmlAttribute("minimum-distance")] public float MinimumDistance
        {
            get => minimumDistanceValue;
            set => SetPreviewValue(ref minimumDistanceValue, value);
        }

        private float minimumDurationValue = 0.5f;

        private float maximumDistanceValue = 20f;

        /// <summary>Gets or sets the maximum allowed movement during a long press, in points. Defaults to 20.</summary>
        /// <remarks>Export substitutes 20 for negative or non-finite values.</remarks>
        [UxmlAttribute("maximum-distance")] public float MaximumDistance
        {
            get => maximumDistanceValue;
            set => SetPreviewValue(ref maximumDistanceValue, value);
        }

        /// <summary>Gets or sets the required long-press duration in seconds. Defaults to 0.5.</summary>
        /// <remarks>Export substitutes 0.5 for negative or non-finite values.</remarks>
        [UxmlAttribute("minimum-duration")] public float MinimumDuration
        {
            get => minimumDurationValue;
            set => SetPreviewValue(ref minimumDurationValue, value);
        }

        private string coordinateSpaceValue = "global";

        /// <summary>Gets or sets the generated drag coordinate space: global (default) or local.</summary>
        /// <remarks>The Editor pointer preview measures positions relative to its own surface.</remarks>
        [UxmlAttribute("coordinate-space")] public string CoordinateSpace
        {
            get => coordinateSpaceValue;
            set => SetPreviewValue(ref coordinateSpaceValue, value);
        }

        /// <summary>Creates a tap surface with default distance and duration thresholds and a local preview.</summary>
        public WatchGestureArea()
        {
            AddToClassList("watch-gesture-area");
            InitializePreview();
        }
    }

    /// <summary>Authors a focusable Digital Crown input surface through <c>watch-crown</c>.</summary>
    /// <remarks>
    /// Generated input reports raw rotation deltas, not a normalized [-1, 1] axis.
    /// A surface tap acquires Crown focus. Keep the surface outside a competing scroll view.
    /// The preview uses a local slider and does not send Crown input to the game.
    /// </remarks>
    [UxmlElement("watch-crown")]
    public sealed partial class WatchCrown : WatchUiElement
    {
        private string labelValue = "[WatchCrown]";

        /// <summary>Gets or sets the Crown-surface caption. Defaults to <c>[WatchCrown]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for Crown rotation. Defaults to empty.</summary>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates a Crown surface with its default caption and a local preview.</summary>
        public WatchCrown()
        {
            AddToClassList("watch-crown");
            InitializePreview();
        }
    }

    /// <summary>Authors a button that starts raw Watch motion sampling through <c>watch-motion</c>.</summary>
    /// <remarks>
    /// The generated control requests selected Core Motion data and stops its source when it disappears.
    /// It does not classify punch, shake, or other gameplay gestures. The Editor preview only reports
    /// simulated feedback and cannot prove hardware sensor behavior.
    /// </remarks>
    [UxmlElement("watch-motion")]
    public sealed partial class WatchMotion : WatchUiElement
    {
        private string labelValue = "[WatchMotion]";

        /// <summary>Gets or sets the motion-start button caption. Defaults to <c>[WatchMotion]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata attached to raw motion samples. Defaults to empty.</summary>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        private float updateIntervalValue = 0.1f;

        /// <summary>Gets or sets the requested sample interval in seconds. Defaults to 0.1.</summary>
        /// <remarks>This is a requested cadence, not a hardware timing guarantee. Invalid values use the export fallback.</remarks>
        [UxmlAttribute("update-interval")] public float UpdateInterval
        {
            get => updateIntervalValue;
            set => SetPreviewValue(ref updateIntervalValue, value);
        }

        private bool includeAccelerationValue = true;

        /// <summary>Gets or sets whether to include user acceleration without gravity. Defaults to true.</summary>
        /// <remarks>The generated payload copies Core Motion's three userAcceleration components.</remarks>
        [UxmlAttribute("include-acceleration")] public bool IncludeAcceleration
        {
            get => includeAccelerationValue;
            set => SetPreviewValue(ref includeAccelerationValue, value);
        }

        private bool includeGyroValue = true;

        private bool includeAttitudeValue;

        /// <summary>Gets or sets whether to include an attitude quaternion and its reference metadata. Defaults to false.</summary>
        /// <remarks>The generated quaternion order is x, y, z, w. Attitude is raw orientation, not a gesture event.</remarks>
        [UxmlAttribute("include-attitude")] public bool IncludeAttitude
        {
            get => includeAttitudeValue;
            set => SetPreviewValue(ref includeAttitudeValue, value);
        }

        /// <summary>Gets or sets whether to include the three Core Motion rotation-rate components. Defaults to true.</summary>
        [UxmlAttribute("include-gyro")] public bool IncludeGyro
        {
            get => includeGyroValue;
            set => SetPreviewValue(ref includeGyroValue, value);
        }

        /// <summary>Creates a motion control with acceleration and gyro enabled at a requested 0.1-second interval.</summary>
        public WatchMotion()
        {
            AddToClassList("watch-motion");
            InitializePreview();
        }
    }

    /// <summary>Authors Watch microphone recording and meter controls through <c>watch-microphone</c>.</summary>
    /// <remarks>
    /// Generated UI requests microphone access, reports level and peak meters, and offers Send Recording.
    /// Meter data is not a clap event; gameplay recognition runs separately. The local preview cannot
    /// record Watch audio or validate permissions, file delivery, or sensor timing.
    /// </remarks>
    [UxmlElement("watch-microphone")]
    public sealed partial class WatchMicrophone : WatchUiElement
    {
        private string labelValue = "[WatchMicrophone]";

        /// <summary>Gets or sets the recording-start caption. Defaults to <c>[WatchMicrophone]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata attached to microphone meter samples. Defaults to empty.</summary>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        private float updateIntervalValue = 0.2f;

        /// <summary>Gets or sets the requested meter update interval in seconds. Defaults to 0.2.</summary>
        /// <remarks>This controls meter sampling, not the audio recording sample rate.</remarks>
        [UxmlAttribute("update-interval")] public float UpdateInterval
        {
            get => updateIntervalValue;
            set => SetPreviewValue(ref updateIntervalValue, value);
        }

        /// <summary>Creates a microphone control with a requested 0.2-second meter interval and a local preview.</summary>
        public WatchMicrophone()
        {
            AddToClassList("watch-microphone");
            InitializePreview();
        }
    }

    /// <summary>Authors a status text element through <c>watch-status</c>.</summary>
    [UxmlElement("watch-status")]
    public sealed partial class WatchStatus : WatchUiElement
    {
        private string labelValue = "[WatchStatus]";

        /// <summary>Gets or sets the status caption. Defaults to <c>[WatchStatus]</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        /// <summary>Creates a status element with its default caption and a local preview.</summary>
        public WatchStatus()
        {
            AddToClassList("watch-status");
            InitializePreview();
        }
    }

    /// <summary>Authors a Boolean control through <c>watch-toggle</c>.</summary>
    /// <remarks>Preview changes update Value locally. Export uses Value as the initial native state.</remarks>
    [UxmlElement("watch-toggle")]
    public sealed partial class WatchToggle : WatchUiElement
    {
        private string labelValue = "Toggle";

        /// <summary>Gets or sets the toggle caption. Defaults to <c>Toggle</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private bool valueValue = false;

        /// <summary>Gets or sets the initial native value and current local preview value. Defaults to false.</summary>
        [UxmlAttribute("value")] public bool Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for value changes. Defaults to empty.</summary>
        /// <remarks>Preview reports this string; the current generated callback uses an unbound source-ID action.</remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates an off toggle with its default caption and a local preview.</summary>
        public WatchToggle()
        {
            AddToClassList("watch-toggle");
            InitializePreview();
        }
    }

    /// <summary>Authors a numeric slider through <c>watch-slider</c>.</summary>
    /// <remarks>Minimum, Maximum, Value, and Step share the author's chosen numeric unit.</remarks>
    [UxmlElement("watch-slider")]
    public sealed partial class WatchSlider : WatchUiElement
    {
        private string labelValue = "Slider";

        /// <summary>Gets or sets the slider caption. Defaults to <c>Slider</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private float minimumValue = 0f;

        /// <summary>Gets or sets the inclusive lower bound. Defaults to 0 and must be less than Maximum.</summary>
        [UxmlAttribute("minimum")] public float Minimum
        {
            get => minimumValue;
            set => SetPreviewValue(ref minimumValue, value);
        }

        private float maximumValue = 1f;

        /// <summary>Gets or sets the inclusive upper bound. Defaults to 1 and must exceed Minimum.</summary>
        [UxmlAttribute("maximum")] public float Maximum
        {
            get => maximumValue;
            set => SetPreviewValue(ref maximumValue, value);
        }

        private float valueValue = 0.5f;

        /// <summary>Gets or sets the initial native value and current local preview value. Defaults to 0.5.</summary>
        [UxmlAttribute("value")] public float Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private float stepValue = 0f;

        /// <summary>Gets or sets the increment between values. The default 0 gives a continuous native slider.</summary>
        /// <remarks>Positive values enable stepping; nonpositive values omit the native step argument.</remarks>
        [UxmlAttribute("step")] public float Step
        {
            get => stepValue;
            set => SetPreviewValue(ref stepValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for slider changes. Defaults to empty.</summary>
        /// <remarks>Preview reports this string; the current generated callback uses an unbound source-ID action.</remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates a continuous 0-to-1 slider at 0.5 with a local preview.</summary>
        public WatchSlider()
        {
            AddToClassList("watch-slider");
            InitializePreview();
        }
    }

    /// <summary>Authors a bounded numeric increment/decrement control through <c>watch-stepper</c>.</summary>
    /// <remarks>Minimum, Maximum, Value, and Step share the author's chosen numeric unit.</remarks>
    [UxmlElement("watch-stepper")]
    public sealed partial class WatchStepper : WatchUiElement
    {
        private string labelValue = "Stepper";

        /// <summary>Gets or sets the stepper caption. Defaults to <c>Stepper</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private float minimumValue = 0f;

        /// <summary>Gets or sets the inclusive lower bound. Defaults to 0 and must be less than Maximum.</summary>
        [UxmlAttribute("minimum")] public float Minimum
        {
            get => minimumValue;
            set => SetPreviewValue(ref minimumValue, value);
        }

        private float maximumValue = 1f;

        /// <summary>Gets or sets the inclusive upper bound. Defaults to 1 and must exceed Minimum.</summary>
        [UxmlAttribute("maximum")] public float Maximum
        {
            get => maximumValue;
            set => SetPreviewValue(ref maximumValue, value);
        }

        private float valueValue = 0f;

        /// <summary>Gets or sets the initial native value and current local preview value. Defaults to 0.</summary>
        [UxmlAttribute("value")] public float Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private float stepValue = 1f;

        /// <summary>Gets or sets the positive increment for each press. Defaults to 1.</summary>
        [UxmlAttribute("step")] public float Step
        {
            get => stepValue;
            set => SetPreviewValue(ref stepValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for stepper changes. Defaults to empty.</summary>
        /// <remarks>Preview reports this string; the current generated callback uses an unbound source-ID action.</remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates a 0-to-1 stepper at 0 with a step of 1 and a local preview.</summary>
        public WatchStepper()
        {
            AddToClassList("watch-stepper");
            InitializePreview();
        }
    }

    /// <summary>Authors a choice control whose children are WatchPickerOption elements through <c>watch-picker</c>.</summary>
    /// <remarks>Export requires at least one option and rejects other child kinds.</remarks>
    [UxmlElement("watch-picker")]
    public sealed partial class WatchPicker : WatchUiElement
    {
        private string labelValue = "Picker";

        /// <summary>Gets or sets the picker caption. Defaults to <c>Picker</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string selectionValue = string.Empty;

        /// <summary>Gets or sets the selected option's Value string. Defaults to empty.</summary>
        /// <remarks>Use option values, not display labels, to identify a choice.</remarks>
        [UxmlAttribute("selection")] public string Selection
        {
            get => selectionValue;
            set => SetPreviewValue(ref selectionValue, value);
        }

        private string styleValue = "automatic";

        /// <summary>Gets or sets <c>picker-style</c>: automatic (default) or wheel.</summary>
        /// <remarks>Menu, segmented, and unknown styles warn and use automatic style on export.</remarks>
        [UxmlAttribute("picker-style")] public string Style
        {
            get => styleValue;
            set => SetPreviewValue(ref styleValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets optional action metadata for selection changes. Defaults to empty.</summary>
        /// <remarks>
        /// A nonempty value emits a native selection-change callback. The current callback uses an
        /// unbound source-ID action rather than this string. Preview reports this string locally.
        /// </remarks>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        /// <summary>Creates an automatic-style picker with no selection or options and a local preview.</summary>
        public WatchPicker()
        {
            AddToClassList("watch-picker");
            InitializePreview();
        }
    }

    /// <summary>Defines a tagged choice within a WatchPicker through <c>watch-picker-option</c>.</summary>
    /// <remarks>Style the owning picker. Export rejects per-option presentation overrides.</remarks>
    [UxmlElement("watch-picker-option")]
    public sealed partial class WatchPickerOption : WatchUiElement
    {
        private string valueValue = string.Empty;

        /// <summary>Gets or sets the required option key matched by WatchPicker.Selection. Defaults to empty.</summary>
        [UxmlAttribute("value")] public string Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private string labelValue = string.Empty;

        /// <summary>Gets or sets the required display label. Defaults to empty, which fails export validation.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string systemImageValue = string.Empty;

        /// <summary>Gets or sets an optional SF Symbols name shown beside the option label. Defaults to empty.</summary>
        [UxmlAttribute("system-image")] public string SystemImage
        {
            get => systemImageValue;
            set => SetPreviewValue(ref systemImageValue, value);
        }

        /// <summary>Creates an option with no key, label, or icon and a local preview.</summary>
        public WatchPickerOption()
        {
            AddToClassList("watch-picker-option");
            InitializePreview();
        }
    }

    /// <summary>Displays a bounded numeric value without editing it through <c>watch-gauge</c>.</summary>
    [UxmlElement("watch-gauge")]
    public sealed partial class WatchGauge : WatchUiElement
    {
        private string labelValue = "Gauge";

        /// <summary>Gets or sets the gauge caption. Defaults to <c>Gauge</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private float valueValue = 0f;

        /// <summary>Gets or sets the displayed value in the same units as its bounds. Defaults to 0.</summary>
        [UxmlAttribute("value")] public float Value
        {
            get => valueValue;
            set => SetPreviewValue(ref valueValue, value);
        }

        private float minimumValue = 0f;

        /// <summary>Gets or sets the inclusive lower bound. Defaults to 0 and must be less than Maximum.</summary>
        [UxmlAttribute("minimum")] public float Minimum
        {
            get => minimumValue;
            set => SetPreviewValue(ref minimumValue, value);
        }

        private float maximumValue = 1f;

        /// <summary>Gets or sets the inclusive upper bound. Defaults to 1 and must exceed Minimum.</summary>
        [UxmlAttribute("maximum")] public float Maximum
        {
            get => maximumValue;
            set => SetPreviewValue(ref maximumValue, value);
        }

        private string styleValue = "automatic";

        /// <summary>Gets or sets <c>gauge-style</c>: automatic (default), circular, or linear.</summary>
        /// <remarks>Unknown styles warn and use the native automatic style on export.</remarks>
        [UxmlAttribute("gauge-style")] public string Style
        {
            get => styleValue;
            set => SetPreviewValue(ref styleValue, value);
        }

        private string currentValueLabelValue = string.Empty;

        /// <summary>Gets or sets optional text for the current reading. Defaults to empty.</summary>
        [UxmlAttribute("current-value-label")] public string CurrentValueLabel
        {
            get => currentValueLabelValue;
            set => SetPreviewValue(ref currentValueLabelValue, value);
        }

        /// <summary>Creates an automatic-style 0-to-1 gauge at 0 with a local preview.</summary>
        public WatchGauge()
        {
            AddToClassList("watch-gauge");
            InitializePreview();
        }
    }

    /// <summary>Authors a drag-based two-axis control through <c>watch-joystick</c>.</summary>
    /// <remarks>Generated axes divide drag translation by Radius and clamp each component to [-1, 1].</remarks>
    [UxmlElement("watch-joystick")]
    public sealed partial class WatchJoystick : WatchUiElement
    {
        private string labelValue = "Joystick";

        /// <summary>Gets or sets the joystick caption. Defaults to <c>Joystick</c>.</summary>
        [UxmlAttribute("label")] public string Label
        {
            get => labelValue;
            set => SetPreviewValue(ref labelValue, value);
        }

        private string actionValue = string.Empty;

        /// <summary>Gets or sets action metadata for joystick changes and release. Defaults to empty.</summary>
        [UxmlAttribute("action")] public string Action
        {
            get => actionValue;
            set => SetPreviewValue(ref actionValue, value);
        }

        private float radiusValue = 48f;

        /// <summary>Gets or sets the positive surface radius and axis scale in points. Defaults to 48.</summary>
        [UxmlAttribute("radius")] public float Radius
        {
            get => radiusValue;
            set => SetPreviewValue(ref radiusValue, value);
        }

        private float deadZoneValue = 8f;

        /// <summary>Gets or sets the minimum drag distance in points. Defaults to 8; valid values run from 0 to Radius.</summary>
        /// <remarks>This is a drag recognition threshold, not an Input System dead-zone processor.</remarks>
        [UxmlAttribute("dead-zone")] public float DeadZone
        {
            get => deadZoneValue;
            set => SetPreviewValue(ref deadZoneValue, value);
        }

        private bool springReturnValue = true;

        /// <summary>Gets or sets whether release sends zero axes. Defaults to true.</summary>
        /// <remarks>When false, the generated release payload keeps the final normalized axes but clears the press flag.</remarks>
        [UxmlAttribute("spring-return")] public bool SpringReturn
        {
            get => springReturnValue;
            set => SetPreviewValue(ref springReturnValue, value);
        }

        /// <summary>Creates a spring-return joystick with a 48-point radius, an 8-point threshold, and a local preview.</summary>
        public WatchJoystick()
        {
            AddToClassList("watch-joystick");
            InitializePreview();
        }
    }
}
