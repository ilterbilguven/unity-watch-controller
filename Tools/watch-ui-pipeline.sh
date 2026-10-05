#!/usr/bin/env bash
set -euo pipefail

WATCH_UI_PATH="${1:-}"
PROJECT_ROOT="${2:-}"
UNITY_CLI="${3:-${UNITY_CLI:-$HOME/.unity/bin/unity}}"
UNITY_TIMEOUT="${5:-${UNITY_TIMEOUT:-330}}"

fail() { echo "$*" >&2; exit 1; }
[[ "$WATCH_UI_PATH" == *.watch.uxml ]] || fail 'Input path must end with .watch.uxml'
[[ -n "$PROJECT_ROOT" ]] || fail 'Explicit consumer project path is required as the second argument.'
command -v jq >/dev/null || fail 'jq is required to validate Unity CLI responses.'
[[ -x "$UNITY_CLI" ]] || fail "Unity CLI not found: $UNITY_CLI"
PROJECT_ROOT="$(cd "$PROJECT_ROOT" && pwd -P)"
if [[ "$WATCH_UI_PATH" != /* ]]; then WATCH_UI_PATH="$PROJECT_ROOT/$WATCH_UI_PATH"; fi
[[ -f "$WATCH_UI_PATH" ]] || fail "Watch UI source not found: $WATCH_UI_PATH"
WATCH_UI_PATH="$(cd "$(dirname "$WATCH_UI_PATH")" && pwd -P)/$(basename "$WATCH_UI_PATH")"
[[ "$WATCH_UI_PATH" == "$PROJECT_ROOT/Assets/"* ]] || fail 'Watch UI source must be inside this project Assets directory.'

TASK_TEMP="$(mktemp -d "${TMPDIR:-/tmp}/watch-ui-export.XXXXXX")"
trap 'rm -r "$TASK_TEMP"' EXIT
connected() {
  "$UNITY_CLI" status --project-path "$PROJECT_ROOT" --format json >"$TASK_TEMP/status.json" &&
    jq -e --arg project "$PROJECT_ROOT" '.success == true and any(.data.instances[]?; .project == $project and .state == "ready")' "$TASK_TEMP/status.json" >/dev/null
}
if ! connected; then
  if [[ "${WATCH_UI_AUTO_OPEN_EDITOR:-false}" == true ]]; then
    "$UNITY_CLI" open "$PROJECT_ROOT" --format json >"$TASK_TEMP/open.json"
    for ((attempt=0; attempt<${WATCH_UI_EDITOR_WAIT_SECONDS:-20}; attempt++)); do
      if connected; then break; fi
      sleep 1
    done
  fi
  connected || fail "STATUS_NO_INSTANCES: No ready Pipeline Editor for '$PROJECT_ROOT'."
fi

if ! "$UNITY_CLI" cmd watch_ui_export --project-path "$PROJECT_ROOT" --timeout "$UNITY_TIMEOUT" \
  --watch_ui_path "$WATCH_UI_PATH" --format json >"$TASK_TEMP/export.json"; then
  sed -n '1,80p' "$TASK_TEMP/export.json" >&2
  fail 'Unity CLI export command failed.'
fi

GENERATED_PATH="$(jq -er '
  select(.success == true) | .data.result |
  select(.Status == "success") | .OutputPath |
  select(type == "string" and length > 0)
' "$TASK_TEMP/export.json")" || fail 'Unity CLI did not return a successful export and OutputPath.'
if [[ "$GENERATED_PATH" != /* ]]; then GENERATED_PATH="$PROJECT_ROOT/$GENERATED_PATH"; fi
[[ -f "$GENERATED_PATH" ]] || fail "Generated Swift file not found after export: $GENERATED_PATH"
echo "OUTPUT_PATH=$GENERATED_PATH"
echo "[WatchUI] pipeline ok: $WATCH_UI_PATH -> $GENERATED_PATH"
