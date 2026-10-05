# Troubleshoot the earliest failing stage

[Guide index](index.md) · Previous: [Samples](samples.md) · Next: [Contributing](contributing.md)

## Installation and Editor input

If a Git URL or release tag cannot be resolved, check that it exists and that you have access. Publication is pending for this preview; a manifest version alone does not prove availability. Use an accessible local package for evaluation. Do not add unrelated registries to repair a missing repository.

If sample setup rejects URP, check the Graphics asset, any Quality overrides, the default renderer, and required shaders. The package dependency does not activate URP. Setup deliberately leaves those settings to you.

For C# errors, read Console's file and line before attempting iOS export. For keyboard simulation, focus Game view, enable the receiver's Editor simulation, and check Active Input Handling. Keep one receiver enabled. Bind the layout `<AppleWatchController>`, not the type name `AppleWatchControllerDevice`.

If Target Pop cannot find Watch Lab, select the imported source explicitly through the setup menu or `CreateWithWatchSource`. A custom source with a new GUID is not the designated default. Do not expect another file to be chosen automatically.

## Swift export fails or times out

Larger WatchUI sources can compile slowly. The exporter gives the native watchOS compiler 300 seconds. It reports **"watchOS Swift compilation timed out."** if that finite budget expires and preserves the previous output. The CLI wrapper defaults to a separate 330-second outer request timeout. Its fifth argument or `UNITY_TIMEOUT` overrides the outer timeout only; neither extends the native compiler budget. These budgets do not establish the cause of every hang or intermittent timeout.

The error **"Generated Swift syntax validation failed; previous output was preserved"** means the failed validation did not replace the previous file. That previous file is not proof of a fresh export. Keep the error, verify your Xcode command-line tools and watchOS SDK, inspect the source and conversion diagnostics, then retry once. If it repeats, report the smallest source that reproduces it, Unity/Xcode versions, and compiler diagnostics. Do not disable validation or edit an old generated file to bypass the failure.

If the exporter says source dependencies changed, let imports finish and export the unchanged source again. If the build reports **"No fresh fingerprinted Watch UI export state"**, select and re-export the intended source after your final source/generator changes. Then export iOS into a new empty directory. Import or state-recording failures can occur after output writes; read the final result, not just file existence.

When `xcrun` is absent or the Editor is not on macOS, export warns that native compiler validation was skipped. Run actual watchOS compilation before treating that output as validated.

## Native build and physical input

For Xcode errors, distinguish source compilation, linking, signing, and destination availability. Use your own authorized team and bundle identifiers. The package does not repair provisioning or make a device reachable.

If Editor input works but device input does not, confirm the installed phone/Watch apps belong to the same export, both are active, and WatchConnectivity is reachable. File-transfer success does not prove the live-message path. A software device's existence is also not evidence of connectivity. Intermittent input loss and perceived latency need separate measurements; they remain unqualified for this preview.

If HOLD seems inert, first change game state, then watch for the expected reset/action feedback. Check recognition, delivery, and game reaction separately. If Crown scrolls instead of aiming, use a dedicated fixed page and focus the control. If motion stays neutral, press Start motion, enable attitude when needed, check `attitudeTracked`, and distinguish pose from angular velocity.

If the Watch returns to its clock face, establish whether it was inactivity, a system gesture, or a crash from device evidence. This package cannot keep watchOS foreground unconditionally. Do not add an unrelated background session to hide a reachability problem.

For recording failures, check permission, Send Recording before leaving the page, receipt, format decoding, and explicit playback in order. Hearing Test sound only establishes phone audio output.

## Report useful evidence

Include package/Unity/Input System/URP/Xcode versions, the first error, the failing stage, and a small source or reproduction. State whether it occurred in Editor, simulator, or physical devices. Remove credentials, signing identifiers, device identifiers, recording contents, and full local paths before sharing. Do not describe a running job, zero discovered tests, or an installed-but-unlaunched app as a pass.
