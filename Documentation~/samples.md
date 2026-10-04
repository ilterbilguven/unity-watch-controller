# Import and use samples

[Guide index](index.md) · Previous: [Build and test](build-and-test.md) · Next: [Troubleshooting](troubleshooting.md)

Open **Window > Package Management > Package Manager**, select **Apple Watch Controller**, expand **Samples**, and click **Import** beside the sample you want. Imports copy editable assets to `Assets`; the hidden `Samples~` source inside the package is not the consumer's authoring location.

For version 0.1.0-preview.1, the default import root is:

```text
Assets/Samples/Apple Watch Controller/0.1.0-preview.1/
```

| Sample | Files below its imported folder |
| --- | --- |
| Basic Controller | `Controller.watch.uxml`, `Scenes/BasicController.unity` |
| Watch Lab | `WatchControllerSample.watch.uxml`, `WatchControllerGallery.uss` |
| Target Pop | `Scenes/WatchControllerDemo.unity`, `Demo/WatchDemoHud.uxml` |

All three samples support URP only. Configure your URP asset and default renderer first. Imports do not choose signing, change bundle identifiers, add scenes to the build, or select URP settings. Save dirty scenes before setup.

## Basic Controller

Choose **Tools > AppleWatch > Open Basic Controller**, then select the imported Basic Controller folder under `Assets`. Setup opens an existing scene without overwriting it. Enter Play Mode, click Game view, use WASD to move, and press Space to fire. The count should increase once per press, including a held press, and the target should briefly grow.

Select the imported `Controller.watch.uxml` for preview or SwiftUI export. The native joystick and Fire button use the same input bindings. Signing and deployment remain separate steps.

The optional Editor API is `BasicControllerSampleSetup.Create(sampleRoot, scenePath, makeStartupScene: false)` in `AppleWatchController.Samples.BasicController.Editor`. Pass `true` only when you intend to change build scene order. Menu setup does not do so.

## Watch Lab

Select `WatchControllerSample.watch.uxml`, then use the Watch UI preview/export menus. The gallery covers navigation, layout, values, touch, hand shortcuts, Crown, motion, audio, and components. Direct-input pages stay fixed; long menus and value pages can scroll. Motion opts into attitude with a 0.05-second request interval.

The UXML and USS are authored source. Native Swift is generated output. Importing this sample does not create a Unity gameplay scene. Preview icons and native symbols differ by design; see [notices](../Third%20Party%20Notices.md).

## Target Pop

Import **Target Pop and Watch Lab**. Choose **Tools > AppleWatch > Open Game Demo**, select the imported Target Pop folder, then choose an authored `.watch.uxml` under `Assets`, normally the imported Watch Lab source. Setup checks required URP shaders and resources before writes. It opens an existing demo scene without overwriting your changes.

Enter Play Mode for movement, Fire, motion modes, Center, reset, effects, a synthetic test sound, and received recording playback. Export the Watch source you selected when building the companion app.

Target Pop and Watch Lab can live in different folders or version directories. The required three-argument API is `TargetPopSampleSetup.Create(sampleRoot, scenePath, makeStartupScene: false)` in `AppleWatchController.Samples.TargetPop.Editor`. It identifies the designated Watch Lab source by its retained asset GUID, not by a sibling folder or a global first match.

Use `CreateWithWatchSource(sampleRoot, scenePath, watchUiPath, false)` to select another authored source explicitly. A copied or reimported source with a different GUID needs explicit selection. The menu always asks for the source and saves no source preference. Missing explicit or designated source fails before writes; other sources do not become automatic fallbacks. Pass `true` only if you intend to change build scene order.

With optional Pipeline support, `watch_demo_create` requires `--sample_root`. Its optional `--watch_ui_path` selects an explicit source; omitting it uses the designated Watch Lab GUID. `--make_startup_scene` defaults to false. The adapter also retains `watch_demo_state`, `watch_demo_pose`, `watch_demo_simulate`, `watch_demo_ui`, and `watch_demo_capture`. Consumers can use all sample menus without Pipeline.

Inter and generated Inter font assets use the [SIL Open Font License](../Samples~/TargetPop/Demo/Inter-OFL.txt). Imported sample scripts use their own assembly definitions; they are not dependencies of the core runtime.
