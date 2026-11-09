#!/usr/bin/env bash
# Publishes the brain CLI to cache/publish through scripts/dotnet-guard.sh. bin/brain runs what this produces.
# cache/ is derived and never committed, so a fresh clone runs this once. It publishes for this machine's runtime only,
# so cache/publish carries the native ONNX Runtime and SQLite libraries of this platform, not of every platform.
#
#   ./build.sh           publishes, then writes cache/publish/source.stamp, the stamp of the source it published from
#   ./build.sh --aot     the same as a Native AOT build, self-contained, which ./install-aot.sh runs; it empties
#                        cache/publish first when a JIT build is there, and a JIT build does the same over an AOT one
#   ./build.sh --stamp   prints the stamp of the source as it is now, and builds nothing
#
# The stamp is a SHA-256 over the path and content of every file the CLI is built from: src/Brain, the harness it
# carries and the Directory.Build files, tracked or new, as git lists them, so build output and ignored files leave it
# alone. ./install.sh rebuilds when it differs from cache/publish/source.stamp. An AOT build writes the stamp followed
# by " aot", so it never equals the stamp of the source, and ./install.sh builds the JIT build again over it.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"

# sha256: prints the SHA-256 of stdin, with sha256sum on Linux and shasum on macOS.
sha256() {
    if command -v sha256sum >/dev/null; then
        sha256sum | cut -d ' ' -f 1
    else
        shasum -a 256 | cut -d ' ' -f 1
    fi
}

stamp() {
    local file
    git ls-files -z --cached --others --exclude-standard -- src/Brain harness Directory.Build.props Directory.Build.rsp |
        sort -z -u | while IFS= read -r -d '' file; do
        [[ -f "$file" ]] || continue
        printf '%s\n%s\n' "$file" "$(sha256 <"$file")"
    done | sha256
}

aot=""
case "${1:-}" in
--stamp)
    stamp
    exit 0
    ;;
--aot) aot=1 ;;
"") ;;
*)
    echo "usage: ./build.sh [--aot | --stamp]" >&2
    exit 2
    ;;
esac

# The stamp is taken before the publish, so a file edited during it makes the next install build again.
source_stamp="$(stamp)"
rm -f cache/publish/source.stamp
# A JIT build has brain.dll beside it and an AOT build has none. A build over the other kind empties the folder first, so
# nothing of the other stays; a build over its own kind publishes over it. The folder itself stays, for a server
# watching it.
if [[ -n "$aot" && -f cache/publish/brain.dll ]]; then
    find cache/publish -mindepth 1 -delete
fi
if [[ -z "$aot" && -e cache/publish/brain && ! -f cache/publish/brain.dll ]]; then
    find cache/publish -mindepth 1 -delete
fi
if [[ -n "$aot" ]]; then
    scripts/dotnet-guard.sh publish src/Brain/Brain.csproj -c Release -o cache/publish --use-current-runtime --self-contained true -p:PublishAot=true --nologo -v:q
    # The debug symbols of the native binary are 57 MB and nothing reads them.
    rm -f cache/publish/*.dbg
    source_stamp="$source_stamp aot"
else
    scripts/dotnet-guard.sh publish src/Brain/Brain.csproj -c Release -o cache/publish --use-current-runtime --self-contained false --nologo -v:q
fi
printf '%s\n' "$source_stamp" >cache/publish/source.stamp
