# Author and export the Watch interface

[Guide index](index.md) · Previous: [Getting started](getting-started.md) · Next: [Input System](input-system.md)

UI Toolkit uses **UXML** for an element tree and **USS** for styles. This package defines custom `watch-*` elements in the `WatchUI` namespace. Their Unity previews help you author the interface; the exporter produces native **SwiftUI** source for the Watch. It is not a general converter for every Unity `VisualElement`.

## Create a small controller

Create `Assets/TutorialController.watch.uxml` and paste this complete example. The `.watch.uxml` suffix and an `Assets`-owned source are required for export.

```xml
<?xml version="1.0" encoding="utf-8"?>
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:watch="WatchUI">
    <watch:watch-v-stack spacing="8" alignment="center">
        <watch:watch-text label="Tutorial Controller" />
        <watch:watch-joystick watch-id="move" radius="48" dead-zone="0.1"
            spring-return="true" />
        <watch:watch-button watch-id="fire" label="Fire" action="primary" />
    </watch:watch-v-stack>
</ui:UXML>
```

`watch-id` identifies a control in generated metadata; keep it unique and stable. `spacing` controls gaps between stack children. `dead-zone` ignores small joystick movement around its center. `spring-return` returns the joystick to zero on release. `action` is metadata, not a C# method name or a new Input System control. A button feeds `primaryButton` and a joystick feeds `primaryStick` through the fixed input contract.

Select the source in the Project window. Choose **Tools > AppleWatch > Preview Selected Watch UI**. Check the label, joystick, and button at Watch size. Preview interactions are local UI feedback; they do not send messages to a phone or drive your game. Native SwiftUI has different fonts and insets, so inspect the native screen separately.

Choose **Tools > AppleWatch > Export Selected Watch UI to SwiftUI**. Wait for the Console success message. This source produces `Assets/TutorialController.generated.swift`. On macOS with `xcrun`, the exporter compiles temporary Swift against watchOS 11 with warnings treated as errors before replacing the output. Elsewhere it warns that native compiler validation was skipped. A skipped check is not a successful watchOS compile.

Edit the UXML, USS, or generator, not `.generated.swift`. Export again after changes. The exporter records hashes of source dependencies, generator assemblies, output, and Unity version. The build uses this **fingerprint** to reject stale output. Only the latest selected fresh export feeds the Watch target. Re-export the intended Watch source immediately before your iOS export.

## Components and input surfaces

See [WatchUiElements.cs](../Editor/WatchUI/WatchUiElements.cs) for attributes and defaults, and import [Watch Lab](samples.md#watch-lab) for complete layouts.

| Purpose | Elements |
| --- | --- |
| Layout | `watch-v-stack`, `watch-h-stack`, `watch-z-stack`, `watch-grid`, `watch-grid-row`, `watch-spacer`, `watch-divider` |
| Longer content | `watch-scroll-view`, `watch-list` |
| Navigation | `watch-navigation-stack`, `watch-navigation-link`, `watch-navigation-destination` |
| Display | `watch-text`, `watch-label`, `watch-image`, `watch-progress-view`, `watch-gauge`, `watch-status` |
| Direct input | `watch-button`, `watch-joystick`, `watch-touchpad`, `watch-gesture-area`, `watch-crown` |
| Values and text | `watch-toggle`, `watch-slider`, `watch-stepper`, `watch-picker`, `watch-picker-option`, `watch-textfield` |
| Sensors | `watch-motion`, `watch-microphone` |

A navigation link's `route` must match a destination inside its navigation stack. Use scroll views for long menus. Keep joystick, touch, swipe, and Crown pages fixed; parent scrolling can compete with input.

For a four-way swipe or HOLD, place these elements inside a complete UXML root with the `WatchUI` namespace:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:watch="WatchUI">
    <watch:watch-v-stack spacing="8">
        <watch:watch-gesture-area watch-id="swipe" label="Swipe"
            gesture="swipe" minimum-distance="32" />
        <watch:watch-gesture-area watch-id="hold" label="Hold"
            gesture="long-press" minimum-duration="1" maximum-distance="30" />
    </watch:watch-v-stack>
</ui:UXML>
```

A swipe selects the largest displacement axis on release; a tie selects horizontal. The distance must reach 32 points. A short movement or a canceled gesture produces no swipe. HOLD recognizes one long-press event after one second; its 30-point tolerance permits some pointer movement. It does not send elapsed hold duration. Test recognition, message delivery, and game reaction separately on hardware.

Supported button-like controls can declare `hand-gesture-shortcut="PrimaryAction"`. The generator permits at most one across the whole scene, including hidden destinations. A supported Watch and system configuration activate that control's existing action. This is not general hand-pose recognition or a separate origin flag.

## Styles and shared channels

The exporter supports text/background color, system font size and style, text alignment, opacity, padding, uniform borders/corners, and finite minimum/maximum dimensions. Set equal minimum and maximum dimensions when you need a fixed size:

```css
.symbol {
    min-width: 22px;
    max-width: 22px;
    min-height: 22px;
    max-height: 22px;
}
```

A plain `width` or `height` does not establish equivalent SwiftUI sizing. Custom fonts, images as backgrounds, margins, absolute positioning, transforms, nonuniform borders, and unsupported stretch directions produce diagnostics. Style a picker as a whole, not individual options. Read warnings even when export succeeds.

Value controls can share `axis0`; changing `action` does not create independent channels. Text-field metadata does not create a string-valued device control or two-way Unity property binding. `watch-status` displays its authored label, not connection health. Several callbacks use `unbound:<id>` metadata even when an action is authored; rely on the documented control-kind mapping, not arbitrary action-string routing.

Preview icons use Material Design Icons under the retained [Apache 2.0 license](../Editor/WatchUI/Resources/WatchUiSymbols/Material-Icons-LICENSE.txt), while native SwiftUI uses system symbols. See [third-party notices](../Third%20Party%20Notices.md). For export failures, read [troubleshooting](troubleshooting.md#swift-export-fails-or-times-out).
