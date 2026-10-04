#!/usr/bin/env bash
set -euo pipefail

WATCH_UI_PATH="${1:-}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ -z "${WATCH_UI_PATH}" ]]; then
  echo "usage: $0 <watch-uxml-path> <project-path> [unity-cli-path] [unity-format] [unity-timeout-seconds]" >&2
  echo "  watch-uxml-path must end with .watch.uxml and be relative to or inside the project" >&2
  exit 1
fi

"$SCRIPT_DIR/watch-ui-pipeline.sh" "$@"
