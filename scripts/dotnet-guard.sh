#!/usr/bin/env bash
# Runs dotnet for every script in this repo. build, test and publish get two MSBuild nodes, no node reuse and no
# shared compiler server, so nothing outlives the command.
#
#   scripts/dotnet-guard.sh build brain.slnx
#   scripts/dotnet-guard.sh test brain.slnx --no-build
#   scripts/dotnet-guard.sh publish src/Brain -c Release -o cache/publish
#
# A machine can add its own limits, such as CPU pinning or a lock: when ${XDG_CONFIG_HOME:-~/.config}/brain/guard is
# executable, dotnet runs through it as `guard dotnet <args>`. brain install links it from BRAIN_HOME/harness/guard.
set -euo pipefail

guard="${XDG_CONFIG_HOME:-$HOME/.config}/brain/guard"

extra=()
case "${1:-}" in
    build | test | publish) extra=(-m:2 -nodeReuse:false -p:UseSharedCompilation=false) ;;
esac

if [[ -x "$guard" ]]; then
    exec "$guard" dotnet "$@" ${extra[@]+"${extra[@]}"}
fi
exec dotnet "$@" ${extra[@]+"${extra[@]}"}
