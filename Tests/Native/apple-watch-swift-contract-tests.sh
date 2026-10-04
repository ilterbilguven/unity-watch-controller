#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BRIDGE="$REPO_ROOT/Runtime/Plugins/iOS/WatchBridge.swift"
EMITTER="$REPO_ROOT/Editor/WatchUI/WatchUiSwiftEmitter.cs"
POSTPROCESSOR="$REPO_ROOT/Editor/Build/AppleWatchBuildPostprocessor.cs"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

sdk_path="$(xcrun --sdk iphoneos --show-sdk-path)"
task_temp="$(mktemp -d "${TMPDIR:-/tmp}/watch-swift-compile.XXXXXX")"
trap 'rm -r "$task_temp"' EXIT
module_cache="${APPLE_WATCH_SWIFT_MODULE_CACHE_DIR:-$task_temp/modules}"
mkdir -p "$module_cache"
xcrun --sdk iphoneos swiftc \
  -emit-object -o "$task_temp/WatchBridge.o" \
  -swift-version 5 \
  -parse-as-library \
  -warnings-as-errors \
  -module-cache-path "$module_cache" \
  -Xcc -fmodules-cache-path="$module_cache" \
  -target arm64-apple-ios18.0 \
  -sdk "$sdk_path" \
  "$BRIDGE"

rg -q 'private final class WatchConnectivitySession' "$EMITTER" ||
  fail "Generated Watch source must own and retain a WCSession delegate"
rg -q 'session\.delegate = self' "$EMITTER" ||
  fail "Generated Watch session owner must install its delegate"
rg -q 'session\.activate\(\)' "$EMITTER" ||
  fail "Generated Watch session owner must activate WCSession"
rg -q '@main' "$EMITTER" ||
  fail "Generated Watch source must contain a SwiftUI App entry point"
rg -q 'WindowGroup' "$EMITTER" ||
  fail "Generated Watch App must host the generated root view"

if rg -q 'DispatchSemaphore' "$EMITTER"; then
  fail "Microphone permission flow must not block the SwiftUI main thread"
fi

rg -q 'NSMicrophoneUsageDescription' "$POSTPROCESSOR" ||
  fail "Watch Info.plist must include a microphone usage description"
rg -q 'NSMotionUsageDescription' "$POSTPROCESSOR" ||
  fail "Watch Info.plist must include a motion usage description"
rg -q 'transferFile' "$EMITTER" ||
  fail "Completed Watch recordings must use the WatchConnectivity file channel"
rg -q 'didReceive file: WCSessionFile' "$BRIDGE" ||
  fail "The iPhone bridge must persist completed Watch recording files"
rg -q 'WatchBridge_PopRecordingPath' "$BRIDGE" ||
  fail "The native bridge must expose completed recording paths to Unity"

echo "apple-watch Swift contract tests passed"
