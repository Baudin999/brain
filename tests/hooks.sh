#!/usr/bin/env bash
# Feeds fake hook JSON into the hooks under harness/hooks and asserts their exit codes. The developer gate: the root
# brain may write only the brain's data and never builds or commits in a project, and a worker without the
# brain-developer skill is refused until `mark` sees the skill load, and the immediate agent the user launched passes
# without it. The readiness check may only read the graph.
#
# Every case runs twice: first on this machine as it is, then as on macOS. The second run puts a PATH before the hooks
# where realpath and readlink refuse GNU's flags, grep and sed refuse -P, -i and the regex escapes POSIX does not
# define, such as \b and a back-reference in a pattern, and sha256sum is missing, with XDG_RUNTIME_DIR unset, so the
# developer marker lives under TMPDIR. HOOKS_BASH names the bash the second run and the hooks in it use, such as a
# bash 3.2 built for the check; it defaults to the bash running this script.
#
# HOME is a temp dir of the test's own, and the data repo sits at its default place in it, so a path written with ~ or
# $HOME names the test's data, never the user's. Claude Code's config directory is the default ~/.claude unless a case
# moves it with CLAUDE_CONFIG_DIR.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
gate="$root/harness/hooks/developer-gate.sh"
door="$root/harness/hooks/graph-door.sh"
session="hooks-test-$$-$RANDOM"
# shellcheck source=harness/hooks/lib.sh
source "$root/harness/hooks/lib.sh"
marker="$(marker_dir)/$session"
repo="$(mktemp -d)"
home="$(mktemp -d)"
work="$(mktemp -d)"
macos="$(mktemp -d)"
trap 'rm -f "$marker"; rm -rf "$repo" "$home" "$work" "$macos"' EXIT
export HOME="$home"
unset CLAUDE_CONFIG_DIR
data="$HOME/.local/share/brain"
mkdir -p "$data"
export BRAIN_HOME="$data"
# The hooks find brain on PATH before LOCAL_BIN. This run's PATH has no directory that holds one, so each case puts the
# brain it means on PATH or in LOCAL_BIN.
IFS=: read -ra dirs <<<"$PATH"
path=""
for dir in "${dirs[@]}"; do
    [[ -x "$dir/brain" ]] || path="${path:+$path:}$dir"
done
export PATH="$path"

failures=0
passes=0

# expect <code> <description> <json> [hook]: the developer gate's check by default
expect() {
    local want="$1" what="$2" json="$3" got=0
    if [ -n "${4:-}" ]; then "$4" <<<"$json" 2>/dev/null || got=$?; else "$gate" check <<<"$json" 2>/dev/null || got=$?; fi
    if [ "$got" = "$want" ]; then
        passes=$((passes + 1))
    else
        echo "hooks: FAIL $what: exit $got, want $want" >&2
        failures=$((failures + 1))
    fi
}

# door <code> <description> <json>: the graph door, which treats everyone alike
door() { expect "$1" "door: $2" "$3" "$door"; }

# brain <tool> <tool_input json> [cwd]: the root brain, no agent_id
brain() { jq -cn --arg s "$session" --arg t "$1" --argjson i "$2" --arg c "${3:-$data}" '{session_id: $s, tool_name: $t, tool_input: $i, agent_type: "brain", agent_id: null, cwd: $c}'; }
# immediate <tool> <tool_input json> [cwd]: the immediate agent the user launched himself, no agent_id
immediate() { jq -cn --arg s "$session" --arg t "$1" --argjson i "$2" --arg c "${3:-$data}" '{session_id: $s, tool_name: $t, tool_input: $i, agent_type: "immediate", agent_id: null, cwd: $c}'; }
# agent <type> <tool> <tool_input json> [cwd]: a subagent of that type
agent() { jq -cn --arg s "$session" --arg a "$1" --arg t "$2" --argjson i "$3" --arg c "${4:-$data}" '{session_id: $s, tool_name: $t, tool_input: $i, agent_type: $a, agent_id: "a1", cwd: $c}'; }
# worker <tool> <tool_input json> [cwd]: a general-purpose subagent
worker() { agent general-purpose "$@"; }
# The closed agents the brain sends: the hooks treat them exactly as they treat any worker.
closed_agents=(builder validator)
file() { jq -cn --arg p "$1" '{file_path: $p}'; }
bash_cmd() { jq -cn --arg c "$1" '{command: $c}'; }

game="$HOME/Projects/Games/afterlife"

# A repo with one commit and a linked worktree under .claude/worktrees.
git -C "$work" init -q -b main
mkdir -p "$work/src"
echo "class X {}" >"$work/src/X.cs"
git -C "$work" add src
git -C "$work" -c user.name=hooks -c user.email=hooks@localhost commit -q -m seed
wt="$work/.claude/worktrees/w"
git -C "$work" worktree add -q -b w "$wt"

expect 2 "brain root edits a .cs file" "$(brain Edit "$(file "$game/src/Afterlife/World.cs")")"
expect 0 "brain root writes a graph node" "$(brain Write "$(file "$data/graph/global/x.md")")"
expect 0 "brain root writes afterlife docs/brain" "$(brain Write "$(file "$game/docs/brain/graph/x.md")")"
expect 2 "brain root commits in a project" "$(brain Bash "$(bash_cmd "git -C $game commit -m x")")"
expect 0 "brain root commits the brain" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m x')")"
expect 0 "brain root commits the brain by its full path" "$(brain Bash "$(bash_cmd "git -C $data commit -m x")")"
expect 2 "brain root commits the tool repo" "$(brain Bash "$(bash_cmd 'git -C ~/Projects/brain commit -m x')")"
expect 2 "brain root commits a sibling of the brain" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain-old commit -m x')")"
expect 2 "brain root edits the brain's own C#" "$(brain Edit "$(file "$root/src/Brain/Cli.cs")")"
expect 2 "brain root edits a hook" "$(brain Edit "$(file "$root/harness/hooks/graph-door.sh")")"
expect 0 "worker without developer commits the brain" "$(worker Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m x')")"
expect 0 "brain root walks the brain" "$(brain Bash "$(bash_cmd 'brain walk x')")"
expect 2 "brain root edits in place with sed -i" "$(brain Bash "$(bash_cmd "sed -i s/a/b/ $game/x.cs")")"
expect 0 "brain root chains add and commit on the brain" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain add graph && git -C ~/.local/share/brain commit -m "x"')")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
expect 0 "brain root commits the brain with a quoted path" "$(brain Bash "$(bash_cmd 'git -C "$HOME/.local/share/brain" commit -m x')")"
expect 2 "brain root chains a build after a brain git" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain status; dotnet build')")"
expect 2 "brain root names the brain in a comment after a commit" "$(brain Bash "$(bash_cmd "git -C $game commit -m x # git -C ~/.local/share/brain")")"
expect 2 "brain root moves a brain git into the tool repo with a second -C" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain -C ~/Projects/brain commit -m x')")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
expect 2 "brain root runs a command inside a brain git message" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m "$(dotnet build)"')")"
expect 2 "brain root commits a project with a config option first" "$(brain Bash "$(bash_cmd "git -c core.editor=true -C $game commit -m x")")"
expect 2 "brain root commits a project with --no-pager first" "$(brain Bash "$(bash_cmd "git --no-pager -C $game commit -m x")")"
expect 2 "brain root edits code through .. from the scratchpad" "$(brain Edit "$(file "/tmp/claude-1/../..$root/src/Brain/Cli.cs")")"
expect 2 "brain root edits code through .. from the graph" "$(brain Edit "$(file "$data/graph/../../..$root/src/Brain/Cli.cs")")"
mkdir -p "$data/graph"
ln -s "$root/src" "$data/graph/escape"
expect 2 "brain root edits code through a symlink in the graph" "$(brain Edit "$(file "$data/graph/escape/Brain/Cli.cs")")"
rm "$data/graph/escape"
# The data repo and Claude Code's config directory are where BRAIN_HOME and CLAUDE_CONFIG_DIR put them, not where they
# sit by default.
mkdir -p "$HOME/elsewhere" "$HOME/config"
BRAIN_HOME="$HOME/elsewhere" expect 0 "brain root commits a data repo BRAIN_HOME moved" "$(brain Bash "$(bash_cmd 'git -C ~/elsewhere commit -m x')")"
BRAIN_HOME="$HOME/elsewhere" expect 2 "brain root commits the default data repo once BRAIN_HOME moved it" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m x')")"
BRAIN_HOME="$work" expect 0 "brain root commits a data repo outside the home by its full path" "$(brain Bash "$(bash_cmd "git -C $work commit -m x")")"
BRAIN_HOME="$HOME/elsewhere" door 2 "the door guards the graph of a data repo BRAIN_HOME moved" "$(brain Bash "$(bash_cmd 'echo x > ~/elsewhere/graph/x.md')")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
BRAIN_HOME="$HOME/elsewhere" door 2 "the door guards the moved graph written with \$HOME" "$(brain Bash "$(bash_cmd 'echo x > $HOME/elsewhere/graph/x.md')")"
expect 0 "worker without developer edits the config directory" "$(worker Edit "$(file "$HOME/.claude/rules/x.md")")"
CLAUDE_CONFIG_DIR="$HOME/config" expect 0 "worker without developer edits a config directory CLAUDE_CONFIG_DIR moved" "$(worker Edit "$(file "$HOME/config/rules/x.md")")"
CLAUDE_CONFIG_DIR="$HOME/config" expect 2 "worker without developer edits ~/.claude once CLAUDE_CONFIG_DIR moved it" "$(worker Edit "$(file "$HOME/.claude/rules/x.md")")"
# A project whose index the brain commits from: only docs/brain staged, then a .cs file beside it.
git -C "$repo" init -q
mkdir -p "$repo/docs/brain/graph" "$repo/src"
echo "# x" >"$repo/docs/brain/graph/x.md"
echo "class X {}" >"$repo/src/X.cs"
git -C "$repo" add docs/brain
# shellcheck disable=SC2016 # the command substitution belongs to the commit message under test
message="$(printf 'git -C %s commit -m "$(cat <<%sEOF%s\nSeed: x\nEOF\n)"' "$repo" "'" "'")"
expect 0 "brain root adds docs/brain in a project" "$(brain Bash "$(bash_cmd "git -C $repo add docs/brain/graph")")"
expect 2 "brain root adds a .cs file" "$(brain Bash "$(bash_cmd "git -C $repo add src/X.cs")")"
expect 2 "brain root adds everything" "$(brain Bash "$(bash_cmd "git -C $repo add -A")")"
expect 0 "brain root commits only docs/brain staged" "$(brain Bash "$(bash_cmd "git -C $repo commit -m 'Seed: x'")")"
expect 0 "brain root commits docs/brain from the repo's cwd" "$(brain Bash "$(bash_cmd "git commit -m x")" "$repo")"
expect 0 "brain root commits docs/brain with a heredoc message" "$(brain Bash "$(bash_cmd "$message")")"
expect 2 "brain root commits with -a" "$(brain Bash "$(bash_cmd "git -C $repo commit -am x")")"
expect 2 "brain root commits a .cs path beside the staged docs/brain" "$(brain Bash "$(bash_cmd "git -C $repo commit -m 'x' src/X.cs")")"
expect 2 "brain root commits a .cs path after an unquoted message" "$(brain Bash "$(bash_cmd "git -C $repo commit -m x src/X.cs")")"
expect 0 "brain root commits a docs/brain path" "$(brain Bash "$(bash_cmd "git -C $repo commit -m 'x' docs/brain/graph")")"
expect 2 "brain root commits paths from a file" "$(brain Bash "$(bash_cmd "git -C $repo commit -m x --pathspec-from-file=list")")"
expect 2 "brain root chains add and commit" "$(brain Bash "$(bash_cmd "git -C $repo add docs/brain && git -C $repo commit -m x")")"
git -C "$repo" add src/X.cs
expect 2 "brain root commits with a .cs file staged" "$(brain Bash "$(bash_cmd "git -C $repo commit -m x")")"
expect 2 "worker without developer commits with git -C" "$(worker Bash "$(bash_cmd "git -C $repo commit -m x")")"
expect 2 "worker without developer edits a .cs file" "$(worker Edit "$(file "$game/src/Afterlife/World.cs")")"
for a in "${closed_agents[@]}"; do
    expect 2 "$a without developer commits with git -C" "$(agent "$a" Bash "$(bash_cmd "git -C $repo commit -m x")")"
    expect 2 "$a without developer runs the guard" "$(agent "$a" Bash "$(bash_cmd "scripts/dotnet-guard.sh build")")"
    expect 2 "$a without developer edits a .cs file" "$(agent "$a" Edit "$(file "$game/src/Afterlife/World.cs")")"
done

# The immediate agent the user launched passes without the marker, in place; the brain root and a subagent of the same
# type without the marker are still refused.
expect 0 "immediate without developer edits a .cs file" "$(immediate Edit "$(file "$game/src/Afterlife/World.cs")")"
expect 0 "immediate without developer edits a file in a primary worktree" "$(immediate Edit "$(file "$work/src/X.cs")")"
expect 0 "immediate without developer adds a .cs file" "$(immediate Bash "$(bash_cmd "git -C $repo add src/X.cs")")"
expect 0 "immediate without developer commits with git -C" "$(immediate Bash "$(bash_cmd "git -C $repo commit -m x")")"
expect 0 "immediate without developer commits in a primary worktree" "$(immediate Bash "$(bash_cmd "git commit -m x")" "$work")"
expect 0 "immediate without developer merges" "$(immediate Bash "$(bash_cmd "git -C $work merge w")")"
expect 0 "immediate without developer rebases" "$(immediate Bash "$(bash_cmd "git rebase main")" "$wt")"
expect 0 "immediate without developer pushes" "$(immediate Bash "$(bash_cmd "git push")" "$work")"
expect 0 "immediate without developer runs the guard" "$(immediate Bash "$(bash_cmd "scripts/dotnet-guard.sh build")" "$work")"
expect 2 "immediate as a subagent without developer edits a .cs file" "$(agent immediate Edit "$(file "$game/src/Afterlife/World.cs")")"
expect 2 "immediate as a subagent without developer commits with git -C" "$(agent immediate Bash "$(bash_cmd "git -C $repo commit -m x")")"
expect 2 "brain root beside immediate still edits a .cs file" "$(brain Edit "$(file "$game/src/Afterlife/World.cs")")"
expect 2 "brain root beside immediate still commits in a primary worktree" "$(brain Bash "$(bash_cmd "git commit -m x")" "$work")"
expect 2 "brain root beside immediate still runs the guard" "$(brain Bash "$(bash_cmd "scripts/dotnet-guard.sh build")" "$work")"
expect 2 "worker beside immediate without developer edits a .cs file" "$(worker Edit "$(file "$game/src/Afterlife/World.cs")")"

jq -cn --arg s "$session" '{session_id: $s, tool_name: "Skill", tool_input: {skill: "developer"}}' | "$gate" mark
expect 2 "worker after the old skill name developer edits a .cs file" "$(worker Edit "$(file "$game/src/Afterlife/World.cs")")"
jq -cn --arg s "$session" '{session_id: $s, prompt: "/brain-developer go"}' | "$gate" mark
expect 0 "worker after the prompt /brain-developer edits a .cs file" "$(worker Edit "$(file "$wt/src/X.cs")")"
rm -f "$marker"
expect 2 "worker with the marker cleared edits a .cs file" "$(worker Edit "$(file "$game/src/Afterlife/World.cs")")"
jq -cn --arg s "$session" '{session_id: $s, tool_name: "Skill", tool_input: {skill: "brain-developer"}}' | "$gate" mark
expect 0 "worker with developer edits a .cs file in a linked worktree" "$(worker Edit "$(file "$wt/src/X.cs")")"

# No hook refuses work for standing in a repo's primary worktree: building in a linked worktree is the worker's
# process, taught by the brain-developer skill.
expect 0 "worker with developer edits a file in a primary worktree" "$(worker Edit "$(file "$work/src/X.cs")")"
expect 0 "worker with developer commits in a primary worktree" "$(worker Bash "$(bash_cmd "git commit -m x")" "$work")"
expect 0 "worker with developer merges into a primary worktree" "$(worker Bash "$(bash_cmd "git -C $work merge w")" "$wt")"
expect 0 "worker with developer sed -i edits a file in a primary worktree" "$(worker Bash "$(bash_cmd "sed -i 's/X/Y/' src/X.cs")" "$work")"
expect 0 "worker with developer commits the brain" "$(worker Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m x')")"
expect 0 "builder with developer edits a .cs file in a linked worktree" "$(agent builder Edit "$(file "$wt/src/X.cs")")"
expect 0 "builder with developer commits in a linked worktree" "$(agent builder Bash "$(bash_cmd "git commit -m x")" "$wt")"
expect 0 "validator with developer runs the guard" "$(agent validator Bash "$(bash_cmd "scripts/dotnet-guard.sh build")" "$wt")"
expect 0 "validator with developer rebases the worktree" "$(agent validator Bash "$(bash_cmd "git rebase main")" "$wt")"
expect 0 "validator with developer fast-forwards the primary" "$(agent validator Bash "$(bash_cmd "git -C $work merge --ff-only w")" "$wt")"
expect 0 "validator with developer removes the worktree" "$(agent validator Bash "$(bash_cmd "git -C $work worktree remove $wt")" "$wt")"
expect 0 "brain root still writes a project's docs/brain in its primary" "$(brain Write "$(file "$work/docs/brain/graph/x.md")")"
expect 0 "brain root still commits the brain" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain commit -m x')")"
expect 2 "brain root still commits in a primary worktree" "$(brain Bash "$(bash_cmd "git commit -m x")" "$work")"

# The graph door: the graph is written only through the brain CLI, by the root brain and by a worker
# with the developer skill loaded alike. voices stays open.
door 2 "brain root writes a graph node" "$(brain Write "$(file "$data/graph/global/x.md")")"
door 2 "brain root edits a project's graph node" "$(brain Edit "$(file "$game/docs/brain/graph/x.md")")"
door 0 "brain root writes a voice" "$(brain Write "$(file "$data/voices/x.md")")"
door 2 "brain root writes a graph node through /./" "$(brain Write "$(file "$data/./graph/global/x.md")")"
door 2 "brain root writes a graph node through .." "$(brain Write "$(file "$data/voices/../graph/global/x.md")")"
door 2 "brain root writes a project node through //" "$(brain Write "$(file "$game/docs/brain//graph/x.md")")"
door 2 "brain root redirects into the graph" "$(brain Bash "$(bash_cmd 'echo x > ~/.local/share/brain/graph/global/x.md')")"
door 2 "brain root writes a node with a heredoc" "$(brain Bash "$(bash_cmd "$(printf 'cat > %s/docs/brain/graph/x.md <<EOF\nx\nEOF' "$game")")")"
door 0 "brain root reads a node from the brain's cwd" "$(brain Bash "$(bash_cmd 'cat graph/global/x.md')")"
door 0 "brain root adds a node with the CLI" "$(brain Bash "$(bash_cmd 'brain add global x "a subject that lasts"')")"
door 0 "brain root stages the graph" "$(brain Bash "$(bash_cmd 'git -C ~/.local/share/brain add graph')")"
door 0 "brain root commits the graph" "$(brain Bash "$(bash_cmd "git -C ~/.local/share/brain commit -m 'graph: x > y'")")"
door 2 "worker notebook-edits a graph file" "$(worker NotebookEdit "$(jq -cn --arg p "$game/docs/brain/graph/x.ipynb" '{notebook_path: $p}')")"
door 0 "worker edits a .cs file" "$(worker Edit "$(file "$game/src/Afterlife/World.cs")")"
door 2 "worker tees into a graph node" "$(worker Bash "$(bash_cmd "echo x | tee -a $game/docs/brain/graph/x.md")")"
door 2 "worker edits a node with sed -i" "$(worker Bash "$(bash_cmd "sed -i s/a/b/ $game/docs/brain/graph/x.md")")"
door 2 "worker edits a node with perl -pi" "$(worker Bash "$(bash_cmd "perl -pi -e s/a/b/ docs/brain/graph/x.md")" "$game")"
door 2 "worker copies into the graph" "$(worker Bash "$(bash_cmd "cp /tmp/x.md $game/docs/brain/graph/x.md")")"
door 2 "worker moves a node" "$(worker Bash "$(bash_cmd "mv docs/brain/graph/x.md docs/brain/graph/y.md")" "$game")"
door 2 "worker removes the graph" "$(worker Bash "$(bash_cmd "cd $game && rm -rf docs/brain/graph")")"
door 0 "worker copies a node out" "$(worker Bash "$(bash_cmd "cp $game/docs/brain/graph/x.md /tmp/x.md")")"
door 0 "worker git rm a node" "$(worker Bash "$(bash_cmd 'git rm docs/brain/graph/x.md')" "$game")"
door 0 "worker reads the graph's history" "$(worker Bash "$(bash_cmd 'git log -- docs/brain/graph')" "$game")"
door 0 "worker greps the graph into the scratchpad" "$(worker Bash "$(bash_cmd 'grep -r x docs/brain/graph 2>/dev/null | tee /tmp/claude-1/out')" "$game")"
door 0 "worker shows an old node" "$(worker Bash "$(bash_cmd 'git show HEAD:docs/brain/graph/x.md > /tmp/claude-1/x.md')" "$game")"
for a in "${closed_agents[@]}"; do
    door 2 "$a writes a graph node" "$(agent "$a" Write "$(file "$game/docs/brain/graph/x.md")")"
    door 2 "$a edits a node with sed -i" "$(agent "$a" Bash "$(bash_cmd "sed -i s/a/b/ $game/docs/brain/graph/x.md")")"
    door 0 "$a edits a .cs file" "$(agent "$a" Edit "$(file "$game/src/Afterlife/World.cs")")"
done
door 2 "immediate writes a graph node" "$(immediate Write "$(file "$game/docs/brain/graph/x.md")")"
door 2 "immediate redirects into the graph" "$(immediate Bash "$(bash_cmd 'echo x > ~/.local/share/brain/graph/global/x.md')")"
door 2 "immediate edits a node with sed -i" "$(immediate Bash "$(bash_cmd "sed -i s/a/b/ $game/docs/brain/graph/x.md")")"
door 0 "immediate edits a .cs file" "$(immediate Edit "$(file "$game/src/Afterlife/World.cs")")"

# A conversation's trails: brain serve writes them, and no session does, the sitting's own open agent included. Its
# research/ stays open to the open agent, in a project and in the data repo alike.
talk="$game/docs/brain/conversations/2026-11-02-abcdef"
mine="$data/conversations/personal/2026-11-02-abcdef"
door 2 "brain root writes a project conversation's trail" "$(brain Write "$(file "$talk/trails/1.md")")"
door 2 "brain root edits a personal conversation's trail" "$(brain Edit "$(file "$mine/trails/2.md")")"
door 2 "brain root writes a trail through .." "$(brain Write "$(file "$talk/research/../trails/1.md")")"
door 2 "brain root redirects into a trail" "$(brain Bash "$(bash_cmd "echo x >> $mine/trails/1.md")")"
door 2 "brain root redirects into a trail from the conversation's folder" "$(brain Bash "$(bash_cmd "echo x > trails/1.md")" "$talk")"
door 2 "worker tees into a trail" "$(worker Bash "$(bash_cmd "echo x | tee $talk/trails/1.md")")"
door 2 "worker removes a sitting's trails" "$(worker Bash "$(bash_cmd "rm -rf $talk/trails")")"
door 0 "brain root reads a trail" "$(brain Bash "$(bash_cmd "cat $mine/trails/1.md")")"
door 0 "brain root writes research in a project conversation" "$(brain Write "$(file "$talk/research/generics.md")")"
door 0 "brain root stores research in a personal conversation" "$(brain Bash "$(bash_cmd "cat > $mine/research/tea.md <<EOF
x
EOF")")"
expect 0 "brain root writes research in a project conversation" "$(brain Write "$(file "$talk/research/generics.md")")"
expect 0 "brain root stores research in a personal conversation" "$(brain Bash "$(bash_cmd "cat > $mine/research/tea.md <<EOF
x
EOF")")"
expect 0 "builder with developer writes a spike in a conversation's TEMP" "$(agent builder Write "$(file "$talk/TEMP/spike/Program.cs")")"

# The readiness check may only read the graph.
ready="$root/harness/hooks/brain-ready.sh"
ready_bash() { "$ready" bash; }
readiness_bash() { expect "$1" "ready bash: $2" "$(worker Bash "$(bash_cmd "$3")")" ready_bash; }
readiness_bash 0 "looks up the area" 'brain lookup "readiness gate"'
readiness_bash 0 "walks a node" "brain walk global:brain-judge --hops 2"
readiness_bash 0 "lists subjects" "brain subjects global"
readiness_bash 0 "checks the graph" "brain check"
readiness_bash 0 "looks up in a named project" "brain --project studio lookup readiness"
readiness_bash 0 "walks a node of a project path" "brain --project ~/Projects/studio walk wg-19"
readiness_bash 2 "adds a node in a named project" 'brain --project studio add project x "y"'
readiness_bash 2 "adds a node" 'brain add global x "y"'
readiness_bash 2 "reads code" "cat src/Brain/Cli.cs"
readiness_bash 2 "pipes a lookup" "brain lookup x | head"
readiness_bash 2 "chains a command" "brain check && touch x"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
readiness_bash 2 "substitutes a command" 'brain lookup "$(cat x)"'
readiness_bash 2 "redirects output to a file" "brain check > $data/x"
readiness_bash 2 "runs a second line" $'brain check\ntouch x'

# The user's gate: `brain land <id>` runs alone and without a prompt in every permission mode and in a session nobody
# attends, since only brain's window answers it. A session never runs brain's window or reaches its server, though a
# quoted mention of the window passes, and the pending writes are guarded like the graph.
id="0123456789ab"
# hook_json <event> <command> [mode]
hook_json() { jq -cn --arg e "$1" --arg c "$2" --arg m "${3:-default}" '{hook_event_name: $e, tool_use_id: "toolu_1", tool_name: "Bash", tool_input: {command: $c}, permission_mode: $m, cwd: "/"}'; }
# check <description> <command...>: passes when the command succeeds
check() {
    local what="$1"
    shift
    if "$@"; then passes=$((passes + 1)); else
        echo "hooks: FAIL gate: $what" >&2
        failures=$((failures + 1))
    fi
}
# lands <description> <mode> [hook]: brain land alone exits 0 and is allowed without a prompt.
lands() {
    local decision
    decision="$("${3:-$door}" <<<"$(hook_json PreToolUse "brain land $id" "$2")" | jq -r .hookSpecificOutput.permissionDecision)" || decision=refused
    check "$1" test "$decision" = allow
}
lands "brain land alone runs without a prompt" default
lands "bypass mode lands a pending write through the window" bypassPermissions
lands "dontAsk mode lands a pending write through the window" dontAsk
lands "acceptEdits mode lands a pending write through the window" acceptEdits
unattended() { CLAUDE_CODE_SESSION_ATTENDED=0 "$door"; }
lands "an unattended session lands a pending write through the window" default unattended
headless() { env -u CLAUDE_CODE_SESSION_ATTENDED "$door"; }
lands "a session without the attended flag lands a pending write through the window" bypassPermissions headless
door 2 "bypass mode still runs brain land only alone" "$(hook_json PreToolUse "brain land $id; true" bypassPermissions)"
door 0 "the old PostToolUse half does nothing" "$(hook_json PostToolUse "brain land $id")"
door 2 "brain land chained with another command" "$(hook_json PreToolUse "brain land $id && true")"
door 2 "brain land inside a shell" "$(hook_json PreToolUse "bash -c 'brain land $id'")"
door 2 "a quoted brain land in a chain" "$(hook_json PreToolUse "true; \"brain\" land $id")"
door 2 "a split brain land in a chain" "$(hook_json PreToolUse "true; b''rain land $id")"
door 2 "a session starts brain serve" "$(hook_json PreToolUse "brain serve")"
door 2 "a session opens brain ui" "$(hook_json PreToolUse "brain ui")"
door 2 "a session starts brain serve in the background" "$(hook_json PreToolUse "nohup brain --project x serve &")"
door 2 "a session starts a quoted brain serve" "$(hook_json PreToolUse "\"brain\" serve")"
door 2 "a session starts a split brain ui" "$(hook_json PreToolUse "b''rain u\\i")"
door 2 "a session starts brain serve by its path" "$(hook_json PreToolUse "/usr/local/bin/brain 2>/dev/null serve")"
door 2 "a session starts brain serve after a chain" "$(hook_json PreToolUse "git status && brain serve")"
door 2 "a session starts brain serve after a line" "$(hook_json PreToolUse $'cat <<EOF\nbrain serve\nEOF\nbrain serve')"
door 2 "a session starts brain ui in a subshell" "$(hook_json PreToolUse "(brain ui)")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
door 2 "a session starts brain serve in a quoted substitution" "$(hook_json PreToolUse 'echo "$(brain serve)"')"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
door 2 "a session starts brain ui in backticks" "$(hook_json PreToolUse 'echo "`brain ui`"')"
door 2 "a session starts brain serve in a shell's -c" "$(hook_json PreToolUse "bash -ec 'true; brain serve'")"
door 2 "a session starts brain serve in a nested shell" "$(hook_json PreToolUse "sh -c \"bash -c 'brain serve'\"")"
door 2 "a session starts brain ui through eval" "$(hook_json PreToolUse "eval 'brain ui'")"
door 2 "a session pipes brain serve into a shell" "$(hook_json PreToolUse "echo 'brain serve' | sh")"
door 2 "a session feeds brain ui to bash in a here-document" "$(hook_json PreToolUse $'bash <<EOF\nbrain ui\nEOF')"
door 2 "a session starts brain serve in -c after --" "$(hook_json PreToolUse "bash -c -- 'brain serve'")"
door 2 "a session starts brain serve in -c after an option" "$(hook_json PreToolUse "sh -c -e 'brain serve'")"
door 2 "a session starts brain serve in -c after -o and its argument" "$(hook_json PreToolUse "bash -c -o pipefail 'brain serve'")"
door 2 "a session starts brain serve in fish's -c" "$(hook_json PreToolUse "fish -c 'brain serve'")"
door 2 "a session starts brain serve in script's -c" "$(hook_json PreToolUse "script -qc 'brain serve' /dev/null")"
door 2 "a session starts brain serve in su's -c" "$(hook_json PreToolUse "su -c 'brain serve' alice")"
door 2 "a session starts brain ui in su's -c after the user" "$(hook_json PreToolUse "su alice -c 'brain ui'")"
door 2 "a session starts brain ui in su's --command=" "$(hook_json PreToolUse "su --command='brain ui' alice")"
door 2 "a session sources brain serve from a here-string" "$(hook_json PreToolUse ". /dev/stdin <<< 'brain serve'")"
door 2 "a session sources brain ui with source" "$(hook_json PreToolUse "source /dev/stdin <<< 'brain ui'")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
door 2 "a session starts brain serve in an unquoted here-document" "$(hook_json PreToolUse $'cat <<EOF\n$(brain serve)\nEOF')"
door 2 "a session starts brain ui in backticks in an unquoted here-document" "$(hook_json PreToolUse $'cat <<EOF\n`brain ui`\nEOF')"
door 2 "a session starts brain serve across a continued line" "$(hook_json PreToolUse $'brain \\\n    serve')"
door 2 "a session starts brain serve after an assignment" "$(hook_json PreToolUse "x=1 brain serve")"
door 2 "a session starts brain ui inside an if" "$(hook_json PreToolUse "if true; then brain ui; fi")"
long_message=$(printf 'Line %s names brain serve in a long message.\n' $(seq 1 300))
door 0 "a long quoted commit message that names brain serve" "$(hook_json PreToolUse "git commit -m \"$long_message\"")"
too_long=$(printf 'Line %s names brain serve in a long message.\n' $(seq 1 400))
door 2 "a command past the walk's length that names brain serve" "$(hook_json PreToolUse "git commit -m \"$too_long\"")"
door 0 "a command past the walk's length that names neither" "$(hook_json PreToolUse "git commit -m \"${too_long//brain/the CLI}\"")"
door 0 "a commit message names brain serve" "$(hook_json PreToolUse "git commit -m 'Narrow the refusal of brain serve and brain ui'")"
# shellcheck disable=SC2016 # the command text is under test, unexpanded
door 0 "a commit message in a here-document names brain serve" "$(hook_json PreToolUse $'git commit -m "$(cat <<\'EOF\'\nNarrow the refusal of brain serve (and brain ui)\n\nIt\'s quoted.\nEOF\n)"')"
door 0 "a search for brain serve" "$(hook_json PreToolUse "grep -rn 'brain serve' README.md")"
door 0 "a search for brain serve or brain ui" "$(hook_json PreToolUse "rg \"brain (serve|ui)\" harness")"
door 0 "a doc edit that names brain ui" "$(hook_json PreToolUse "sed -i 's/brain serve/brain ui/' README.md")"
door 0 "a here-document that names brain serve" "$(hook_json PreToolUse $'cat > notes.md <<EOF\nrun brain serve\nEOF')"
door 0 "a comment that names brain serve" "$(hook_json PreToolUse "true # brain serve")"
door 0 "a lookup for the word serve" "$(hook_json PreToolUse "brain lookup serve")"
door 0 "a search for brain serve in the working directory" "$(hook_json PreToolUse "grep -rn 'brain serve' .")"
door 0 "a count of brain serve" "$(hook_json PreToolUse "grep -c 'brain serve' README.md")"
door 0 "a quoted here-document that names a substitution" "$(hook_json PreToolUse $'cat > notes.md <<\'EOF\'\nrun $(brain serve)\nEOF')"
door 2 "a session curls the answer call" "$(hook_json PreToolUse "curl -X POST http://127.0.0.1:47474/api/answer -d '{}'")"
door 2 "a session curls the window server on localhost" "$(hook_json PreToolUse "curl localhost:47474/api/pending")"
door 2 "a session reaches the window server on ::1" "$(hook_json PreToolUse "curl 'http://[::1]:47474/api/state'")"
door 2 "a session reaches the window server through nc" "$(hook_json PreToolUse "nc 127.0.0.1 47474")"
door 2 "a session reaches the window server by another host name" "$(hook_json PreToolUse "curl http://my-box:47474/")"
door 2 "a session names the launch call" "$(hook_json PreToolUse "wget -qO- --post-data x 127.1/api/launch")"
door 2 "a session names the call that drives a sitting's agent" "$(hook_json PreToolUse "wget -qO- --post-data x 127.1/api/conversation/say")"
door 2 "a session names the conversations list" "$(hook_json PreToolUse "wget -qO- 127.1/api/conversations")"
door 2 "a session names the call that drives a chat's agent" "$(hook_json PreToolUse "wget -qO- --post-data x 127.1/api/chat/say")"
door 2 "a session names a chat" "$(hook_json PreToolUse "wget -qO- '127.1/api/chat?id=x'")"
door 2 "a session names the registry of the window's agents" "$(hook_json PreToolUse "wget -qO- 127.1/api/agents")"
door 0 "a word that starts with chat passes" "$(hook_json PreToolUse "ls docs/api/chatter")"
port_moved() { BRAIN_PORT=5000 "$door"; }
expect 2 "door: a session reaches a moved window server" "$(hook_json PreToolUse "curl 127.0.0.1:5000/api/state")" port_moved
door 0 "another local port stays open" "$(hook_json PreToolUse "curl http://127.0.0.1:11434/api/embed")"
door 0 "the port number elsewhere stays open" "$(hook_json PreToolUse "echo 474740")"
door 2 "a redirect into the pending writes" "$(brain Bash "$(bash_cmd "echo x > $data/cache/pending/answers/x.json")")"
door 2 "a Write into the pending writes" "$(brain Write "$(file "$data/cache/pending/abc.json")")"
door 2 "a Write of an answer" "$(brain Write "$(file "$data/cache/pending/answers/$id.json")")"
door 2 "a Write of an answer to a tool's question" "$(brain Write "$(file "$data/cache/answers/disputes/q1.json")")"
door 2 "a worker's Edit of an answer to a tool's question" "$(worker Edit "$(file "$data/cache/answers/disputes/q1.json")")"
door 2 "a redirect into the answers to tools' questions" "$(brain Bash "$(bash_cmd "echo '{}' > $data/cache/answers/disputes/q1.json")")"
door 2 "a removal of an answer to a tool's question" "$(brain Bash "$(bash_cmd "rm $data/cache/answers/disputes/q1.json")")"
door 2 "a session curls the question answer call" "$(hook_json PreToolUse "curl -X POST http://127.0.0.1:47474/api/answer/question -d '{}'")"
door 0 "a read of an answer to a tool's question" "$(brain Bash "$(bash_cmd "cat $data/cache/answers/disputes/q1.json")")"
door 2 "a Write of the sources brain serve runs" "$(brain Write "$(file "$data/sources.tsv")")"
door 2 "a worker's Edit of the sources brain serve runs" "$(worker Edit "$(file "$data/sources.tsv")")"
door 2 "a redirect into the sources brain serve runs" "$(brain Bash "$(bash_cmd "printf 'x\\tid\\n' >> $data/sources.tsv")")"
door 2 "a relative redirect into the sources from the data repo" "$(brain Bash "$(bash_cmd "echo x >> sources.tsv")" "$data")"
door 0 "a read of the sources brain serve runs" "$(brain Bash "$(bash_cmd "cat $data/sources.tsv")")"
door 0 "a plain brain add stages" "$(hook_json PreToolUse "brain add global x 'A title'")"
door 2 "a Write into the usage log" "$(brain Write "$(file "$data/usage/2026-11.jsonl")")"
door 2 "a worker's Edit of the usage log" "$(worker Edit "$(file "$data/usage/2026-11.jsonl")")"
door 2 "a redirect into the usage log" "$(brain Bash "$(bash_cmd "echo '{}' >> $data/usage/2026-11.jsonl")")"
door 2 "a removal of the usage log" "$(brain Bash "$(bash_cmd "rm -r $data/usage")")"
door 0 "a read of the usage log" "$(brain Bash "$(bash_cmd "cat $data/usage/2026-11.jsonl")")"
door 0 "git commits the usage log with the data repo" "$(brain Bash "$(bash_cmd "git -C $data add usage")")"

# The usage hook hands the JSON of an Agent, SendMessage or Skill call to brain record, which logs it in the data
# repo's usage/. It never refuses: a failure exits 1, which Claude Code does not block on, and without brain it is
# silent.
usage="$root/harness/hooks/usage.sh"
[[ -x "$root/cache/publish/brain" ]] || { echo "hooks: $root/cache/publish/brain is missing; run ./build.sh" >&2; exit 1; }
mkdir -p "$work/bin"
ln -sf "$root/bin/brain" "$work/bin/brain"
uses() { LOCAL_BIN="$work/bin" "$usage"; }
spawn="$(jq -cn --arg s "$session" '{session_id: $s, hook_event_name: "PostToolUse", tool_name: "Agent", agent_type: "brain", tool_input: {subagent_type: "builder", prompt: "x"}, tool_response: {agentId: "b1", resolvedModel: "opus"}}')"
message="$(jq -cn --arg s "$session" '{session_id: $s, hook_event_name: "PostToolUse", tool_name: "SendMessage", agent_id: "v1", agent_type: "validator", tool_input: {to: "b1", message: "fix"}}')"
expect 0 "usage: an Agent call is recorded" "$spawn" uses
expect 0 "usage: a SendMessage is recorded" "$message" uses
logged() { cat "$data"/usage/*.jsonl 2>/dev/null || true; }
check "usage: the spawn names the builder it started" grep -q "\"spawned\":\"b1\"" <<<"$(logged)"
check "usage: the message names its sender and recipient" grep -q "\"agentId\":\"v1\",\"agentType\":\"validator\",\"to\":\"b1\"" <<<"$(logged)"
expect 1 "usage: a record that fails exits 1, never 2" "not json" uses
on_path() { PATH="$work/bin:$PATH" LOCAL_BIN="$work/none" "$usage"; }
expect 0 "usage: brain on PATH is found before LOCAL_BIN" "$spawn" on_path
check "usage: brain on PATH recorded the spawn" test "$(grep -c '"spawned":"b1"' <<<"$(logged)")" = 2
# errors <pattern>: the error events in the usage log that match.
errors() { grep '"event":"error"' <<<"$(logged)" | grep "$1" || true; }
no_brain() { LOCAL_BIN="$work/none" "$usage"; }
expect 0 "usage: without brain the hook still exits 0" "$spawn" no_brain
check "usage: without brain the hook records the failure" test -n "$(errors '"failed":"usage.sh","problem":"the brain CLI is neither on PATH nor in '"$work/none"'; run brain install, or put brain on PATH"')"
mkdir -p "$work/crashing"
printf '#!/bin/sh\ncat >/dev/null\nexit 134\n' >"$work/crashing/brain"
chmod +x "$work/crashing/brain"
crashing() { LOCAL_BIN="$work/crashing" "$usage"; }
expect 1 "usage: a brain that crashes exits 1, never 2" "$spawn" crashing
check "usage: a brain that crashes is recorded" test -n "$(errors '"failed":"usage.sh","problem":"brain record exited 134"')"
check "usage: a record that brain refused is not recorded again" test -z "$(errors '"problem":"brain record exited 1"')"
context_hook() { env -u CLAUDE_CODE_SESSION_ID LOCAL_BIN="$work/bin" "$root/harness/hooks/brain-context.sh" >"$work/context.out"; }
expect 0 "usage: the session start hook runs brain context" "$(jq -cn --arg c "$data" '{session_id: "s-ctx", cwd: $c}')" context_hook
check "usage: brain context at session start counts as the session's" grep -q '"session":"s-ctx","caller":"ai","command":"context"' <<<"$(logged)"
check "usage: the session start tells of the newest error" grep -q '^errors    2 errors since you were last told; the latest, at .*: usage.sh failed: brain record exited 134; ' "$work/context.out"
expect 0 "usage: the session start runs brain context again" "$(jq -cn --arg c "$data" '{session_id: "s-ctx", cwd: $c}')" context_hook
check "usage: the session start tells of an error once" test -z "$(grep '^errors ' "$work/context.out")"
check "usage: the session start without new errors says the scopes as before" grep -q '^use: brain lookup' "$work/context.out"
# The CLI's one boundary holds before a command runs too: a shell left in a directory that was removed, such as an agent's
# worktree, gets one line and an error event, never a stack.
mkdir "$work/gone"
gone_status=0
gone_said="$(cd "$work/gone" && rmdir "$work/gone" && "$root/cache/publish/brain" context 2>&1 >/dev/null)" || gone_status=$?
check "usage: brain from a removed directory exits 1" test "$gone_status" = 1
check "usage: brain from a removed directory says one line" test "$(wc -l <<<"$gone_said" | tr -d ' ')" = 1
check "usage: brain from a removed directory says what to do" grep -q '^brain: brain context failed: the working directory no longer exists; cd into one that does; ' <<<"$gone_said"
check "usage: brain from a removed directory says no stack" test -z "$(grep -E '^ +at ' <<<"$gone_said" || true)"
check "usage: brain from a removed directory records the error" test -n "$(errors '"failed":"brain context"')"
context_none() { env -u CLAUDE_CODE_SESSION_ID LOCAL_BIN="$work/none" "$root/harness/hooks/brain-context.sh" >"$work/context.out"; }
expect 0 "usage: the session start without brain still exits 0" "{}" context_none
check "usage: the session start without brain says so" grep -q '^brain: the brain CLI is neither on PATH nor in ' "$work/context.out"
check "usage: the session start without brain records the failure" test -n "$(errors '"failed":"brain-context.sh","problem":"the brain CLI is neither')"
context_crashing() { env -u CLAUDE_CODE_SESSION_ID LOCAL_BIN="$work/crashing" "$root/harness/hooks/brain-context.sh" >"$work/context.out"; }
expect 0 "usage: the session start with a brain that crashes still exits 0" "{}" context_crashing
check "usage: the session start with a brain that crashes records it" test -n "$(errors '"failed":"brain-context.sh","problem":"brain context exited 134')"
stop="$(jq -cn --arg s "$session" '{session_id: $s, hook_event_name: "SubagentStop", stop_hook_active: false, agent_id: "r1", agent_type: "brain-ready", last_assistant_message: "NOT READY\n1. Which project?"}')"
expect 0 "usage: a readiness check's stop is recorded" "$stop" uses
check "usage: the stop records the verdict it ended on" grep -q '"event":"readiness",.*"verdict":"NOT READY",.*"agentId":"r1","agentType":"brain-ready"' <<<"$(logged)"
rm -rf "$data/usage"

# refusal.sh runs a hook unchanged and records it in the usage log when it refuses. The record runs in the background,
# so a brain that fails or hangs changes neither the answer nor how soon it comes.
refusal="$root/harness/hooks/refusal.sh"
refuses() { printf '#!/bin/sh\ncat >/dev/null\necho out\necho "BLOCKED: %s" >&2\nexit %s\n' "$1" "$2" >"$work/$3"; chmod +x "$work/$3"; }
refuses "not this" 2 no-hook.sh
refuses "nothing" 0 yes-hook.sh
# recorded <pattern>: waits up to five seconds for the background record to log a line that matches.
recorded() {
    local tries
    for ((tries = 0; tries < 50; tries++)); do
        grep -q "$1" <<<"$(logged)" && return 0
        sleep 0.1
    done
    return 1
}
through() { LOCAL_BIN="$1" "$refusal" "$work/$2" check; }
no_through_brain() { through "$work/bin" no-hook.sh >/dev/null; }
yes_through_brain() { through "$work/bin" yes-hook.sh >/dev/null; }
no_through_failing() { through "$work/failing" no-hook.sh >/dev/null; }
yes_through_failing() { through "$work/failing" yes-hook.sh >/dev/null; }
no_through_none() { through "$work/none" no-hook.sh >/dev/null; }
crash_through() { through "$work/bin" crash-hook.sh >/dev/null; }
missing_through() { through "$work/bin" missing-hook.sh >/dev/null 2>&1; }
guarded_call="$(worker Bash "$(bash_cmd "ls")")"
expect 2 "refusal: a hook's refusal stands" "$guarded_call" no_through_brain
check "refusal: the refusal is recorded with its hook, tool and reason" recorded '"event":"refusal",.*"agentType":"general-purpose","hook":"no-hook","tool":"Bash","reason":"BLOCKED: not this"'
rm -rf "$data/usage"
expect 0 "refusal: a hook's allow stands" "$guarded_call" yes_through_brain
sleep 0.5
check "refusal: an allow records nothing" test -z "$(logged)"
check "refusal: the hook's stdout and stderr pass through" test "$(through "$work/bin" no-hook.sh <<<"$guarded_call" 2>&1 || true)" = "$(printf 'out\nBLOCKED: not this')"
mkdir -p "$work/failing" "$work/hanging"
printf '#!/bin/sh\nexit 1\n' >"$work/failing/brain"
printf '#!/bin/sh\nsleep 5\n' >"$work/hanging/brain"
chmod +x "$work/failing/brain" "$work/hanging/brain"
expect 2 "refusal: a brain that fails leaves the refusal" "$guarded_call" no_through_failing
expect 0 "refusal: a brain that fails leaves the allow" "$guarded_call" yes_through_failing
expect 2 "refusal: without brain the refusal stands" "$guarded_call" no_through_none
check "refusal: without brain the failure is recorded" test -n "$(errors '"failed":"refusal.sh","problem":"the brain CLI is neither')"
started=$SECONDS
hung="$(through "$work/hanging" no-hook.sh <<<"$guarded_call" 2>/dev/null || echo "exit $?")"
check "refusal: a brain that hangs leaves the refusal" test "$hung" = "$(printf 'out\nexit 2')"
check "refusal: a brain that hangs does not hold the answer" test $((SECONDS - started)) -lt 3
expect 2 "refusal: with no hook to run it refuses" "$guarded_call" "$refusal"
rm -rf "$data/usage"
refuses "crashed" 1 crash-hook.sh
expect 2 "refusal: a hook that exits 1 refuses" "$guarded_call" crash_through
check "refusal: the failed hook is recorded as a refusal with its status" recorded '"event":"refusal",.*"hook":"crash-hook","tool":"Bash","reason":"BLOCKED: the hook crash-hook.sh exited 1, so it refuses"'
rm -rf "$data/usage"
expect 2 "refusal: a hook that is missing refuses" "$guarded_call" missing_through
rm -rf "$data/usage"
# The readiness check's agent file runs its door through refusal.sh, and the door alone where brain install has not
# linked refusal.sh yet: the agent file goes live when it lands, the new hook only with the next install. It finds both
# in Claude Code's config directory, which CLAUDE_CONFIG_DIR moves.
ready_command="$(sed -nE "s/^[[:space:]]*command: '(.*)'\$/\\1/p" "$root/harness/agents/brain-ready.md")"
mkdir -p "$work/ready-home/.claude/hooks"
ln -s "$ready" "$work/ready-home/.claude/hooks/brain-ready.sh"
ready_agent() { HOME="$work/ready-home" LOCAL_BIN="$work/none" bash -c "$ready_command" 2>/dev/null; }
mkdir -p "$work/ready-config/hooks"
ln -s "$ready" "$work/ready-config/hooks/brain-ready.sh"
ready_moved() { HOME="$work/none" CLAUDE_CONFIG_DIR="$work/ready-config" LOCAL_BIN="$work/none" bash -c "$ready_command" 2>/dev/null; }
expect 2 "refusal: the readiness door in a moved config directory refuses" "$(agent brain-ready Bash "$(bash_cmd "cat /etc/passwd")")" ready_moved
expect 0 "refusal: the readiness door in a moved config directory allows a graph read" "$(agent brain-ready Bash "$(bash_cmd "brain lookup tea")")" ready_moved
check "refusal: the readiness check's agent file names its hook command" test -n "$ready_command"
expect 2 "refusal: the readiness door refuses without refusal.sh installed" "$(agent brain-ready Bash "$(bash_cmd "cat /etc/passwd")")" ready_agent
expect 0 "refusal: the readiness door allows a graph read without refusal.sh installed" "$(agent brain-ready Bash "$(bash_cmd "brain lookup tea")")" ready_agent
ln -s "$refusal" "$work/ready-home/.claude/hooks/refusal.sh"
expect 2 "refusal: the readiness door refuses through refusal.sh" "$(agent brain-ready Bash "$(bash_cmd "cat /etc/passwd")")" ready_agent
rm -rf "$data/usage"

# A hook that fails refuses: input that is not JSON and a symlink loop make the hooks fail, and they exit 2.
expect 2 "the gate refuses input that is not JSON" "not json"
door 2 "the door refuses input that is not JSON" "not json"
expect 2 "the readiness door refuses input that is not JSON" "not json" ready_bash
expect 2 "the worktree guard refuses input that is not JSON" "not json" "$root/harness/hooks/block-bad-worktree.sh"
ln -s "$work/loop-b" "$work/loop-a"
ln -s "$work/loop-a" "$work/loop-b"
expect 2 "the gate refuses a path through a symlink loop" "$(worker Edit "$(file "$work/loop-a/x.cs")")"
door 2 "the door refuses a path through a symlink loop" "$(worker Edit "$(file "$work/loop-a/x.cs")")"
# An unbound variable exits 1 without the ERR trap; the door's walk of the words, given one, still refuses.
mkdir "$work/unbound"
cp "$root/harness/hooks/lib.sh" "$work/unbound/lib.sh"
# shellcheck disable=SC2016 # the line injected names the variable unexpanded
awk '{ print } $0 == "runs_window() {" { print "    : \"$unset_on_purpose\"" }' "$door" >"$work/unbound/graph-door.sh"
chmod +x "$work/unbound/graph-door.sh"
check "the unbound variable is injected" grep -q unset_on_purpose "$work/unbound/graph-door.sh"
expect 2 "door: an unbound variable in the walk refuses" "$(hook_json PreToolUse "git commit -m 'Narrow brain serve'")" "$work/unbound/graph-door.sh"

# Links relative to the process resolve in a process with the hook's root and working directory, as Claude's are.
if [[ -d /proc/self/root ]]; then
    door 2 "a Write into the graph through /proc/self/root" "$(worker Write "$(file "/proc/self/root$data/graph/personal/x.md")")"
    door 2 "a Write into the pending writes through /proc/thread-self/root" "$(worker Write "$(file "/proc/thread-self/root$data/cache/pending/x")")"
    ln -s "/proc/self/root$data/graph/global" "$data/graph/via-proc"
    expect 0 "brain root writes a node through a link into /proc/self/root" "$(brain Write "$(file "$data/graph/via-proc/x.md")")"
    ln -s "/proc/self/root$root/src" "$data/graph/proc-escape"
    expect 2 "brain root edits code through a graph link into /proc/self/root" "$(brain Edit "$(file "$data/graph/proc-escape/Brain/Cli.cs")")"
    rm "$data/graph/via-proc" "$data/graph/proc-escape"
    # /proc/self/cwd is the hook's working directory, as it is Claude's: here the data repo, then this repo.
    door_in_data() { (cd "$data" && "$door"); }
    gate_in_data() { (cd "$data" && "$gate" check); }
    gate_in_root() { (cd "$root" && "$gate" check); }
    expect 2 "door: a Write into the graph through /proc/self/cwd" "$(worker Write "$(file /proc/self/cwd/graph/x.md)")" door_in_data
    expect 2 "door: a Write into the pending writes through /proc/self/cwd" "$(worker Write "$(file /proc/self/cwd/cache/pending/x)")" door_in_data
    expect 2 "door: a Write into the graph through /proc/self/cwd/.." "$(worker Write "$(file "/proc/self/cwd/../$(basename "$data")/graph/x.md")")" door_in_data
    ln -s /proc/self/cwd/graph/global "$data/graph/via-cwd"
    expect 0 "brain root writes a node through a link into /proc/self/cwd" "$(brain Write "$(file "$data/graph/via-cwd/x.md")")" gate_in_data
    ln -s /proc/self/cwd/src "$data/graph/cwd-escape"
    expect 2 "brain root edits code through a graph link into /proc/self/cwd" "$(brain Edit "$(file "$data/graph/cwd-escape/Brain/Cli.cs")")" gate_in_root
    rm "$data/graph/via-cwd" "$data/graph/cwd-escape"
fi

if [[ -n "${HOOKS_MACOS:-}" ]]; then
    # The marker lives in a directory of the user's own under TMPDIR, and only there does it count.
    mark_dir="$(dirname "$marker")"
    check "the marker lives under TMPDIR" test "$mark_dir" = "${TMPDIR%/}/claude-developer-$(id -u)"
    check "only the user opens the marker's directory" test -n "$(find "$mark_dir" -maxdepth 0 -perm 700)"
    check "no hook ran a command the way only GNU has it" test ! -s "$HOOKS_MACOS/refused"
    # The shims themselves: the real grep and sed would succeed here.
    gnu_word() { echo x | grep -qE 'x\b'; }
    check "the grep shim refuses \\b" test "$(gnu_word 2>/dev/null && echo ran)" != ran
    check "the sed shim refuses a back-reference in a pattern" test "$(echo aa | sed -E 's/(a)\1/b/' 2>/dev/null)" != b
    mv "$mark_dir" "$mark_dir.real"
    ln -s "$mark_dir.real" "$mark_dir"
    expect 2 "a marker in a linked directory does not count" "$(worker Edit "$(file "$wt/src/X.cs")")"
    echo "hooks, as on macOS with bash $BASH_VERSION: $passes passed, $failures failed"
    [ "$failures" = 0 ]
    exit
fi
echo "hooks: $passes passed, $failures failed"
[ "$failures" = 0 ]

# The second run, as on macOS. Every command on PATH is linked into one directory, except sha256sum, which macOS
# lacks, and the shims: those log and refuse an argument spelled the way only GNU has it, and run the real command
# otherwise.
bin="$macos/bin"
mkdir "$bin" "$macos/tmp"
cat >"$macos/refuse" <<'EOF'
#!/bin/sh
# refuse <log> <name> <real> <arg...>
log=$1 name=$2 real=$3
shift 3
for arg in "$@"; do
    gnu=""
    case "$name:$arg" in
    realpath:-[!q]* | readlink:-*[efm]* | grep:-*P* | sed:-*i*) gnu="an option" ;;
    grep:*\\[bB\<\>wWsS1-9]* | sed:*\\[bB\<\>wWsS]*) gnu="a regex escape" ;;
    esac
    # A back-reference in the pattern of an s command; the replacement may hold one.
    if [ "$name" = sed ] && printf '%s\n' "$arg" | awk -F';' '{ for (i = 1; i <= NF; i++) { c = $i; sub(/^[ \t]*/, "", c); if (substr(c, 1, 2) == "s/") { p = substr(c, 3); p = substr(p, 1, index(p, "/") - 1); if (p ~ /\\[1-9]/) bad = 1 } } } END { exit !bad }'; then
        gnu="a back-reference"
    fi
    if [ -n "$gnu" ]; then
        echo "$name $*" >>"$log"
        echo "$name: refused $gnu, which only GNU has: $arg" >&2
        exit 1
    fi
done
exec "$real" "$@"
EOF
chmod +x "$macos/refuse"
for name in realpath readlink grep sed; do
    printf '#!/bin/sh\nexec "%s" "%s" %s "%s" "$@"\n' "$macos/refuse" "$macos/refused" "$name" "$(command -v "$name")" >"$bin/$name"
    chmod +x "$bin/$name"
done
ln -s "${HOOKS_BASH:-$BASH}" "$bin/bash"
IFS=: read -ra dirs <<<"$PATH"
for dir in "${dirs[@]}"; do
    for tool in "$dir"/*; do
        name="$(basename "$tool")"
        [[ -f "$tool" && -x "$tool" && ! -e "$bin/$name" && "$name" != sha256sum ]] || continue
        ln -s "$tool" "$bin/$name"
    done
done
env -u XDG_RUNTIME_DIR HOOKS_MACOS="$macos" TMPDIR="$macos/tmp" PATH="$bin" "$bin/bash" "$root/tests/hooks.sh"
