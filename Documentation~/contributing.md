# Contribute to the package

[Guide index](index.md) · Previous: [Troubleshooting](troubleshooting.md)

Use the [public repository](https://github.com/ilterbilguven/unity-watch-controller). Start with a small URP consumer project on Unity 6000.7.0b2 and install the checkout through Package Manager's **Install package from disk** route. Import only the samples needed for your change. Consumer setup does not require a submodule, Unity Pipeline, Rider, or shell wrappers.

## Source boundaries

| Location | Owner |
| --- | --- |
| `Runtime/Input` | Custom device, managed frame, receiver and stale-input policy; assembly `AppleWatchController.Runtime` |
| `Runtime/Plugins/iOS` | Swift-owned native bridge |
| `Editor/WatchUI` | UXML elements, preview, conversion, generation and export state; assembly `AppleWatchController.Editor` |
| `Editor/Build` | iOS export service and postprocessor; assembly `AppleWatchController.Build.Editor` |
| `Editor/Integrations/Pipeline` | Optional version-constrained CLI adapters |
| `Samples~` | Optional gameplay/gallery assets and their own assemblies |
| `Tests/Editor`, `Tests/Native` | Managed, native and shell checks |

Preserve public type names, control paths, the 216-byte native frame, the 96-byte device state, and recording behavior unless a separately reviewed compatibility change requires otherwise. Core must compile without sample assemblies or Unity Pipeline. URP remains a required package dependency even though the input runtime does not reference rendering APIs.

For a new Watch element, add its authoring properties, converter data/validation, Swift emission, and an honest Unity preview. Test invalid attributes, generated source, styles, interaction, and actual Swift compilation. A visible control can still send the wrong input, so test both paths. Do not change generated Swift by hand.

Keep useful XML comments accurate, including parameter names and units. Add a failing behavior test before a runtime change and run the affected suites plus the complete available suite afterward. Use [build and test](build-and-test.md) to separate Editor, native compile, simulator, and physical acceptance. Report unverified stages directly.

## Optional CLI and submodule workflows

Unity Pipeline `[0.7.0-exp.1, 0.8.0-exp.1)` enables the package adapters; see [Tools](../Tools/README.md). It is separate from Universal Render Pipeline. Always select the consumer project explicitly when using CLI commands. Do not assume an Editor from another project is suitable.

A contributor may keep this repository as a submodule at `Packages/com.ilter.apple-watch-controller`. Initialize/update that submodule through normal Git tools, then open the consumer project. Commit the package changes in its own repository before updating a parent gitlink. Another clone needs access to the referenced package commit. This is a contributor choice, not an installation requirement.

Swift checks require Xcode on macOS. Run the native buffer behavior test with `xcrun swift Tests/Native/apple-watch-buffer-tests.swift` from the package root. The native shell checks cover transport, generation, postprocessing, components, and optional adapters. For the postprocessor check, set `UNITY_EDITOR_VERSION` or `UNITY_IOS_XCODE_DLL` to the installed Editor's iOS Xcode assembly. Do not treat a source contract check as a physical test.

Owned sources use MIT. Preview icons use Apache 2.0 and Inter uses OFL; retain the [notices and licenses](../Third%20Party%20Notices.md). Do not add SF Symbols raster exports, credentials, recordings, device/signing records, or build/cache output to package history.
