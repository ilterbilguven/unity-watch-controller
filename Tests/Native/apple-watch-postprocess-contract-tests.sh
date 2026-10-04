#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
POSTPROCESSOR="$REPO_ROOT/Editor/Build/AppleWatchBuildPostprocessor.cs"
EDITOR_VERSION="${UNITY_EDITOR_VERSION:-}"
UNITY_IOS_XCODE_DLL="${UNITY_IOS_XCODE_DLL:-/Applications/Unity/Hub/Editor/$EDITOR_VERSION/PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.Xcode.dll}"

fail() {
  echo "FAIL: $1" >&2
  exit 1
}

require_source() {
  local pattern="$1"
  local message="$2"
  rg -q "$pattern" "$POSTPROCESSOR" || fail "$message"
}

reject_source() {
  local pattern="$1"
  local message="$2"
  if rg -q "$pattern" "$POSTPROCESSOR"; then
    fail "$message"
  fi
}

require_source 'GetPBXProjectPath' 'postprocessor must resolve the Swift Xcode project through PBXProject.GetPBXProjectPath'
require_source 'typeof\(XcodePbxProject\)' 'postprocessor must bind the installed iOS PBXProject type directly'
require_source 'XcodePbxSourceTree\.Source' 'generated Swift must be registered through the supported three-argument AddFile API'
require_source 'AddSourcesBuildPhase' 'fresh modern Watch target must receive a sources phase before Swift attachment'
reject_source 'Directory\.GetFiles\(buildPath, "project\.pbxproj"' 'postprocessor must not choose the first recursive project.pbxproj'
require_source 'TargetGuidByName' 'owned Watch target discovery must use the installed public exact-name API'
reject_source 'GetAllTargets|GetAllTargetGuids|FindTargetGuidByName' 'non-public or unsupported PBX target lookup reflection must not remain'

require_source 'ModernWatchProductType = "com\.apple\.product-type\.application"' 'single-target watchOS executable must use the application product type'
reject_source 'ModernWatchProductType = "com\.apple\.product-type\.application\.watchapp2"' 'legacy watchapp2 copies a stub executable over the Swift executable'
require_source 'AddTarget' 'modern watchOS target must be created through PBXProject.AddTarget'
require_source 'GetTargetProductFileRef' 'modern Watch product reference must be resolved through the supported PBX API'
require_source 'AddTargetDependency' 'iOS app must depend on the modern Watch target'
require_source 'AddCopyFilesBuildPhase' 'modern Watch product must be embedded through the public PBX copy-files API'
reject_source 'AddCopyFilesBuildPhaseToTheEnd' 'non-public PBX copy-files helpers must not be reflected'
require_source 'GetCopyFilesBuildPhaseByTarget' 'modern Watch embed phase must be reconciled through the supported PBX lookup API'
require_source 'AddFileToBuildSection' 'modern Watch product must be attached to the embed phase'
require_source 'RemoveFileFromBuild' 'generated source and Watch product membership must be normalized before reattachment'
reject_source '"AddWatchExtension"|"AddWatchApp"' 'legacy two-target WatchKit creation must not remain in the postprocessor'

require_source 'FindWatchExecutableTargetGuid' 'generated @main Swift must resolve an executable Watch target explicitly'
require_source 'NSMicrophoneUsageDescription' 'Watch microphone privacy description must be preserved'
require_source 'NSMotionUsageDescription' 'Watch motion privacy description must be preserved'
require_source 'No fresh fingerprinted Watch UI export state was found' 'fresh generated-output enforcement must be preserved'

reject_source 'skipping pbx mutation|skipping watch target provisioning|cannot create watch targets|skipping target-level mutation' 'mandatory PBX operations must fail closed'
reject_source 'Enum\.ToObject\(propertyType, 1\)' 'Swift Xcode project type must not fall back to an unnamed enum value'
require_source 'throw new BuildFailedException' 'mandatory build validation must throw BuildFailedException'

if [[ -f "$UNITY_IOS_XCODE_DLL" ]]; then
  python3 - "$UNITY_IOS_XCODE_DLL" <<'PY'
import re
import sys

payload = open(sys.argv[1], "rb").read()
ascii_names = set(match.decode("ascii") for match in re.findall(rb"[A-Za-z][A-Za-z0-9_]{4,}", payload))
required_methods = {
    "GetPBXProjectPath",
    "TargetGuidByName",
    "AddTarget",
    "GetTargetProductFileRef",
    "AddTargetDependency",
    "AddCopyFilesBuildPhase",
    "GetCopyFilesBuildPhaseByTarget",
    "AddFileToBuildSection",
    "RemoveFileFromBuild",
}
missing = sorted(required_methods - ascii_names)
if missing:
    raise SystemExit("FAIL: installed Unity iOS Xcode API is missing: " + ", ".join(missing))

# AddTarget accepts the product identifier as a caller-supplied string.
# The actual Xcode build, not DLL string contents, validates that identifier.
PY
else
  echo "SKIP: installed Unity iOS Xcode DLL not found at $UNITY_IOS_XCODE_DLL" >&2
fi

echo "apple-watch postprocess contract tests passed"
