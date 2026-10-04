# Target Pop

Import Target Pop and Watch Lab through Package Manager. The package resolves
`com.unity.render-pipelines.universal` 17.7.0. Configure your URP pipeline with a
valid default renderer before setup.
Setup checks for URP shaders but does not install packages or change pipeline
settings. Watch Lab supplies the authored Watch controller.

Select Tools > AppleWatch > Open Game Demo, then select this imported folder
under Assets. Save dirty scenes first. Existing scenes open without overwriting
edits. The setup API is `TargetPopSampleSetup.Create(sampleRoot, scenePath,
makeStartupScene: false)` in `AppleWatchController.Samples.TargetPop.Editor`.
Resource paths resolve from the selected root, not a global asset search.
Build order changes only when you explicitly pass `true`.

Enter Play Mode. WASD moves, Space fires, and the HUD exposes motion modes,
Center, reset, effects, a synthetic test sound, and received recording playback.
The synthetic sound is not a received Watch recording. Export the Watch Lab
source for the native Watch application. Configure signing and deployment
separately; Editor probes do not prove physical transport or audio output.

Optional Pipeline 0.7.x commands retain `watch_demo_create`, `watch_demo_state`,
`watch_demo_pose`, `watch_demo_simulate`, `watch_demo_ui`, and
`watch_demo_capture`. Setup requires `--sample_root` and defaults
`--make_startup_scene` to false. Menus and services do not require Pipeline.

Inter and generated Inter font assets use the SIL Open Font License, supplied
in `Demo/Inter-OFL.txt`.
