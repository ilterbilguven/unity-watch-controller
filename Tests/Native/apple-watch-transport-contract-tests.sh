#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
BRIDGE="$ROOT_DIR/Runtime/Plugins/iOS/WatchBridge.swift"
RECEIVER="$ROOT_DIR/Runtime/Input/WatchInputReceiver.cs"
FRAME="$ROOT_DIR/Runtime/Input/WatchInputFrame.cs"

require() {
    local file="$1"
    local pattern="$2"
    local description="$3"

    if ! rg -q --fixed-strings "$pattern" "$file"; then
        echo "FAIL: $description" >&2
        exit 1
    fi
}

require "$BRIDGE" "private let FrameQueueCapacity" "native transport declares a bounded queue"
require "$BRIDGE" "private var pendingFrames: [WatchControllerFrame]" "native transport keeps FIFO frames"
require "$BRIDGE" "private var deferredContinuousFrame: WatchControllerFrame?" "native transport retains a continuous update behind a full discrete queue"
require "$BRIDGE" "func currentContinuousFrame() -> WatchControllerFrame" "native transport retains continuous state"
require "$BRIDGE" "func hasDiscreteInput" "native transport classifies pulse frames"
require "$BRIDGE" "var frame = touchBuffer.currentContinuousFrame()" "incoming messages begin from retained continuous state"
require "$RECEIVER" "[SerializeField] private float staleTimeoutSeconds = 0.25f;" "managed stale timeout defaults to 250ms"
require "$RECEIVER" "private AppleWatchInputFrame lastContinuousFrame" "managed receiver retains continuous values"
require "$RECEIVER" "WithDiscretePulsesCleared" "managed receiver clears pulse controls after one update"
require "$RECEIVER" "Time.realtimeSinceStartupAsDouble" "managed receiver measures stale time on the long-running realtime clock"
require "$RECEIVER" "#if UNITY_EDITOR" "editor simulator implementation is excluded from iOS player compilation"
require "$FRAME" "public AppleWatchInputFrame WithDiscretePulsesCleared()" "frame exposes pulse clear helper"
require "$RECEIVER" "RecordingReceived" "managed receiver exposes completed recording paths outside Input System"

echo "PASS: Apple Watch transport contract static checks"
