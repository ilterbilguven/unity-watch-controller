#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ELEMENTS="$REPO_ROOT/Editor/WatchUI/WatchUiElements.cs"
CONVERTER="$REPO_ROOT/Editor/WatchUI/WatchUiElementConverter.cs"
EMITTER="$REPO_ROOT/Editor/WatchUI/WatchUiSwiftEmitter.cs"
SAMPLE="$REPO_ROOT/Samples~/WatchLab/WatchControllerSample.watch.uxml"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

rg -Uq '\[UxmlElement\][[:space:]]+public abstract partial class WatchUiElement' "$ELEMENTS" ||
  fail "WatchUiElement base must participate in UXML source generation"

for element in \
  watch-scroll-view \
  watch-list \
  watch-grid \
  watch-grid-row \
  watch-stepper \
  watch-picker \
  watch-picker-option \
  watch-gauge \
  watch-joystick; do
  rg -q "UxmlElement\\(\"$element\"\\)" "$ELEMENTS" || fail "Missing authoring element: $element"
done

for kind in \
  WatchScrollView \
  WatchList \
  WatchGrid \
  WatchGridRow \
  WatchStepper \
  WatchPicker \
  WatchPickerOption \
  WatchGauge \
  WatchJoystick; do
  rg -q "\"$kind\"" "$CONVERTER" || fail "Converter does not map $kind"
  rg -q "\"$kind\"" "$EMITTER" || fail "Emitter does not support $kind"
done

for attribute in \
  axes \
  shows-indicators \
  list-style \
  picker-style \
  gauge-style \
  horizontal-spacing \
  vertical-spacing \
  minimum \
  maximum \
  step \
  selection \
  value \
  radius \
  dead-zone; do
  rg -q "UxmlAttribute\\(\"$attribute\"\\)" "$ELEMENTS" || fail "Missing component attribute: $attribute"
done

for element in \
  watch-scroll-view \
  watch-list \
  watch-grid \
  watch-stepper \
  watch-picker \
  watch-gauge \
  watch-joystick; do
  rg -q "watch:$element" "$SAMPLE" || fail "Sample does not cover $element"
done

rg -q 'payload: \[\\"x\\": normalizedX, \\"y\\": normalizedY' "$EMITTER" ||
  fail "WatchJoystick must emit normalized primary-stick coordinates"

echo "watch-ui component contract tests passed"
