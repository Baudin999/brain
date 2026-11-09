#!/usr/bin/env bash
# refusal.sh <hook> [arg...]: runs a PreToolUse hook as Claude Code would and, when it refuses, records the refusal in
# the usage log. brain install puts it before every PreToolUse hook in the settings it writes, the data repo's own hooks
# included, and an agent file puts it before the hooks it names, so every refusal is recorded the same way.
#
# The hook gets the tool call's JSON on stdin, and its stdout, its stderr and its answer pass through unchanged: the
# hook alone answers. Its answer is exit 0 or exit 2. Any other status is a hook that failed, such as 1 for an unbound
# variable or 127 for a hook that is missing, and Claude Code would let the call run on it, so it becomes exit 2: a
# failed hook refuses. So does a call with no hook to run. On exit 2, Claude Code's refusal, `brain record --refused` gets the same JSON, the hook's name
# without .sh and the first line the hook said. It runs in the background with its output thrown away, so a brain that
# is missing, fails or hangs never turns a refusal into an allow or an allow into a refusal. It finds brain on PATH,
# else in LOCAL_BIN, where brain install links the CLI of a checkout. A brain it cannot find or run is recorded in the
# usage log, so the next session start tells the user.
#
# No set -e and no ERR trap: nothing here may end the script before it exits with the hook's own status.

(($#)) || {
    echo "BLOCKED by refusal.sh: it has no hook to run, so it refuses" >&2
    exit 2
}

hooks="$(dirname "$(readlink "${BASH_SOURCE[0]}" || printf '%s' "${BASH_SOURCE[0]}")")"
# shellcheck source=harness/hooks/lib.sh
source "$hooks/lib.sh" 2>/dev/null || echo "brain: $hooks/lib.sh is missing, so refusal.sh cannot record refusals; run brain install" >&2

input=$(cat)
# The hook's stdout goes straight to ours through fd 3; its stderr is kept to pass on and to read the reason from.
{ said=$(printf '%s\n' "$input" | "$@" 2>&1 1>&3 3>&-); } 3>&1
status=$?
if ((status != 0 && status != 2)); then
    said="BLOCKED: the hook $(basename "$1") exited $status, so it refuses${said:+$'\n'$said}"
    status=2
fi
[[ -z "$said" ]] || printf '%s\n' "$said" >&2

# Without lib.sh there is no brain to find, which was said above.
if ((status == 2)) && type brain_cli >/dev/null 2>&1; then
    if ! brain="$(brain_cli)"; then
        record_failure refusal.sh "$(brain_missing)"
    else
        reason=$(printf '%s\n' "$said" | grep -m 1 -v '^[[:space:]]*$')
        hook=$(basename "$1" .sh)
        {
            printf '%s\n' "$input" | "$brain" record --refused "$hook" --reason "$reason"
            recorded=$?
            answered "$recorded" || record_failure refusal.sh "brain record --refused exited $recorded"
        } </dev/null >/dev/null 2>&1 &
    fi
fi
exit "$status"
