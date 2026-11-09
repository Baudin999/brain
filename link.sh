#!/usr/bin/env bash
# Links brain's harness to this checkout, for brain's own development. It ships with the checkout only: brain install
# has no development mode. ./install.sh runs it when you install for development, after it builds the CLI.
#
#   ./link.sh [--yes] [--permission-mode <mode>]
#
# It asks which parts of the harness link to this checkout's harness/: CLAUDE.md, agents, hooks and skills, all of them
# on an empty answer or none. The parts not picked link to the installed harness. Then it hands every argument over to
# brain install with --harness and --harness-parts, so one install record and one brain uninstall cover it all.
# A change to a linked part goes live the moment it lands on the default branch. settings.json is written from this
# checkout's harness/settings.json, so run ./link.sh again after it changes, and after a new child of hooks, skills or
# agents.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
parts="CLAUDE.md agents hooks skills"

if [[ ! -x "$root/cache/publish/brain" ]]; then
    echo "link: the CLI is not built. Run ./install.sh and pick development. Nothing was changed." >&2
    exit 1
fi

echo "Which parts of the harness link to this checkout, $root/harness? The rest link to the installed harness."
echo "  $parts, or none"
while true; do
    printf 'Link to this checkout [%s]: ' "$parts" >&2
    answer=""
    read -r answer || true
    # An answer from a pipe is not echoed, so the prompt's line ends here.
    [[ -t 0 ]] || echo >&2
    picked="${answer:-$parts}"
    known=1
    for part in $picked; do
        [[ " $parts none " == *" $part "* ]] || known=""
    done
    [[ "$picked" == none || " $picked " != *" none "* ]] || known=""
    [[ -n "$known" ]] && break
    echo "Name parts from: $parts, or none." >&2
done

if [[ "$picked" == none ]]; then
    exec "$root/bin/brain" install --harness-parts none "$@"
fi
exec "$root/bin/brain" install --harness "$root/harness" --harness-parts "$(tr ' ' ',' <<<"$picked" | tr -s ',')" "$@"
