#!/usr/bin/env bash
set -euo pipefail
case "${1:-}" in
  --version) echo '1.0.0-beta.12' ;;
  status)
    [[ " $* " == *" --project-path $FAKE_PROJECT "* || "${FAKE_ALLOW_OTHER_PROJECT:-false}" == true ]] || { echo 'status target mismatch' >&2; exit 7; }
    jq -n --arg project "$FAKE_PROJECT" '{success:true,command:"status",data:{count:1,instances:[{project:$project,state:"ready"}]}}'
    ;;
  cmd)
    if [[ -n "${FAKE_COMMAND_ARGS:-}" ]]; then printf '%s\n' "$@" >"$FAKE_COMMAND_ARGS"; fi
    [[ " $* " == *" --project-path $FAKE_PROJECT "* ]] || { echo 'command target mismatch' >&2; exit 7; }
    [[ "$HOME" == "$EXPECTED_HOME" ]] || { echo 'CLI home was changed' >&2; exit 7; }
    printf '%s\n' "$FAKE_RESPONSE"
    ;;
  *) exit 2 ;;
esac
