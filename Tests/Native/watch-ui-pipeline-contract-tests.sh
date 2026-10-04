#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PIPELINE="$REPO_ROOT/Tools/watch-ui-pipeline.sh"
EXPORT_MENU="$REPO_ROOT/Editor/WatchUI/WatchUiExportMenu.cs"
EXPORT_COMMAND="$REPO_ROOT/Editor/Integrations/Pipeline/WatchUiExportCommand.cs"
BUILD_STATE="$REPO_ROOT/Editor/WatchUI/AppleWatchUiBuildState.cs"
POSTPROCESSOR="$REPO_ROOT/Editor/Build/AppleWatchBuildPostprocessor.cs"
EMITTER="$REPO_ROOT/Editor/WatchUI/WatchUiSwiftEmitter.cs"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

for required in "$PIPELINE" "$EXPORT_MENU" "$EXPORT_COMMAND" "$BUILD_STATE" "$POSTPROCESSOR" "$EMITTER" "$REPO_ROOT/Tools/README.md" "$REPO_ROOT/Tools/export-watch-ui.sh"; do
  [[ -f "$required" ]] || fail "Required package file is missing: $required"
done

if rg -q 'watch-ui-pipeline\.sh' "$EXPORT_MENU"; then
  fail "Editor export must not shell back into the Unity CLI pipeline"
fi

if rg -q '\(or \.uxml\)|\.watch\.uxml/\.uxml' "$EXPORT_COMMAND" "$REPO_ROOT/Tools/README.md" "$REPO_ROOT/Tools/export-watch-ui.sh"; then
  fail "Watch UI tooling must advertise only the .watch.uxml contract"
fi

if rg -q 'EndsWith\("\.uxml"|\*\.uxml' "$EXPORT_MENU" "$PIPELINE"; then
  fail "Generic .uxml files must not enter the Watch UI export path"
fi

invalid_log="$(mktemp /tmp/watch-ui-invalid-extension-XXXXXX.log)"
if "$PIPELINE" "$REPO_ROOT/Assets/WatchControllerSample.uxml" "$REPO_ROOT" >"$invalid_log" 2>&1; then
  fail "Generic .uxml input was accepted"
fi

if ! rg -q 'must end with \.watch\.uxml' "$invalid_log"; then
  fail "Generic .uxml rejection did not explain the required extension"
fi

rm -f "$invalid_log"

rg -q 'watchUiSha256' "$BUILD_STATE" ||
  fail "Export state must fingerprint the .watch.uxml source"
rg -q 'outputSha256' "$BUILD_STATE" ||
  fail "Export state must fingerprint the generated Swift output"
rg -q 'generatorSha256' "$BUILD_STATE" ||
  fail "Export state must fingerprint the generator assembly"

if rg -q 'Directory\.GetFiles\([^;]*\.generated\.swift' "$POSTPROCESSOR"; then
  fail "Postprocessor must not fall back to an unverified generated Swift file"
fi

rg -q 'File\.Replace\(' "$EXPORT_MENU" ||
  fail "Generated Swift publication must atomically preserve the previous valid output"
rg -q 'await ValidateSwiftSyntaxAsync\(tempPath\)' "$EXPORT_MENU" ||
  fail "Direct Unity export must await Swift validation before publication"
rg -q -- '-emit-object.*-parse-as-library.*-warnings-as-errors' "$EXPORT_MENU" ||
  fail "Export must compile generated Swift with warnings treated as errors"
rg -q 'sourceDependenciesSha256' "$BUILD_STATE" ||
  fail "Export state must fingerprint stylesheet and asset dependencies"
rg -q "case '\\\\n':" "$EMITTER" ||
  fail "Swift string escaping must handle newlines"

echo "watch-ui pipeline contract tests passed"
