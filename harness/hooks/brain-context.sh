#!/usr/bin/env bash
# SessionStart hook: tell the session which brain scopes apply here, personal and global always, a
# project's docs/brain when the session starts inside that project. It prints `brain context`, a few
# lines of pointers and never content: the brain is pulled with the brain skill, never pushed whole.
# The call runs under the session's id, so the usage log counts it as the session's own, and its last line tells the
# user of the errors brain recorded since they were last told.
# It finds brain on PATH, else in LOCAL_BIN, where brain install links the CLI of a checkout. It never fails the session
# start, and never fails silently: what brain says on stderr is printed with the rest, and a brain it cannot find or
# run is said in a line and recorded in the usage log.

hooks="$(dirname "$(readlink "${BASH_SOURCE[0]}" || printf '%s' "${BASH_SOURCE[0]}")")"
# shellcheck source=harness/hooks/lib.sh
source "$hooks/lib.sh" 2>/dev/null || {
    echo "brain: $hooks/lib.sh is missing, so brain-context.sh cannot run brain; run brain install"
    exit 0
}
input="$(cat 2>/dev/null || true)"
cd "$(jq -r '.cwd // empty' <<<"$input" 2>/dev/null || pwd)" 2>/dev/null || true
session="$(jq -r '.session_id // empty' <<<"$input" 2>/dev/null || true)"
if ! brain="$(brain_cli)"; then
    problem="$(brain_missing)"
    record_failure brain-context.sh "$problem"
    echo "brain: $problem"
    exit 0
fi
# brain's stdout goes straight to ours through fd 3; its stderr is kept, to print with it and to record.
{ said="$(CLAUDE_CODE_SESSION_ID="${CLAUDE_CODE_SESSION_ID:-$session}" "$brain" context 2>&1 1>&3 3>&-)"; } 3>&1
status=$?
[[ -z "$said" ]] || printf '%s\n' "$said"
if ! answered "$status"; then
    problem="brain context exited $status: $(printf '%s\n' "$said" | grep -m 1 -v '^[[:space:]]*$')"
    record_failure brain-context.sh "$problem"
    echo "brain: $problem"
fi
exit 0
