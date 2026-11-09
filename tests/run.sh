#!/usr/bin/env bash
# THE GATE. Exits non-zero on the first failure: shellcheck, a 0-warning build, the tests, the CLI against an empty
# temp data dir, the developer gate's hook tests, and the wipe drill against a fresh clone. Every dotnet call goes through scripts/dotnet-guard.sh.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

# The judge never runs in the gate: the tests reach it through a fake, and nothing here may call the model.
export BRAIN_JUDGE=off

step() { printf '== %s\n' "$*"; }

step shellcheck
mapfile -t scripts < <(git ls-files --cached --others --exclude-standard -- '*.sh' bin/brain)
shellcheck "${scripts[@]}"
echo "shellcheck: ${#scripts[@]} scripts clean"

step build
if ! build_log="$(scripts/dotnet-guard.sh build brain.slnx --nologo)"; then
    echo "$build_log"
    exit 1
fi
warnings="$(grep -oE '^ +[0-9]+ Warning\(s\)' <<<"$build_log" | grep -oE '[0-9]+' || true)"
errors="$(grep -oE '^ +[0-9]+ Error\(s\)' <<<"$build_log" | grep -oE '[0-9]+' || true)"
echo "build: ${warnings:-?} warnings, ${errors:-?} errors"
[ "${warnings:-1}" = 0 ] && [ "${errors:-1}" = 0 ]

step test
if ! test_log="$(scripts/dotnet-guard.sh test brain.slnx --no-build --nologo)"; then
    echo "$test_log"
    exit 1
fi
grep -E '^(Passed|Failed)!' <<<"$test_log"

step cli
./build.sh
data="$(mktemp -d)"
trap 'rm -rf "$data"' EXIT
# From inside the empty data dir, so no project scope is in effect: this repo's own graph links into the real personal
# and global scopes, which an empty data dir does not have.
(cd "$data" && BRAIN_HOME="$data" "$root/bin/brain" context >/dev/null)
(cd "$data" && BRAIN_HOME="$data" "$root/bin/brain" check)

step hooks
tests/hooks.sh

step drill
tests/drill.sh

echo "gate: green"
