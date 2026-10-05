#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FIXTURE="$ROOT/Tests/Native/fixtures/unity-export-cli.sh"
TEMP="$(mktemp -d "${TMPDIR:-/tmp}/watch-cli-tests.XXXXXX")"
TEMP="$(cd "$TEMP" && pwd -P)"
trap 'rm -r "$TEMP"' EXIT
mkdir -p "$TEMP/Assets" "$TEMP/Output"
touch "$TEMP/Assets/Test.watch.uxml" "$TEMP/Assets/Test.generated.swift" "$TEMP/Output/Actual.generated.swift"
export FAKE_PROJECT="$TEMP" EXPECTED_HOME="$HOME"
export FAKE_COMMAND_ARGS="$TEMP/command-args"
unset UNITY_TIMEOUT

run_export() {
  "$ROOT/Tools/watch-ui-pipeline.sh" Assets/Test.watch.uxml "$TEMP" "$FIXTURE" "$@" >"$TEMP/result" 2>&1
}
assert_timeout() {
  local received
  received="$(awk '/^--timeout$/ { getline; print; exit }' "$FAKE_COMMAND_ARGS")"
  [[ "$received" == "$1" ]] || { echo "FAIL: expected timeout $1, received $received"; exit 1; }
}
export FAKE_RESPONSE='{"success":true,"data":{"result":{"Status":"success","OutputPath":"Output/Actual.generated.swift"}}}'
run_export || { sed -n '1,30p' "$TEMP/result"; exit 1; }
rg -Fq "OUTPUT_PATH=$TEMP/Output/Actual.generated.swift" "$TEMP/result" || { echo 'FAIL: must use the actual nested command output'; exit 1; }
assert_timeout 330
run_export json 420
assert_timeout 420
export UNITY_TIMEOUT=390
run_export
assert_timeout 390
run_export json 450
assert_timeout 450
unset UNITY_TIMEOUT

for response in \
  '{"success":true,"data":{"result":{"Status":"failed","OutputPath":"Assets/Test.generated.swift"}}}' \
  '{"success":true,"data":{"result":{"Status":"success"}}}' \
  '{"success":false,"data":{"result":{"Status":"success","OutputPath":"Assets/Test.generated.swift"}}}' \
  '{"success":true,"data":{"result":{"Status":"success","OutputPath":"Output/Missing.generated.swift"}}}' \
  'not-json'; do
  export FAKE_RESPONSE="$response"
  if run_export; then echo "FAIL: accepted failed/missing/malformed result: $response"; exit 1; fi
done
export FAKE_RESPONSE='{"success":true,"data":{"result":{"Status":"success","OutputPath":"Output/Actual.generated.swift"}}}'
export FAKE_PROJECT="$TEMP/OtherProject"
export FAKE_ALLOW_OTHER_PROJECT=true
if run_export; then echo 'FAIL: accepted an unrelated Editor'; exit 1; fi
unset PROJECT_ROOT
if "$ROOT/Tools/watch-ui-pipeline.sh" Assets/Test.watch.uxml >"$TEMP/result" 2>&1; then
  echo 'FAIL: accepted a missing consumer project'; exit 1
fi
rg -Fq 'Explicit consumer project path is required' "$TEMP/result" || { echo 'FAIL: missing project rejection must explain the requirement'; exit 1; }
echo 'Watch UI CLI behavior: 11 cases passed'
