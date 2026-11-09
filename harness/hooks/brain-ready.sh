#!/usr/bin/env bash
# The readiness check's read-only door.
#
#   brain-ready.sh bash   PreToolUse on Bash in the brain-ready agent: only `brain lookup|node|walk|subjects|check`,
#                         optionally after `--project <name|path>`, one command, no shell operators.
#
# Exits 2 with stderr to deny the tool call (Claude Code's PreToolUse blocking convention).

# Any failure exits 2 and refuses the tool call; a plain exit 1 would let it through.
set -euo pipefail -o errtrace
trap '((BASH_SUBSHELL)) || { echo "BLOCKED by the readiness door: the hook failed at line $LINENO, so it refuses" >&2; exit 2; }' ERR

input=$(cat)

case "${1:-}" in
bash)
    cmd=$(jq -r '.tool_input.command // ""' <<<"$input")
    bare=$(sed -E "s/\"[^\"\`\$]*\"/Q/g; s/'[^']*'/Q/g" <<<"$cmd")
    # shellcheck disable=SC2016 # the regex matches a literal $ and backtick in the command text
    if [[ "$cmd" != *$'\n'* ]] && grep -qE '^[[:space:]]*brain([[:space:]]+--project[[:space:]]+[^[:space:]]+)?[[:space:]]+(lookup|node|walk|subjects|check)([[:space:]]|$)' <<<"$bare" &&
        ! grep -qE '[;&|<>`$()\\]' <<<"$bare"; then
        exit 0
    fi
    cat >&2 <<'EOF'
BLOCKED: the readiness check reads only the graph. Run one `brain [--project <name>] lookup|node|walk|subjects|check`
command at a time, without pipes, redirects or chains. Read no code and run nothing else.
EOF
    exit 2
    ;;
esac
exit 0
