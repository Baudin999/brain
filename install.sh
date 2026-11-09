#!/usr/bin/env bash
# Installs brain from this checkout, on Linux, macOS or WSL. On Windows, run it and claude from WSL.
#
#   ./install.sh [--yes] [--permission-mode <mode>]
#
# It is the bootstrap of a checkout, until brain ships as a binary. First it asks whether you install as a user or for
# brain's own development; an empty answer, or none, is a user. Then it checks that the CLI can be built, builds it into
# cache/publish when it is missing or was built from other source, and hands every argument over: a user's to
# brain install --harness-parts none, the guided installer in the CLI, which links every part to the installed harness,
# and a developer's to ./link.sh, which links the parts picked to this checkout through brain install. brain install
# says what brain is, shows every change with its diff and asks before it touches anything outside this checkout.
#
#   checks   bash 3.2 or newer and git, and, when the CLI needs a build, the .NET SDK of the projects' target
#            framework, read from Directory.Build.props. It names each one missing and stops, changing nothing. Native
#            Windows stops too, and WSL warns when this checkout lives on a Windows drive under /mnt.
#   build    ./build.sh stamps each build with a hash of the source it was built from, the harness it carries
#            included. A stamp that differs from ./build.sh --stamp, or none, means a rebuild. When nothing changed,
#            nothing is built.
if [ -z "${BASH_VERSION:-}" ] || [ "${BASH_VERSINFO[0]}" -lt 3 ] || { [ "${BASH_VERSINFO[0]}" -eq 3 ] && [ "${BASH_VERSINFO[1]}" -lt 2 ]; }; then
    echo "install: brain needs bash 3.2 or newer. Run it as ./install.sh with bash. Nothing was changed." >&2
    exit 1
fi
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Do you install brain as a user, or for brain's own development?"
echo "  1  user          links Claude Code to the harness the CLI carries"
echo "  2  development   links the parts of the harness you pick to this checkout, with ./link.sh"
while true; do
    printf 'Install as [user]: ' >&2
    answer=""
    read -r answer || true
    # An answer from a pipe is not echoed, so the prompt's line ends here.
    [[ -t 0 ]] || echo >&2
    case "$answer" in
    "" | 1 | user) development="" && break ;;
    2 | development) development=1 && break ;;
    esac
    echo "Answer 1 or user, 2 or development." >&2
done

case "$(uname -s)" in
MINGW* | MSYS* | CYGWIN*)
    echo "install: brain runs on Linux, macOS and WSL, not on Windows itself. Install WSL, clone brain inside it, and" >&2
    echo "run ./install.sh and claude from WSL. Nothing was changed." >&2
    exit 1
    ;;
esac
if grep -qi microsoft /proc/version 2>/dev/null && [[ "$root" == /mnt/* ]]; then
    echo "install: warning: $root is on a Windows drive. Builds and hooks there are slow, and links may not work." >&2
    echo "Clone brain into your WSL home, such as ~/brain, and run ./install.sh from there." >&2
fi

# Every missing prerequisite is named before the installer stops, so one round of installs fixes them all. brain install
# checks what the installed brain needs at run time itself.
missing=0
if ! command -v git >/dev/null; then
    echo "install: brain needs git, and it is not on PATH. Install it with your package manager." >&2
    missing=$((missing + 1))
fi

# The version comes from Directory.Build.props, never from this script.
framework="$(sed -n 's|.*<TargetFramework>net\([0-9][0-9]*\)\..*|\1|p' "$root/Directory.Build.props" | head -n 1)"
if [[ -z "$framework" ]]; then
    echo "install: cannot read the target framework from $root/Directory.Build.props. Nothing was changed." >&2
    exit 1
fi
built=""
if ((missing == 0)) && [[ -x "$root/cache/publish/brain" && -f "$root/cache/publish/source.stamp" ]] &&
    [[ "$(cat "$root/cache/publish/source.stamp")" == "$("$root/build.sh" --stamp)" ]]; then
    built=1
fi
if [[ -z "$built" ]]; then
    sdk="$(dotnet --version 2>/dev/null || true)"
    major="${sdk%%.*}"
    if [[ ! "$major" =~ ^[0-9]+$ ]] || ((major < framework)); then
        found="no .NET SDK was found"
        [[ -n "$sdk" ]] && found="found .NET SDK $sdk"
        echo "install: brain needs the .NET $framework SDK or newer to build the CLI, and $found." >&2
        echo "Get it from https://dotnet.microsoft.com/download" >&2
        missing=$((missing + 1))
    fi
fi
if ((missing > 0)); then
    echo "install: $missing missing. Install them and run ./install.sh again. Nothing was changed." >&2
    exit 1
fi

if [[ -n "$built" ]]; then
    echo "skipped    build (cache/publish/brain is built from this source)"
else
    echo "building   the brain CLI into $root/cache/publish, from this checkout's source"
    "$root/build.sh"
fi

if [[ -n "$development" ]]; then
    exec "$root/link.sh" "$@"
fi
exec "$root/bin/brain" install --harness-parts none "$@"
