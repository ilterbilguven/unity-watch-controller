# Changelog

## 0.1.0-preview.1

Initial preview package.

### Added

- A custom `AppleWatchController` Input System device for buttons, joystick, touch gestures, Digital Crown, motion, and microphone meters.
- A Swift WatchConnectivity bridge with stale-input handling and separate recording-file delivery.
- Watch UI elements for UXML/USS authoring, an Editor preview, SwiftUI generation, and export freshness checks.
- iOS Xcode export services that create a watchOS companion target using Unity's experimental Swift exporter.
- Basic Controller, Watch Lab, and Target Pop samples for URP projects.
- Optional, version-constrained Unity Pipeline adapters and shell wrappers.

### Requirements and limits

- Initial baseline: Unity 6000.7.0b2, Input System 6.7.0, URP 17.7.0, iOS 18, watchOS 11, and ARM64 iPhone.
- Package import does not choose signing, bundle identifiers, URP assets, or build scene order.
- Live transport requires foreground reachability; delivery and latency are not guaranteed.
- Generated Swift compiler validation can time out. A failed validation preserves the previous generated output and does not establish a fresh export.
- Physical input reliability and device acceptance need application-specific testing. Built-in and HDRP projects are unsupported.
