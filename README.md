# Apple Watch Controller for Unity

Use an Apple Watch as an input source for a Unity game. Author the Watch interface in Unity UI Toolkit (UXML and USS), export native SwiftUI, and read input through the Unity Input System.

This preview package requires Unity **6000.7.0b2**, Input System **6.7.0**, and Universal Render Pipeline (URP) **17.7.0**. Native deployment requires Unity's experimental Swift exporter, iOS 18, watchOS 11, an ARM64 iPhone, and a Mac with Xcode and the matching SDKs. Built-in and HDRP projects are not supported. Unity Pipeline and Rider are not required.

Start with [installation](Documentation~/installation.md), then follow the [working-input walkthrough](Documentation~/getting-started.md). The [guide index](Documentation~/index.md) covers authoring, bindings, motion, audio, and native builds. [Three optional samples](Documentation~/samples.md) provide a small controller, a Watch component gallery, and Target Pop.

The transport is foreground-first: keep both apps active for live input. WatchConnectivity does not guarantee low latency, continuous delivery, or that watchOS will keep the app in the foreground. Physical reliability, perceived latency, HOLD, four-way swipe, and concurrent motion controls still need qualification for your application. Editor simulation does not prove device behavior.

OpenUPM submission is pending. No registry installation is offered here. Use the Package Manager routes in the installation guide with an accessible repository or local package.

Owned code and documentation use [MIT](LICENSE.md). See [third-party notices](Third%20Party%20Notices.md) for preview icons and sample fonts. Native SwiftUI uses system symbols; the package does not redistribute SF Symbols PNGs.

[Public repository](https://github.com/ilterbilguven/unity-watch-controller) · [Changelog](CHANGELOG.md) · [Contributing](Documentation~/contributing.md)
