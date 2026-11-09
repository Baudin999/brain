#!/usr/bin/env bash
# Installs brain from this checkout with a Native AOT build of the CLI, on Linux. A trial, until a release pipeline ships
# the binary: ./install.sh stays the JIT build, and runs again to put it back.
#
#   ./install-aot.sh [--yes] [--permission-mode <mode>]
#
# It publishes the CLI for this machine's runtime with ./build.sh --aot into cache/publish, replacing the build there,
# and hands every argument to brain install --harness-parts none, as ./install.sh does for a user. The build is
# self-contained and needs the .NET SDK of the projects' target framework and a C toolchain, clang or gcc, to link.
# bin/brain, the hooks and the model beside the build find it as they found the JIT build. If it does not work, run
# ./install.sh: it sees the AOT stamp and builds the JIT CLI again from unchanged source.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "building   the brain CLI with Native AOT into $root/cache/publish"
"$root/build.sh" --aot
exec "$root/bin/brain" install --harness-parts none "$@"
