#!/usr/bin/env bash
# The wipe drill: proves a fresh clone restores the brain and that uninstall puts back what was there. It clones this
# repo into a temp dir and runs the clone's ./install.sh, which asks user or development, builds the CLI and hands over
# to brain install or ./link.sh, against a temp CLAUDE_CONFIG_DIR, LOCAL_BIN and BRAIN_HOME.
#
#   1. A .NET SDK older than the target framework stops the install before anything changes, a machine without the
#      prerequisites hears each one named, and native Windows is sent to WSL.
#   2. The installer asks user or development, where the data repo goes and which permission mode to use, shows each
#      change with its diff, and asks replace or keep for a CLAUDE.md and a settings.json of the user's own. Keeping
#      them and then any answer but yes changes nothing outside the checkout.
#   3. --yes over the user's CLAUDE.md and settings.json, with a seeded data repo cloned into BRAIN_HOME first, the
#      restore path. The seed carries a private harness/: a rule and a settings overlay. The binary is bare, so it
#      unpacks its harness into BRAIN_HOME/cache/harness and every part links there, settings.json merges the overlay
#      and the permission mode and runs every hook from CLAUDE_CONFIG_DIR, the private rule is linked and the user's
#      files are backed up.
#   4. The installed CLI checks and reads the data, and a second run changes nothing. The embedding model downloads
#      from a mirror on disk, never the network, at the sources the install shows: stub files are refused by their
#      pinned SHA-256 and the install goes on. When this checkout's cache/model holds the real model in its directory,
#      the second run downloads it from the mirror and lookup ranks by meaning; otherwise the drill says it skipped
#      that.
#   5. Development, through ./link.sh with every part, relinks each part into the clone, as brain's own development
#      runs, saying so plainly and making no backup of brain's own links, and a second run changes nothing. A plain
#      brain install run again, the update, keeps every part linked to the clone.
#   6. A running brain serve, with a `brain land` waiting on it, restarts on the build when install runs in a Claude
#      Code session. The successor is a child of the old server, not of the install. The write stays before the user,
#      and the restarted window's yes lands it once and reaches the waiting land. A stub browser stands in, on a port
#      of the drill's own. Its source changes first, so the install rebuilds the CLI before the restart, and an
#      install over unchanged source builds nothing. A window that cannot restart ends the install with the command
#      that fixes it.
#   7. --yes creating an empty data repo.
#   8. brain uninstall --yes restores the user's CLAUDE.md and settings.json byte for byte, removes every link, the
#      unpacked harness, the model, the backups and the directories it created, and leaves the data repo as it was.
#
# The clone's build goes through its own scripts/dotnet-guard.sh. Only committed work is cloned, so the drill tests
# the HEAD of the worktree it runs in: a worker's branch before it lands, the default branch after.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp="$(mktemp -d)"
clone="$tmp/brain"
seed="$tmp/seed"
export CLAUDE_CONFIG_DIR="$tmp/claude" LOCAL_BIN="$tmp/bin" BRAIN_HOME="$tmp/data"
# The window server of the drill's data repo listens on a port of the drill's own, never the user's, and lands without
# the judge or a desktop notification.
export BRAIN_PORT=$((20000 + RANDOM % 20000)) BRAIN_JUDGE=off BRAIN_NOTIFY_SEND=off
origin="http://127.0.0.1:$BRAIN_PORT"
land_pid=""

# serve_pid: prints the pid of the clone's brain serve, if one runs.
serve_pid() {
    pgrep -f -- "^$clone/cache/publish/brain serve\$" || true
}

# stop_serve: stops the drill's brain serve when one runs, and exits 1 when its port still answers five seconds later.
stop_serve() {
    local i pid
    pid="$(serve_pid)"
    [[ -z "$pid" ]] || kill "$pid" 2>/dev/null || true
    for ((i = 0; i < 50; i++)); do
        curl -s -o /dev/null "$origin/" || return 0
        sleep 0.1
    done
    return 1
}
cleanup() {
    stop_serve || true
    [[ -z "$land_pid" ]] || kill "$land_pid" 2>/dev/null || true
    rm -rf "$tmp"
}
trap cleanup EXIT

fail() {
    echo "drill: $*" >&2
    exit 1
}

# run_install <log> [flag...]: runs the clone's install.sh --yes as a user, failing the drill with its log on an error.
run_install() {
    local log=$1
    shift
    "$clone/install.sh" --yes --permission-mode acceptEdits "$@" >"$log" 2>&1 </dev/null || {
        cat "$log"
        fail "install.sh failed"
    }
}

# run_link <log>: runs the clone's install.sh --yes for development, which links every part to the clone.
run_link() {
    local log=$1
    printf '2\n' | "$clone/install.sh" --yes --permission-mode acceptEdits >"$log" 2>&1 || {
        cat "$log"
        fail "install.sh for development failed"
    }
}

git clone -q "$root" "$clone"

# A private data repo with one node, as a restore would clone it.
mkdir -p "$seed/graph/global" "$seed/graph/personal"
cat >"$seed/graph/global/drill.md" <<'EOF'
---
kind: information
subjects: []
named-by: agent
born: 2026-10-17
---
# The node the wipe drill seeds its data repo with

## Why
The drill needs one valid node to prove a restore reads the data.

## Edges
EOF
git -C "$seed" init -q
git -C "$seed" add -A
git -C "$seed" -c user.name=drill -c user.email=drill@localhost commit -q -m seed

# The private harness the user keeps in their data repo.
mkdir -p "$seed/harness/rules"
echo "# A rule of the drill's own machine" >"$seed/harness/rules/drill.md"
printf '{"hooks": {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "drill-hook"}]}]}}\n' >"$seed/harness/settings.json"
git -C "$seed" add -A
git -C "$seed" -c user.name=drill -c user.email=drill@localhost commit -q -m harness

# A CLAUDE.md and a settings.json of the user's own, from before brain.
mkdir -p "$CLAUDE_CONFIG_DIR"
echo "# The user's own instructions" >"$CLAUDE_CONFIG_DIR/CLAUDE.md"
echo '{"theme": "dark"}' >"$CLAUDE_CONFIG_DIR/settings.json"
cp "$CLAUDE_CONFIG_DIR/CLAUDE.md" "$tmp/own-CLAUDE.md"
cp "$CLAUDE_CONFIG_DIR/settings.json" "$tmp/own-settings.json"

git clone -q "$seed" "$BRAIN_HOME"

# A .NET SDK older than the target framework stops the installer before anything is touched. A stub dotnet stands in.
mkdir -p "$tmp/stub"
printf '#!/usr/bin/env bash\necho 8.0.100\n' >"$tmp/stub/dotnet"
chmod +x "$tmp/stub/dotnet"
if PATH="$tmp/stub:$PATH" "$clone/install.sh" --yes >"$tmp/old-sdk.log" 2>&1 </dev/null; then
    fail "install went on with a too old .NET SDK"
fi
grep -q 'needs the .NET 10 SDK or newer.*found .NET SDK 8.0.100' "$tmp/old-sdk.log" || fail "install did not name the SDK it needs: $(tail -n 2 "$tmp/old-sdk.log")"
grep -q 'dotnet.microsoft.com/download' "$tmp/old-sdk.log" || fail "install did not say where to get the SDK"
[[ ! -e "$LOCAL_BIN" && ! -e "$CLAUDE_CONFIG_DIR/backups" ]] || fail "install touched files after the .NET refusal"

# A machine with none of the prerequisites hears each one named, and nothing changes. Its PATH has only the commands
# the checks run.
mkdir -p "$tmp/bare"
for tool in bash uname grep sed head dirname date tr; do
    ln -s "$(command -v "$tool")" "$tmp/bare/$tool"
done
if PATH="$tmp/bare" "$clone/install.sh" --yes >"$tmp/bare.log" 2>&1 </dev/null; then
    fail "install went on without its prerequisites"
fi
for need in 'needs git,' 'no .NET SDK was found' '^install: 2 missing\.'; do
    grep -q "$need" "$tmp/bare.log" || fail "install did not say: $need"
done
[[ ! -e "$LOCAL_BIN" && ! -e "$CLAUDE_CONFIG_DIR/backups" ]] || fail "install touched files without its prerequisites"

# Native Windows stops and points at WSL. A stub uname stands in.
printf '#!/usr/bin/env bash\necho MINGW64_NT-10.0\n' >"$tmp/stub/uname"
chmod +x "$tmp/stub/uname"
if PATH="$tmp/stub:$PATH" "$clone/install.sh" --yes >"$tmp/windows.log" 2>&1 </dev/null; then
    fail "install went on on native Windows"
fi
grep -q 'from WSL' "$tmp/windows.log" || fail "install did not point Windows at WSL: $(tail -n 1 "$tmp/windows.log")"
rm "$tmp/stub/uname"

# The model's mirror, served from disk. It is empty until the refused install below shows where each file comes from.
mirror="$tmp/mirror"
mkdir -p "$mirror"
export BRAIN_MODEL_URL="file://$mirror"

# The questions, answered by hand: a user, the default data repo, the default mode, keep CLAUDE.md, keep settings.json,
# and then no. Every change is shown with its diff first, and nothing outside the checkout changes.
if printf '\n\n\nk\nk\nno\n' | "$clone/install.sh" >"$tmp/refused.log" 2>&1; then
    fail "install went on after the answer no"
fi
for said in 'user, or for brain.s own development' 'never writes code itself' 'beta under active development' 'Opus' 'Sonnet judge' \
    "^    -# The user's own instructions" "^    +++ $CLAUDE_CONFIG_DIR/hooks/graph-door.sh" 'Replace or keep .*CLAUDE.md' \
    "kept       $CLAUDE_CONFIG_DIR/settings.json (yours)" 'Do you want to continue' 'Nothing was changed\.$'; do
    grep -q -- "$said" "$tmp/refused.log" || fail "install did not say: $said"
done
grep -q 'Permission mode \[default\]' "$tmp/refused.log" || fail "the permission menu did not offer default first"
[[ ! -e "$LOCAL_BIN" && ! -e "$CLAUDE_CONFIG_DIR/backups" && ! -e "$BRAIN_HOME/cache/harness" ]] || fail "install touched files after the answer no"
cmp -s "$CLAUDE_CONFIG_DIR/CLAUDE.md" "$tmp/own-CLAUDE.md" || fail "install changed CLAUDE.md after the answer no"

# The model's directory and the mirror path of each of its files, as the refused install showed them. Each file is a
# stub, so its hash is wrong.
model_dir="$(sed -n "s|^new     $clone/\(cache/model/[^:]*\): the embedding model .*|\1|p" "$tmp/refused.log")"
[[ -n "$model_dir" ]] || fail "install did not show where the embedding model goes"
mapfile -t model_files < <(grep -o "^        file://$mirror/.*" "$tmp/refused.log" | sed "s|^ *file://$mirror/||")
((${#model_files[@]} > 0)) || fail "install did not show where the embedding model's files come from"
for file in "${model_files[@]}"; do
    mkdir -p "$(dirname "$mirror/$file")"
    echo stub >"$mirror/$file"
done

run_install "$tmp/install.log"
grep -q 'What it costs' "$tmp/install.log" || fail "install banner does not mention the usage cost"
grep -q "$(basename "${model_files[0]}") does not match its pinned SHA-256" "$tmp/install.log" || fail "install did not refuse a model with the wrong hash"
[[ ! -e "$clone/$model_dir" && ! -e "$clone/$model_dir.part" ]] || fail "install kept a model with the wrong hash"
grep -q "^skipped    data ($BRAIN_HOME exists" "$tmp/install.log" || fail "install did not keep the pre-cloned data dir"

# expect_link <source> <target>
expect_link() {
    [[ -L "$2" && "$(readlink "$2")" == "$1" ]] || fail "$2 is not a link to $1"
    links=$((links + 1))
}

# expect_parts <root>: every part of the harness links into <root>, one link for CLAUDE.md and for each child of
# agents, hooks and skills.
expect_parts() {
    local item name child
    shopt -s nullglob dotglob
    for item in "$clone"/harness/*; do
        name="$(basename "$item")"
        if [[ "$name" == settings.json ]]; then
            continue
        elif [[ -d "$item" ]]; then
            for child in "$item"/*; do
                expect_link "$1/$name/$(basename "$child")" "$CLAUDE_CONFIG_DIR/$name/$(basename "$child")"
            done
        else
            expect_link "$1/$name" "$CLAUDE_CONFIG_DIR/$name"
        fi
    done
    shopt -u nullglob dotglob
}

# The bare binary unpacked the harness it carries into the data repo's cache: every file as the clone has it,
# executable where the clone's is. Every part links there.
unpacked="$BRAIN_HOME/cache/harness"
copies=0
while IFS= read -r file; do
    copy="$unpacked/${file#harness/}"
    { [[ -f "$copy" ]] && cmp -s "$copy" "$clone/$file"; } || fail "$copy is not the clone's $file"
    [[ ! -x "$clone/$file" || -x "$copy" ]] || fail "$copy is not executable"
    copies=$((copies + 1))
done < <(git -C "$clone" ls-files harness)
links=0
expect_parts "$unpacked"
expect_link "$clone/bin/brain" "$LOCAL_BIN/brain"
expect_link "$BRAIN_HOME/harness/rules/drill.md" "$CLAUDE_CONFIG_DIR/rules/drill.md"

settings="$CLAUDE_CONFIG_DIR/settings.json"
[[ -f "$settings" && ! -L "$settings" ]] || fail "$settings is not a file of its own"
[[ "$(jq -r .agent "$settings")" == brain ]] || fail "settings.json lacks brain's settings"
[[ "$(jq -r .permissions.defaultMode "$settings")" == acceptEdits ]] || fail "settings.json lacks the permission mode"
[[ "$(jq -r .autoMemoryEnabled "$settings")" == false ]] || fail "settings.json leaves Claude Code auto-memory on"
# The hooks run from the config directory, as a hook command spells it: under ~ when it lies in the home.
hooks="$CLAUDE_CONFIG_DIR/hooks"
# shellcheck disable=SC2088 # the tilde is the text settings.json holds, not a path to expand
[[ "$hooks" != "$HOME"/* ]] || hooks="~/${hooks#"$HOME"/}"
jq -e --arg h "$hooks" '[.hooks.PreToolUse[].hooks[].command] | index("\($h)/refusal.sh drill-hook") and index("\($h)/refusal.sh \($h)/graph-door.sh")' "$settings" >/dev/null ||
    fail "settings.json did not merge the private hooks into brain's, each through refusal.sh from $hooks"
jq -e --arg h "$hooks" '[.hooks.PreToolUse[].hooks[].command | select(startswith("\($h)/refusal.sh ") | not)] == []' "$settings" >/dev/null ||
    fail "a PreToolUse hook in settings.json does not run through refusal.sh"
# shellcheck disable=SC2088 # the tilde is the text settings.json must not hold
! grep -qF '~/.claude/' "$settings" || fail "settings.json names ~/.claude, not CLAUDE_CONFIG_DIR"
git -C "$clone" diff --quiet -- harness/settings.json || fail "install changed the tracked harness/settings.json"
for own in CLAUDE.md settings.json; do
    grep -qF "$own" "$CLAUDE_CONFIG_DIR/backups/brain-install.tsv" || fail "the install record has no backup of the user's $own"
done
[[ -f "$BRAIN_HOME/graph/global/drill.md" ]] || fail "the pre-cloned seed is gone from $BRAIN_HOME"

cd "$tmp"
check="$("$LOCAL_BIN/brain" check)" || fail "brain check failed: $check"
nodes="$(grep -oE '^check: [0-9]+ nodes' <<<"$check" | grep -oE '[0-9]+' || echo 0)"
((nodes > 0)) || fail "brain check found no nodes: $check"

context="$("$LOCAL_BIN/brain" context)"
grep -q '^personal ' <<<"$context" || fail "brain context has no personal scope"
grep -q '^global ' <<<"$context" || fail "brain context has no global scope"

model=1
for file in "${model_files[@]}"; do
    [[ -f "$root/$model_dir/$(basename "$file")" ]] || model=0
done
if ((model)); then
    for file in "${model_files[@]}"; do
        cp "$root/$model_dir/$(basename "$file")" "$mirror/$file"
    done
fi
run_install "$tmp/again.log"
grep -q '^install: 0 linked, 0 written, 0 backed up, ' "$tmp/again.log" || fail "a second install changed something: $(grep '^install:' "$tmp/again.log")"
grep -q '^skipped    [0-9]* already in place: .*CLAUDE\.md, settings\.json, ' "$tmp/again.log" || fail "a second install did not name what is in place"
grep -q '^skipped    build (cache/publish/brain is built from this source)' "$tmp/again.log" ||
    fail "a second install did not skip the build of unchanged source: $(grep -i build "$tmp/again.log")"
if ((model)); then
    grep -q "^downloaded $clone/$model_dir " "$tmp/again.log" || fail "install did not download the model from the mirror: $(tail -n 1 "$tmp/again.log")"
    found="$("$LOCAL_BIN/brain" lookup proves a restore reads what was kept 2>"$tmp/lookup.err")"
    [[ ! -s "$tmp/lookup.err" ]] || fail "lookup did not rank by meaning: $(cat "$tmp/lookup.err")"
    grep -q '^global:drill ' <<<"$found" || fail "lookup did not find the seeded node: $found"
else
    echo "drill: $root/$model_dir has no model, so the download from the mirror and search by meaning were skipped"
fi
grep -q "^skipped    data ($BRAIN_HOME exists" "$tmp/again.log" || fail "a second install did not keep the data dir"
grep -q "^brain ui: no brain serve of this brain runs" "$tmp/again.log" ||
    fail "install did not say no window ran: $(tail -n 3 "$tmp/again.log")"

# Development links every part into the clone, as brain's own development runs. The links it replaces were brain's,
# so it says each one changes target, backs none up and records none as the user's.
backups_before="$(find "$CLAUDE_CONFIG_DIR/backups" -mindepth 1 -maxdepth 1 -type d | LC_ALL=C sort)"
run_link "$tmp/link.log"
expect_parts "$clone/harness"
grep -q "^relink  $CLAUDE_CONFIG_DIR/CLAUDE.md: it links to $unpacked/CLAUDE.md now, and will link to $clone/harness/CLAUDE.md$" "$tmp/link.log" ||
    fail "the development install did not say CLAUDE.md changes target"
! grep -q '^backed up  ' "$tmp/link.log" || fail "the development install backed up brain's own links: $(grep '^backed up' "$tmp/link.log")"
[[ "$(find "$CLAUDE_CONFIG_DIR/backups" -mindepth 1 -maxdepth 1 -type d | LC_ALL=C sort)" == "$backups_before" ]] ||
    fail "the development install made a backup directory for brain's own links"
[[ "$(grep -c '^saved' "$CLAUDE_CONFIG_DIR/backups/brain-install.tsv")" == 2 ]] || fail "the development install recorded brain's links as the user's"
run_link "$tmp/link-again.log"
grep -q '^install: 0 linked, 0 written, 0 backed up, ' "$tmp/link-again.log" ||
    fail "a second development install changed something: $(grep '^install:' "$tmp/link-again.log")"
"$clone/bin/brain" install --yes --permission-mode acceptEdits >"$tmp/rerun.log" 2>&1 </dev/null || {
    cat "$tmp/rerun.log"
    fail "brain install run again failed"
}
grep -q '^install: 0 linked, 0 written, 0 backed up, ' "$tmp/rerun.log" ||
    fail "brain install run again did not keep the parts linked to the clone: $(grep '^relink' "$tmp/rerun.log")"

# A running brain serve restarts on the build, and the write a waiting brain land put before the user survives it.
# The stub browser records the launch file each window opens on.
cat >"$tmp/stub/browser" <<EOF
#!/usr/bin/env bash
printf '%s\n' "\$1" >>"$tmp/opened"
EOF
chmod +x "$tmp/stub/browser"
export BRAIN_BROWSER="$tmp/stub/browser"
"$LOCAL_BIN/brain" ui >"$tmp/ui.log" 2>&1 || fail "brain ui did not start brain serve: $(cat "$tmp/ui.log")"
staged="$("$LOCAL_BIN/brain" add global tea "The drill drinks tea" --subjects drinks --kind idea)"
id="$(grep -oE 'pending [0-9a-f]{12}' <<<"$staged" | cut -d ' ' -f 2)"
[[ -n "$id" ]] || fail "brain add staged nothing: $staged"
"$LOCAL_BIN/brain" land "$id" >"$tmp/land.log" 2>&1 &
land_pid=$!
for ((i = 0; i < 50; i++)); do
    [[ ! -f "$BRAIN_HOME/cache/pending/asked/$id.json" ]] || break
    sleep 0.1
done
[[ -f "$BRAIN_HOME/cache/pending/asked/$id.json" ]] || fail "brain land did not put $id before the user: $(cat "$tmp/land.log")"
old_key="$(cat "$BRAIN_HOME/cache/serve/key")"
old_pid="$(serve_pid)"
[[ -n "$old_pid" ]] || fail "no brain serve of the clone runs after brain ui"

# The source changes, as after a pull, so the install rebuilds the CLI before it restarts the window on it.
printf '// The drill changed this source.\n' >>"$clone/src/Brain/Cli.cs"
CLAUDECODE=1 run_link "$tmp/restart.log"
rebuilt="$(grep -n '^building   the brain CLI into ' "$tmp/restart.log" | cut -d : -f 1)"
restarted="$(grep -n "^brain ui: restarted brain serve on 127.0.0.1:$BRAIN_PORT" "$tmp/restart.log" | cut -d : -f 1)"
[[ -n "$rebuilt" ]] || fail "install did not rebuild the CLI after its source changed: $(grep -i build "$tmp/restart.log")"
[[ -n "$restarted" ]] || fail "install in a session did not restart brain serve: $(tail -n 3 "$tmp/restart.log")"
((rebuilt < restarted)) || fail "install restarted brain serve before it rebuilt the CLI"
[[ "$(cat "$clone/cache/publish/source.stamp")" == "$("$clone/build.sh" --stamp)" ]] ||
    fail "the rebuild did not stamp the source it was built from"
[[ "$(cat "$BRAIN_HOME/cache/serve/key")" != "$old_key" ]] || fail "brain serve kept its old key, so it did not restart"
for ((i = 0; i < 50; i++)); do
    kill -0 "$old_pid" 2>/dev/null || break
    sleep 0.1
done
! kill -0 "$old_pid" 2>/dev/null || fail "the old brain serve did not exit after its restart"
new_pid="$(serve_pid)"
[[ -n "$new_pid" ]] || fail "no new brain serve runs the published binary: $(cat "$BRAIN_HOME/cache/serve.log")"
# The old server started the successor and has exited since, so whoever adopted the successor is its parent, never
# the drill that ran the install or a process of the install.
parent="$(ps -o ppid= -p "$new_pid" | tr -d ' ')"
if [[ "$parent" == "$$" ]] || ps -o args= -p "$parent" | grep -qE 'install\.sh|brain (ui|install)'; then
    fail "the new brain serve is a child of the install: $(ps -o args= -p "$parent")"
fi
for ((i = 0; i < 50; i++)); do
    (($(wc -l <"$tmp/opened") < 2)) || break
    sleep 0.1
done
(($(wc -l <"$tmp/opened") == 2)) || fail "the restarted brain serve did not open its window"
kill -0 "$land_pid" 2>/dev/null || fail "brain land stopped waiting when brain serve restarted: $(cat "$tmp/land.log")"

# The restarted window trades its launch code, as the page does, and says yes to the write it lists.
launch="$(tail -n 1 "$tmp/opened")"
launch="${launch#--app=file://}"
code="$(grep -oE '#launch=[0-9a-f]{64}' "$launch" | head -n 1 | cut -d = -f 2)"
token="$(curl -fsS -X POST -H "Origin: $origin" -H 'Content-Type: application/json' -d "{\"code\":\"$code\"}" "$origin/api/launch" | jq -r .token)"
# A new session answers once it is armed, two seconds after the trade.
sleep 3
digest="$(curl -fsS -H "X-Brain-Session: $token" "$origin/api/pending" | jq -r --arg id "$id" '.[] | select(.id == $id) | .digest')"
[[ -n "$digest" ]] || fail "the restarted window does not list the waiting write $id"
answer="{\"id\":\"$id\",\"digest\":\"$digest\",\"answer\":\"yes\"}"
curl -fsS -X POST -H "Origin: $origin" -H "X-Brain-Session: $token" -H 'Content-Type: application/json' -d "$answer" "$origin/api/answer" >/dev/null ||
    fail "the restarted window could not answer $id"
wait "$land_pid" || fail "brain land failed after the yes: $(cat "$tmp/land.log")"
land_pid=""
[[ -f "$BRAIN_HOME/graph/global/tea.md" ]] || fail "the yes did not land global:tea"

# The data repo's usage log holds every call and the answer, and through the installed hook a validator's round of
# findings; brain stats reports every section from it.
logged="$(cat "$BRAIN_HOME"/usage/*.jsonl)"
grep -q '"event":"call",.*"command":"check"' <<<"$logged" || fail "the usage log has no call of brain check"
grep -q '"event":"answer",.*"verdict":"yes","landed":true' <<<"$logged" || fail "the usage log has no yes to $id"
jq -e --arg h "$hooks" '[.hooks.PostToolUse[].hooks[].command] | index("\($h)/usage.sh")' "$settings" >/dev/null ||
    fail "settings.json does not run the usage hook"
jq -cn '{session_id: "s-drill", tool_name: "Agent", agent_type: "brain", tool_input: {subagent_type: "builder"}, tool_response: {agentId: "b-drill"}}' |
    "$CLAUDE_CONFIG_DIR/hooks/usage.sh" || fail "the installed usage hook did not record a spawn"
jq -cn '{session_id: "s-drill", tool_name: "SendMessage", agent_id: "v-drill", agent_type: "validator", tool_input: {to: "b-drill"}}' |
    "$CLAUDE_CONFIG_DIR/hooks/usage.sh" || fail "the installed usage hook did not record a message"
stats="$("$LOCAL_BIN/brain" stats)" || fail "brain stats failed: $stats"
for section in Cost "Knowledge reuse" "Hit rate" Saves Validator Rework; do
    grep -qx "$section" <<<"$stats" || fail "brain stats has no $section section: $stats"
done
grep -q '^  1 answers: 1 yes, 0 no, 0 no with words$' <<<"$stats" || fail "brain stats did not count the yes: $stats"
grep -q '^  1 rounds of findings sent to 1 builders' <<<"$stats" || fail "brain stats did not count the validator's round: $stats"
twice="$(curl -sS -o /dev/null -w '%{http_code}' -X POST -H "Origin: $origin" -H "X-Brain-Session: $token" -H 'Content-Type: application/json' -d "$answer" "$origin/api/answer")"
[[ "$twice" == 409 ]] || fail "a second yes to $id answered $twice, not 409"
stop_serve || fail "brain serve did not stop: $(cat "$BRAIN_HOME/cache/serve.log")"

# A window that cannot restart, here another brain's brain serve on the port, ends the install with the command that
# fixes it.
mkdir -p "$tmp/other/graph/personal" "$tmp/other/graph/global"
BRAIN_HOME="$tmp/other" "$LOCAL_BIN/brain" ui >"$tmp/other-ui.log" 2>&1 || fail "brain ui did not start another brain's serve: $(cat "$tmp/other-ui.log")"
other_pid="$(serve_pid)"
run_link "$tmp/stale.log"
grep -q '^skipped    build (cache/publish/brain is built from this source)' "$tmp/stale.log" ||
    fail "install rebuilt source that had not changed since its last build"
tail -n 2 "$tmp/stale.log" | head -n 1 | grep -q "^install: brain's window was not restarted" ||
    fail "install did not end by saying the window was not restarted: $(tail -n 3 "$tmp/stale.log")"
tail -n 1 "$tmp/stale.log" | grep -qF "fix it with: kill $other_pid && brain ui" ||
    fail "install did not end with the command that fixes the window: $(tail -n 3 "$tmp/stale.log")"
stop_serve || fail "another brain's serve did not stop: $(cat "$tmp/other/cache/serve.log")"

BRAIN_HOME="$tmp/fresh" run_link "$tmp/fresh.log"
for dir in graph/personal graph/global voices .git; do

    [[ -d "$tmp/fresh/$dir" ]] || fail "a fresh install made no $dir"
done
BRAIN_HOME="$tmp/fresh" "$LOCAL_BIN/brain" check >/dev/null || fail "brain check failed on a fresh data repo"

data_before="$(cd "$BRAIN_HOME" && find . -path ./.git -prune -o -path ./cache -prune -o -type f -print | LC_ALL=C sort | xargs cksum)"
"$clone/bin/brain" uninstall --yes >"$tmp/uninstall.log" 2>&1 </dev/null || {
    cat "$tmp/uninstall.log"
    fail "brain uninstall failed"
}
cmp -s "$CLAUDE_CONFIG_DIR/CLAUDE.md" "$tmp/own-CLAUDE.md" || fail "uninstall did not restore the user's CLAUDE.md"
cmp -s "$CLAUDE_CONFIG_DIR/settings.json" "$tmp/own-settings.json" || fail "uninstall did not restore the user's settings.json"
[[ ! -e "$LOCAL_BIN" ]] || fail "uninstall left $LOCAL_BIN, which brain created: $(find "$LOCAL_BIN")"
[[ ! -e "$unpacked" ]] || fail "uninstall left the unpacked harness in $unpacked"
[[ ! -e "$CLAUDE_CONFIG_DIR/backups" ]] || fail "uninstall left backups: $(find "$CLAUDE_CONFIG_DIR/backups")"
[[ ! -e "$clone/cache/model" ]] || fail "uninstall left the model in $clone/cache/model"
leftover="$(find "$CLAUDE_CONFIG_DIR" -type l)"
[[ -z "$leftover" ]] || fail "uninstall left links: $leftover"
for dir in hooks skills agents rules; do
    [[ ! -e "$CLAUDE_CONFIG_DIR/$dir" ]] || fail "uninstall left $CLAUDE_CONFIG_DIR/$dir: $(find "$CLAUDE_CONFIG_DIR/$dir")"
done
[[ "$(cd "$BRAIN_HOME" && find . -path ./.git -prune -o -path ./cache -prune -o -type f -print | LC_ALL=C sort | xargs cksum)" == "$data_before" ]] ||
    fail "uninstall touched the data repo"
grep -q "^Your data repo at $BRAIN_HOME is left alone, but for the harness brain unpacked into its cache.$" "$tmp/uninstall.log" ||
    fail "uninstall did not say it left the data repo"

echo "drill: $copies unpacked, $links links, $nodes nodes, personal and global in context, fresh data repo checks, uninstall restores"
