# shellcheck shell=bash
# Shared by the hooks beside it, which source it. It runs on Linux, macOS with its stock bash 3.2, and WSL: no
# mapfile, no GNU-only flags, no array that may be empty under set -u.
#
# A gate that sources it sets `set -euo pipefail -o errtrace` and an ERR trap that exits 2 first, so a failure here,
# or a missing lib.sh, refuses the tool call. The hooks that run the brain CLI, brain-context.sh, usage.sh and
# refusal.sh, source it for brain_cli and record_failure, and never refuse: a missing lib.sh is a failure they say.

# brain_cli: the path of the brain CLI to run: brain on PATH, else the one brain install links into
# ${LOCAL_BIN:-~/.local/bin}. Fails when there is neither.
brain_cli() {
    local found
    if found=$(command -v brain 2>/dev/null) && [[ -x "$found" ]]; then
        printf '%s\n' "$found"
        return 0
    fi
    found="${LOCAL_BIN:-$HOME/.local/bin}/brain"
    [[ -x "$found" ]] || return 1
    printf '%s\n' "$found"
}

# brain_missing: the problem a hook says when brain_cli finds no CLI.
brain_missing() {
    printf 'the brain CLI is neither on PATH nor in %s; run brain install, or put brain on PATH\n' "${LOCAL_BIN:-$HOME/.local/bin}"
}

# answered <status>: exit 0 when a brain call with that status answered for itself: it succeeded, refused, or failed
# and recorded the error in the usage log, each of which brain exits 0, 1 or 2 for. Any other status, such as 127 for
# a CLI that is gone or 134 for one that crashed, is a failure brain could not record.
answered() { (($1 <= 2)); }

# record_failure <failed> <problem>: appends an error event to the data repo's usage log as brain writes one, for a
# failure brain cannot record itself, such as a CLI that is missing or crashed. The data repo is BRAIN_HOME, else brain
# under ${XDG_DATA_HOME:-~/.local/share}; one that is not there records nothing. It never fails.
record_failure() {
    local home now at
    home=${BRAIN_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/brain}
    [[ -d "$home" ]] || return 0
    # One date call, so the second and the zone agree; brain writes the zone as +01:00 and macOS's date knows only +0100.
    now=$(date +%Y-%m-%dT%H:%M:%S%z)
    at="${now:0:19}.000${now:19:3}:${now:22:2}"
    {
        mkdir -p "$home/usage" &&
            printf '{"at":"%s","event":"error","failed":"%s","problem":"%s"}\n' "$at" "$(json_text "$1")" "$(json_text "$2")" >>"$home/usage/${at:0:7}.jsonl"
    } 2>/dev/null || true
}

# json_text <text>: the text as the inside of a JSON string, on one line.
json_text() { printf '%s' "$1" | tr '\t\r\n' '   ' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

# quote_re <text>: the text as an extended regex that matches it literally.
quote_re() { sed -E 's/[][\.*^$+?(){}|]/\\&/g' <<<"$1"; }

# path_re <path>: an extended regex for the path as a command may write it: in full or, when it lies in the user's
# home, after ~, $HOME or ${HOME}.
path_re() {
    if [[ "$1" == "$HOME"/* ]]; then
        # shellcheck disable=SC2016 # the alternatives match the literal text ~ and $HOME in the command
        printf '(%s|(~|\\$HOME|\\$\\{HOME\\})%s)\n' "$(quote_re "$1")" "$(quote_re "${1#"$HOME"}")"
    else
        quote_re "$1"
    fi
}

# path_written <path>: the absolute path with ., .. and repeated slashes resolved and symlinks kept, the path as
# written. GNU's realpath -m -s, which macOS lacks. A relative path is taken from the physical working directory.
path_written() {
    local rest out="" part
    rest=$1
    [[ "$rest" == /* ]] || rest="$(pwd -P)/$rest"
    rest="$rest/"
    while [[ -n "$rest" ]]; do
        part=${rest%%/*}
        rest=${rest#*/}
        case "$part" in
        '' | .) ;;
        ..) out=${out%/*} ;;
        *) out="$out/$part" ;;
        esac
    done
    printf '%s\n' "${out:-/}"
}

# path_resolved <path>: the absolute path as it resolves, every symlink followed and the missing tail taken as
# written. GNU's realpath -m, which macOS lacks. Fails on more than 40 symlinks, a loop.
# The longest prefix that is a directory goes, as written, to one cd -P in a subshell that still has the hook's
# working directory and root, so the kernel resolves it the way realpath did, links relative to the process such as
# /proc/self/cwd included. Only the tail past it is walked here, and only a link that is no directory goes to readlink.
path_resolved() {
    local path=$1 prefix next rest part out target hops=0
    [[ "$path" == /* ]] || path="$(pwd -P)/$path"
    while true; do
        prefix=/
        rest="${path#/}/"
        while [[ -n "$rest" ]]; do
            part=${rest%%/*}
            next="${prefix%/}/$part"
            [[ -z "$part" || (-d "$next" && -x "$next") ]] || break
            prefix=$next
            rest=${rest#*/}
        done
        out=$(cd -P "$prefix" && pwd -P) || return 1
        # bash spells a link to /proc/self/root/x as //x.
        while [[ "$out" == *//* ]]; do out=${out//\/\//\/}; done
        [[ "$out" != / ]] || out=""
        while [[ -n "$rest" ]]; do
            part=${rest%%/*}
            rest=${rest#*/}
            case "$part" in
            '' | .) continue ;;
            ..)
                out=${out%/*}
                continue
                ;;
            esac
            if [[ -d "$out/$part" && -x "$out/$part" ]]; then
                # A directory after a missing part and ..: resolve again from here, as written.
                path="$out/$part/$rest"
                continue 2
            elif [[ -L "$out/$part" ]]; then
                hops=$((hops + 1))
                ((hops <= 40)) || return 1
                target=$(readlink "$out/$part")
                [[ "$target" == /* ]] || target="$out/$target"
                path="$target/$rest"
                continue 2
            fi
            out="$out/$part"
        done
        printf '%s\n' "${out:-/}"
        return 0
    done
}

# word_start and word_end: the edges of a word in an extended regex. POSIX defines no \b, and macOS's grep and sed
# do not promise it.
# shellcheck disable=SC2034 # the hooks that source this file use them
word_start='(^|[^[:alnum:]_])'
# shellcheck disable=SC2034 # the hooks that source this file use them
word_end='([^[:alnum:]_]|$)'

# split_commands <cmd>: each command of a chain or pipeline on its own line, split at &&, ||, ;, & and |.
# awk, since BSD sed writes no newline for \n in a replacement.
split_commands() {
    awk '{ gsub(/&&|\|\||[;&|]/, "\n"); print }' <<<"$1"
}

# marker_dir: the directory for per-session markers that a reboot or a new login clears. ${XDG_RUNTIME_DIR} where the
# session has one, as on Linux with systemd; else a directory of the user's own under ${TMPDIR:-/tmp}, as on macOS
# and on WSL without systemd.
marker_dir() {
    if [[ -n "${XDG_RUNTIME_DIR:-}" ]]; then
        printf '%s\n' "$XDG_RUNTIME_DIR/claude-developer"
    else
        local tmp=${TMPDIR:-/tmp}
        printf '%s\n' "${tmp%/}/claude-developer-$(id -u)"
    fi
}

# own_dir <dir>: exit 0 when dir is a real directory, no symlink, that only this user owns and can open.
# It creates the directory when missing.
own_dir() {
    [[ -e "$1" || -L "$1" ]] || mkdir -m 700 "$1"
    [[ -d "$1" && ! -L "$1" && -O "$1" ]] || return 1
    chmod 700 "$1"
}
