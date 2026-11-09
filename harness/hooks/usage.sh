#!/usr/bin/env bash
# PostToolUse hook on Agent, SendMessage and Skill, and SubagentStop hook: hands the hook's JSON to `brain record`, which
# appends what it carries to the usage log in the data repo as a fact: who spawned which agent with which brief, who
# messaged whom, which skill loaded, and the verdict a readiness check stopped on.
# It records and never refuses: a failure exits 1, which Claude Code shows and does not block on, never 2. It finds
# brain on PATH, else in LOCAL_BIN, where brain install links the CLI of a checkout. A brain it cannot find or run is
# recorded in the usage log, so the next session start tells the user, and the hook exits 0.

hooks="$(dirname "$(readlink "${BASH_SOURCE[0]}" || printf '%s' "${BASH_SOURCE[0]}")")"
# shellcheck source=harness/hooks/lib.sh
source "$hooks/lib.sh" 2>/dev/null || {
    echo "brain: $hooks/lib.sh is missing, so usage.sh cannot run brain; run brain install" >&2
    exit 1
}
if ! brain="$(brain_cli)"; then
    record_failure usage.sh "$(brain_missing)"
    exit 0
fi
"$brain" record
status=$?
((status == 0)) && exit 0
answered "$status" || record_failure usage.sh "brain record exited $status"
exit 1
