#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_COMMAND="$REPO_ROOT/Editor/Integrations/Pipeline/Build/AppleWatchBuildCommand.cs"
EXPORT_COMMAND="$REPO_ROOT/Editor/Integrations/Pipeline/WatchUiExportCommand.cs"
BUILD_SERVICE="$REPO_ROOT/Editor/Build/AppleWatchBuildService.cs"

rg -Uq 'CliCommand\([[:space:]]*"apple_watch_export_ios"' "$BUILD_COMMAND"
rg -q 'AppleWatchBuildService\.ExportIos' "$BUILD_COMMAND"
rg -q 'BuildPipeline\.BuildPlayer' "$BUILD_SERVICE"
rg -q 'BuildTarget\.iOS' "$BUILD_SERVICE"
rg -q 'Directory\.EnumerateFileSystemEntries' "$BUILD_SERVICE"
rg -Uq 'CliCommand\([[:space:]]*"watch_ui_export"' "$EXPORT_COMMAND"

echo "apple-watch Unity CLI command contract tests passed"
