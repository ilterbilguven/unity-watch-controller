# Optional Unity CLI wrappers

The package's Editor menus and services work without Unity Pipeline. These Bash wrappers require `jq`, the Unity CLI, and a ready Editor for your consumer project with `com.unity.pipeline` in `[0.7.0-exp.1,0.8.0-exp.1)`.

Use the registered command directly, with the consumer project explicitly selected:

```bash
unity cmd watch_ui_export --project-path /path/to/UnityProject --watch_ui_path Assets/Controller.watch.uxml --format json
```

To use a wrapper, locate the installed package's physical directory, then run:

```bash
bash /physical/package/root/Tools/export-watch-ui.sh Assets/Controller.watch.uxml /path/to/UnityProject /path/to/unity
```

Unity's `Packages/com.ilter.apple-watch-controller` asset path is virtual for Git and registry installs. It is not always a shell directory. The wrappers never infer the consumer project from their own directory or select an unrelated Editor.

Arguments are the `.watch.uxml` source path, required consumer project path, optional Unity CLI executable, a retained legacy format argument, and optional timeout in seconds. Responses always use JSON. The default executable is `$HOME/.unity/bin/unity`; the default timeout is 30 seconds. `UNITY_CLI` and `UNITY_TIMEOUT` can override those defaults. `WATCH_UI_AUTO_OPEN_EDITOR=true` allows the wrapper to open the exact consumer project and wait up to `WATCH_UI_EDITOR_WAIT_SECONDS` (default 20) for its Pipeline server.

The source must be inside the consumer's `Assets` directory. The wrapper rejects the wrong Editor, failed responses, missing output paths, malformed JSON, and missing generated files. It prints `OUTPUT_PATH` only after export succeeds. `watch-ui-pipeline.sh` accepts the same arguments.

The optional adapter also registers `watch_ui_preview(watch_ui_path, route)`. When iOS is the active build target, it registers `apple_watch_export_ios(output_path, simulator)` and `apple_watch_ios_sdk_state()`. iOS export requires iOS Build Support, enabled consumer scenes, and a fresh Watch UI export. Success means Xcode project export, not native compilation, signing, installation, or device acceptance.

The package's shell checks are under `Tests/Native`. Set `UNITY_EDITOR_VERSION` to your installed Editor version, or `UNITY_IOS_XCODE_DLL` to its iOS Xcode API assembly, when running the postprocessor check. Swift checks require Xcode on macOS.
