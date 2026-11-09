#!/usr/bin/env bash
# Closed mode starts with the brain-developer skill. This hook makes that switch impossible to skip, and
# keeps the brain out of closed mode altogether.
#
#   developer-gate.sh mark    PreToolUse on Skill, and UserPromptSubmit: when the brain-developer skill
#                             loads, remember it for this session.
#   developer-gate.sh check   PreToolUse on Edit, Write, NotebookEdit and Bash.
#
# check has three behaviours, picked by who is acting.
#
# The root brain (agent_type "brain", no agent_id) never implements, developer skill or not. It may
# write only the brain's data: ${BRAIN_HOME:-~/.local/share/brain}/{graph,voices}, a
# project's docs/brain, and Claude's scratchpad under /tmp/claude-*. In Bash it may not build, test,
# run the game, commit, merge, rebase, push, add a worktree, pin with taskset, run dotnet or cargo,
# or edit in place with sed -i or perl -i. graph-door.sh, beside this hook, then refuses direct writes
# to the graph for everyone: those go through the `brain` CLI. `git -C` on the data repo stays open for data commits,
# and so does everything else: reading, `brain`, git log, show and status.
# In a project the brain commits its own data and nothing else: `git add` takes only docs/brain
# paths, and `git commit` or `git -C <repo> commit` runs alone, as one command, and only when every
# staged path in <repo> (from -C, else the hook's cwd) is under docs/brain/.
#
# The immediate agent (agent_type "immediate", no agent_id) is a session the user launched himself with
# `claude --agent immediate` to make a small change in place. Launching it is the user's switch, so it passes without
# the marker. A subagent of that type has an agent_id and needs the marker like any worker.
#
# Everyone else, workers and non-brain sessions: refuse an edit, a build, a test, a commit or a game
# run until the brain-developer skill has loaded. Files under Claude Code's config directory,
# ${CLAUDE_CONFIG_DIR:-~/.claude}, Claude's scratchpad and the brain's data stay open, and so does `git -C` on the
# data repo: talking about how we work is not building. `git -C <repo> commit` counts as a commit like `git commit`.
#
# The marker is a file keyed by session in ${XDG_RUNTIME_DIR}, else in a directory of the user's own under
# ${TMPDIR:-/tmp}, so a reboot or a new login clears it in the usual case. A marker counts only in a directory that
# only the user owns and can open.
#
# Exits 2 with stderr to deny the tool call (Claude Code's PreToolUse blocking convention). Any failure in the hook
# exits 2 as well, so an error never switches the gate off.

# errtrace carries the trap into functions; without it a failure there exits 1, which lets the tool call through. A
# command substitution reports its failure to the hook instead of refusing itself, since bash 3.2 runs the trap there
# even when the substitution is a tested condition.
set -euo pipefail -o errtrace
trap '((BASH_SUBSHELL)) || { echo "BLOCKED by the developer gate: the hook failed at line $LINENO, so it refuses" >&2; exit 2; }' ERR
# lib.sh sits beside the hook in the harness; brain install links the hook there with one absolute link.
# shellcheck source=harness/hooks/lib.sh
source "$(dirname "$(readlink "${BASH_SOURCE[0]}" || printf '%s' "${BASH_SOURCE[0]}")")/lib.sh"

input=$(cat)
session=$(jq -r '.session_id // "unknown"' <<<"$input")
dir=$(marker_dir)
marker="$dir/$session"

case "${1:-}" in
mark)
    skill=$(jq -r '.tool_input.skill // ""' <<<"$input")
    prompt=$(jq -r '.prompt // ""' <<<"$input")
    if [[ "$skill" == "brain-developer" ]] || grep -qE "^[[:space:]]*/brain-developer$word_end" <<<"$prompt"; then
        own_dir "$dir"
        touch "$marker"
    fi
    exit 0
    ;;
check) ;;
*) exit 0 ;;
esac

tool=$(jq -r '.tool_name // ""' <<<"$input")
cmd=$(jq -r '.tool_input.command // ""' <<<"$input")
path=$(jq -r '.tool_input.file_path // .tool_input.notebook_path // ""' <<<"$input")
agent_type=$(jq -r '.agent_type // ""' <<<"$input")
agent_id=$(jq -r '.agent_id // ""' <<<"$input")
cwd=$(jq -r '.cwd // ""' <<<"$input")
brain_home="${BRAIN_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/brain}"

# git -C on the brain's data repo, then its subcommand: BRAIN_HOME in full or, when it lies in the user's home, after ~,
# $HOME or ${HOME}. A second global option, such as another -C or -c, does not follow.
brain_git="^[[:space:]]*git -C $(path_re "$brain_home")/?[[:space:]]+[^-[:space:]]"
# git and its -C argument. Every use anchors it at the start of the command.
git_c='git[[:space:]]+(-C[[:space:]]+[^[:space:]]+[[:space:]]+)?'
# Any git command line: global options such as -c, --no-pager or several -C may stand before the subcommand.
git_any="${word_start}git"'([[:space:]]+(-[Cc][[:space:]]*[^[:space:]]+|--(git-dir|work-tree|namespace|config-env)([[:space:]]+|=)[^[:space:]]+|-[^[:space:]]+))*[[:space:]]+'
closed="(dotnet|cargo)-guard\\.sh|run(-client)?\\.sh|shoot\\.sh|compare\\.sh|$git_any(commit|merge|rebase|push|worktree[[:space:]]+add)$word_end|taskset"
implementing="$closed|$word_start(dotnet|cargo)$word_end|$word_start(sed|perl)([^[:alnum:]_|;&][^|;&]*)?[[:space:]]-[a-zA-Z]*i"

# A file path as written, with . and .. resolved and symlinks kept, and as it resolves, symlinks followed.
written=$(path_written "${path:-.}")
resolved=$(path_resolved "${path:-.}")
data_resolved=$(path_resolved "$brain_home")

# data_file: exit 0 when the path is in the brain's data or a project's docs/brain, both as written and as it resolves,
# so neither .. nor a symlink leads out of it.
data_file() {
    case "$written" in
    "$brain_home"/graph/* | "$brain_home"/voices/* | */docs/brain/*) ;;
    *) return 1 ;;
    esac
    case "$resolved" in
    "$data_resolved"/graph/* | "$data_resolved"/voices/* | */docs/brain/*) return 0 ;;
    esac
    return 1
}

# scratch_file: exit 0 for a path in Claude's scratchpad, as written and as it resolves. On macOS /tmp is a link to
# /private/tmp.
scratch_file() { [[ "$written" == /tmp/claude-* && ("$resolved" == /tmp/claude-* || "$resolved" == /private/tmp/claude-*) ]]; }

# substitutes <cmd>: exit 0 when the command holds a command substitution other than a heredoc message, $(cat <<.
# shellcheck disable=SC2016 # the patterns match the literal text $( in the command
substitutes() {
    local rest=${1//'$(cat <<'/}
    [[ "$rest" == *'$('* ]]
}

# brain_data_chain <cmd>: exit 0 when every command of the chain is git -C on the brain's data repo. Quoted text is
# dropped, so a commit message may say anything, but a command substitution other than a heredoc message is refused.
brain_data_chain() {
    local bare seg
    substitutes "$1" && return 1
    bare=$(tr '\n' ' ' <<<"$1" | sed -E "s/-C \"([^\"'[:space:]]*)\"/-C \\1/g; s/-C '([^\"'[:space:]]*)'/-C \\1/g; s/\"[^\"]*\"/Q/g; s/'[^']*'/Q/g")
    grep -qE '[`#]|\$\(' <<<"$bare" && return 1
    while IFS= read -r seg; do
        grep -qE "$brain_git" <<<"$seg" || return 1
    done < <(split_commands "$bare")
}

# The repo a git command acts on: its -C argument with ~ and $HOME expanded, else the hook's cwd.
git_repo() {
    local dir
    dir=$(grep -oE "${git_c}" <<<"$1" | head -n 1 | sed -E 's/^git[[:space:]]+(-C[[:space:]]+)?//; s/[[:space:]]+$//; s/^["'"'"']//; s/["'"'"']$//' || true)
    [[ -n "$dir" ]] || dir="$cwd"
    dir="${dir/#\~/$HOME}"
    printf '%s\n' "${dir/#\$HOME/$HOME}"
}

# brain_data_git <cmd>: exit 0 when the command is one git add of docs/brain paths, or one commit
# whose staged paths all sit under docs/brain/. Quoted text is dropped first, so a commit message
# may say anything, but a command substitution other than a heredoc message is refused.
brain_data_git() {
    local bare repo staged arg docs_arg='(^|/)docs/brain(/|$)' docs_path='^docs/brain(/|$)'
    substitutes "$1" && return 1
    bare=$(tr '\n' ' ' <<<"$1" | sed -E "s/-C \"([^\"'[:space:]]*)\"/-C \\1/g; s/-C '([^\"'[:space:]]*)'/-C \\1/g; s/\"[^\"]*\"/Q/g; s/'[^']*'/Q/g")
    grep -qE '[;&|`#]' <<<"$bare" && return 1
    grep -qE "^[[:space:]]*${git_c}(add|commit)$word_end" <<<"$bare" || return 1
    repo=$(git_repo "$bare")
    if grep -qE "^[[:space:]]*${git_c}add$word_end" <<<"$bare"; then
        local found=0 args
        read -ra args <<<"$(sed -E "s/^[[:space:]]*${git_c}add//" <<<"$bare")"
        for arg in ${args[@]+"${args[@]}"}; do
            [[ "$arg" == -- ]] && continue
            [[ "$arg" == -* ]] && return 1
            [[ "$arg" =~ $docs_arg && "$arg" != *..* ]] || return 1
            found=1
        done
        return $((1 - found))
    fi
    grep -qE "[[:space:]](-[a-zA-Z]*[aio][a-zA-Z]*$word_end|--(all|include|only|pathspec-from-file)$word_end|--[[:space:]])" <<<"$bare" && return 1
    # A path after commit commits that path's working tree, whatever is staged: only docs/brain paths may follow.
    local tokens token value=0
    read -ra tokens <<<"$(sed -E "s/^[[:space:]]*${git_c}commit//" <<<"$bare")"
    for token in ${tokens[@]+"${tokens[@]}"}; do
        if ((value)); then
            value=0
            continue
        fi
        case "$token" in
        -m | -F | -C | -c | -t | --message | --file | --reuse-message | --reedit-message | --author | --date | --template | --cleanup | --fixup | --squash | --trailer) value=1 ;;
        -*) ;;
        *) [[ "$token" =~ $docs_path && "$token" != *..* ]] || return 1 ;;
        esac
    done
    staged=$(git -C "$repo" diff --cached --name-only 2>/dev/null) || return 1
    [[ -n "$staged" ]] || return 1
    ! grep -qvE '^docs/brain/' <<<"$staged"
}

if [[ "$agent_type" == "brain" && -z "$agent_id" ]]; then
    if [[ "$tool" == "Bash" ]]; then
        brain_data_chain "$cmd" && exit 0
        brain_data_git "$cmd" && exit 0
        grep -qE "$implementing|${git_any}add$word_end" <<<"$cmd" || exit 0
    else
        data_file && exit 0
        scratch_file && exit 0
    fi
    cat >&2 <<'EOF'
BLOCKED: the brain never implements. Not code, not the harness, not its own C#.

Send a `builder` agent once the user has decided what to build, with a brief that names the brain
documents that bind the area. A `validator` agent checks its work until it is satisfied and lands
it. Verify the validator's report before telling the user.
EOF
    exit 2
fi

if [[ "$agent_type" == "immediate" && -z "$agent_id" ]]; then
    exit 0
fi

[[ -e "$marker" ]] && own_dir "$dir" && exit 0

if [[ "$tool" == "Bash" ]]; then
    brain_data_chain "$cmd" && exit 0
    grep -qE "$closed" <<<"$cmd" || exit 0
else
    [[ "$written" == "${CLAUDE_CONFIG_DIR:-$HOME/.claude}"/* ]] && exit 0
    data_file && exit 0
    scratch_file && exit 0
fi

cat >&2 <<'EOF'
BLOCKED by the developer gate: this is closed-mode work, and the brain-developer skill is not loaded.

If the user decided what to build, load the `brain-developer` skill now (and the project's skill if it has one), then
retry. If the user is still exploring, this is open mode: talk, don't build.
EOF
exit 2
