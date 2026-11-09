#!/usr/bin/env bash
# PreToolUse hook on Bash: block `git worktree add` targets that are not
# under .worktrees/ or .claude/worktrees/.
#
# Triggered by ~/.claude/settings.json. Exits 2 with stderr to deny the
# tool call (Claude Code's convention for PreToolUse blocking).

# Any failure exits 2 and refuses the tool call; a plain exit 1 would let it through.
set -euo pipefail -o errtrace
trap '((BASH_SUBSHELL)) || { echo "BLOCKED by the worktree guard: the hook failed at line $LINENO, so it refuses" >&2; exit 2; }' ERR

input=$(cat)
cmd=$(jq -r '.tool_input.command // ""' <<<"$input")

# Fast path: not a worktree-add command.
# POSIX word edges, since macOS's grep does not promise \b.
if ! grep -qE '(^|[^[:alnum:]_])git[[:space:]]+(.*[[:space:]])?worktree[[:space:]]+add([^[:alnum:]_]|$)' <<<"$cmd"; then
    exit 0
fi

# Tokens after `worktree add` (greedy strip, matches last occurrence).
rest=$(sed -E 's/^.*worktree[[:space:]]+add[[:space:]]+//' <<<"$cmd")

# Walk tokens, skip flags (and their argument when known), grab first positional.
path=""
skip_next=0
# shellcheck disable=SC2086
set -- $rest
while [ $# -gt 0 ]; do
    if [ "$skip_next" = "1" ]; then
        skip_next=0
        shift
        continue
    fi
    case "$1" in
        -b|-B|--reason)
            skip_next=1
            ;;
        --*=*|-*)
            ;;
        *)
            path="$1"
            break
            ;;
    esac
    shift
done

# Could not parse — let it through rather than false-block.
[ -z "$path" ] && exit 0

case "$path" in
    .worktrees/*|./.worktrees/*|.claude/worktrees/*|./.claude/worktrees/*)
        exit 0
        ;;
esac

cat >&2 <<EOF
BLOCKED: \`git worktree add\` target '$path' is not under an allowed location.

Allowed (relative to repo root):
  .worktrees/<name>
  .claude/worktrees/<name>

Re-run with the path under one of those prefixes.
EOF
exit 2
