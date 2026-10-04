# Export, build, and test

[Guide index](index.md) · Previous: [Motion and audio](motion-and-audio.md) · Next: [Samples](samples.md)

Unity exports game data and an Xcode project. Xcode then compiles, links, and signs the iPhone and Watch apps. Successful Unity export does not mean successful native compilation, signing, installation, or physical input.

## Export through the Editor

1. Install iOS Build Support for Unity 6000.7.0b2. Open **Tools > AppleWatch > Configure iOS Export** to read requirements and open **File > Build Profiles**.
2. Select iOS as the active platform. Add your intended scene to the profile's enabled scene list and check its order. Opening a scene does not add it to the build.
3. Set your own bundle identifier and signing choices. The package does not select a development team or supply credentials.
4. Select the intended `Assets`-owned `.watch.uxml` and use **Tools > AppleWatch > Export Selected Watch UI to SwiftUI**. Wait for success and read warnings.
5. Choose **Tools > AppleWatch > Export iOS Xcode Project (Current SDK)** for your selected SDK, or **Export iOS Simulator Xcode Project** for a simulator export. Use a new empty destination. The service refuses to replace a directory containing files, which can hold signing changes or a useful previous export.

Export enforces Unity's Swift project type, ARM64, iOS 18, and watchOS 11. It rejects Objective-C source in the owned iOS plugin folder and requires fresh fingerprinted Watch UI output. The postprocessor creates a modern single-target `AppleWatchApp` companion, adds its source, privacy descriptions, dependency, and embedding phase. You do not need to create a legacy WatchKit extension target by hand.

Simulator export restores the prior SDK and architecture settings. Package/sample import does not choose those settings. The device export path applies its explicit Swift/ARM64/deployment requirements when you request export.

Open the returned `.xcodeproj` in Xcode. Check signing, bundle identifiers, companion relationship, and available destinations for the iPhone and Watch targets. Use your authorized signing setup. Build and install the matching pair, then open both apps and keep them active. Never select an old app artifact only because its name matches.

## Verify each stage

Resolve C# Console errors before export. For package tests, open **Window > General > Test Runner**, select EditMode, and run the suites available in your project. Package tests require the package in the project's `testables` list; preserve existing entries when adding it. Imported sample test assemblies provide extra coverage. A completed run with nonzero discovered tests and no failures is different from an empty Console or a job still running.

The package includes Editor input/frame, Watch UI, build-service, and sample tests. Swift and shell checks are under [Tests/Native](../Tests/Native). These checks cover different contracts; source checks and simulation cannot establish physical delivery.

## Physical acceptance

Test each of these on the matching installed apps:

- Button: one tap gives one game action; release/repeat still works.
- Joystick: drag moves and release returns to neutral without drift.
- HOLD: change game state first, hold for the authored duration, and confirm recognition and game feedback.
- Swipe: test all four directions and a short canceled movement separately.
- Crown: focus without firing, then turn without scrolling the input page.
- Orientation: rotate, stop, retain pose, then Center. Angular velocity should return toward neutral after stopping.
- Concurrent motion controls: start both, inspect cadence and reference changes, and stop/restart each independently.
- Audio: capture, send, receive, and explicitly play a real recording. A synthetic tone is not a substitute.
- Live input: measure outages and perceived latency with both apps active. Queue age alone is not end-to-end latency.

Simulator checks help with native layout, navigation, and some message paths. They do not replace wrist motion, hand-gesture hardware, microphone capture, or physical latency tests. No physical reliability guarantee is made for this preview.

## Optional command-line tools

Editor menus and services do not require Unity Pipeline. The optional adapter supports `com.unity.pipeline` in `[0.7.0-exp.1, 0.8.0-exp.1)`. This **Unity Pipeline** package supplies CLI integration; it is not the required **Universal Render Pipeline**.

With a ready Editor for the explicit consumer project, optional commands include:

```bash
unity cmd watch_ui_export --project-path /path/to/UnityProject --watch_ui_path Assets/TutorialController.watch.uxml --format json
unity cmd apple_watch_export_ios --project-path /path/to/UnityProject --output_path Build/iOS-Controller --format json
```

The second command requires iOS as the active target, enabled scenes, and a fresh Watch UI export. See [wrapper arguments and requirements](../Tools/README.md). An outer CLI timeout does not remove the exporter's native compiler deadline. See [timeout diagnostics](troubleshooting.md#swift-export-fails-or-times-out).
