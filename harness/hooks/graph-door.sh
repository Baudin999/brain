#!/usr/bin/env bash
# The `brain` CLI is the only door into the graph. A node is a subject that lasts, and the CLI is where that shape is
# enforced. Files written by hand turn the graph into a work log. This hook refuses them for everyone: the root brain, workers
# and plain sessions alike, developer skill or not.
#
#   graph-door.sh   PreToolUse on Edit, Write, NotebookEdit and Bash.
#
# Guarded: ${BRAIN_HOME:-~/.local/share/brain}/graph and every repo's docs/brain/graph.
# Edit, Write and NotebookEdit on a guarded path are refused. A Bash command is refused when it writes a guarded
# path: redirection with > or >>, tee, sed -i, perl -i, or cp, mv, rm, touch or truncate on it. Reading stays
# open, and so does one plain `git` or `brain` command, such as git add, commit, rm, show and log.
# The `brain` binary writes these files itself and never passes through a tool call, so it is unaffected.
#
# voices/ stays writable: `brain voices` syncs it, and nothing else reads it as the graph.
#
# The door also keeps the user's yes the user's. From the command line, add, edit, update, merge and forget stage a
# pending write in ${BRAIN_HOME}/cache/pending, guarded here like the graph, and print its id. A session puts it before
# the user with `brain land <id>`, alone as its own command. The command lists the write in brain's window and blocks
# until the user answers there; only `brain serve`, on an answer from that window, lands it. The hook lets the command
# run without a permission prompt in every permission mode and in a session nobody attends, since it lands nothing
# itself: the user answers in the window whatever the session is.
#
# The user's answers to other tools' questions, in ${BRAIN_HOME}/cache/answers, are guarded the same way: only
# `brain serve` writes there, on the user's choice in the window. ${BRAIN_HOME}/sources.tsv is guarded too: brain serve
# runs each command it names as the user, outside every session, so a session that wrote it could write the answers.
# ${BRAIN_HOME}/usage is guarded as well: the usage log is written by brain as things happen, never by hand.
#
# A conversation's trails, every */conversations/*/trails/ of the data repo and of a project's docs/brain, are guarded
# the same way: brain serve writes a trail from its sitting's events, and no session edits one, the sitting's own agent
# included.
#
# The window is the user's, so a session never reaches it: the hook refuses a command that runs `brain serve` or
# `brain ui`, which would make the server a child of the session, and any command that names the window server's port on
# a loopback address or its answer, launch, conversation and chat calls and the registry of its agents. It reads the command's words as the shell
# splits them, so a quoted mention in a commit message or a search pattern runs nothing and passes. The two are refused
# in a chain, in $( ) and backticks, also inside double quotes and an unquoted here-document, in the -c script of a
# shell, su, runuser or script, and after eval. A command that hands text to a shell any other way, such as a pipe or a
# here-document into bash, or source or . at the start of a command, is refused when its text names the two at all, and
# so is a command longer than walk_max. Any failure in the hook refuses the tool call.
#
# The limit: the door matches the command text. A write it does not recognise, from an interpreter, a redirect after a
# cd into the directory, dd of=, install and the like, gets past this gate exactly as it gets past the graph door. So
# does a quoted `brain serve` that a program other than those runs as a command line, such as watch, tmux, ssh or
# sudo -s, and a command name built from a variable or a $'' escape. A process of the user's account that the door
# does not see can land a write: it can read the window server's key and launch file, or start a brain serve of its own
# on another port.
#
# Exits 2 with stderr to deny the tool call (Claude Code's PreToolUse blocking convention).

# errtrace carries the trap into functions; without it a failure there exits 1, which lets the tool call through. A
# command substitution reports its failure to the hook instead of refusing itself, since bash 3.2 runs the trap there
# even when the substitution is a tested condition.
set -euo pipefail -o errtrace
trap '((BASH_SUBSHELL)) || { echo "BLOCKED by the graph door: the hook failed at line $LINENO, so it refuses" >&2; exit 2; }' ERR
# An unbound variable exits 1 without the ERR trap; any exit but an answer, 0 or 2, refuses.
# shellcheck disable=SC2329 # the EXIT trap below calls it
exited() {
    local status=$?
    ((status == 0 || status == 2)) || { echo "BLOCKED by the graph door: the hook exited $status, so it refuses" >&2; exit 2; }
}
trap exited EXIT
# lib.sh sits beside the hook in the harness; brain install links the hook there with one absolute link.
# shellcheck source=harness/hooks/lib.sh
source "$(dirname "$(readlink "${BASH_SOURCE[0]}" || printf '%s' "${BASH_SOURCE[0]}")")/lib.sh"

input=$(cat)
tool=$(jq -r '.tool_name // ""' <<<"$input")
cmd=$(jq -r '.tool_input.command // ""' <<<"$input")
path=$(jq -r '.tool_input.file_path // .tool_input.notebook_path // ""' <<<"$input")
cwd=$(jq -r '.cwd // ""' <<<"$input")
event=$(jq -r '.hook_event_name // "PreToolUse"' <<<"$input")
brain_home="${BRAIN_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/brain}"
pending="$brain_home/cache/pending"
answers="$brain_home/cache/answers"
land_re='^[[:space:]]*brain[[:space:]]+land[[:space:]]+[0-9a-f]{12}[[:space:]]*$'
port="${BRAIN_PORT:-47474}"
[[ "$port" =~ ^[0-9]{1,5}$ ]] || port=47474
# The longest command the word walk reads; a longer one that names brain serve or brain ui is refused. Each step of
# the walk copies the text, so its time grows with the square of the length: under a second at this length. The walk
# reads walk_window characters at a step. Inside double quotes it stops at " \ $ and a backtick, inside $'' at ' and \.
walk_max=16384
walk_window=256
quoted_stops='"\\$`'
ansi_stops="'\\\\"

# refuse <reason>: denies the tool call with the reason for Claude.
refuse() {
    echo "BLOCKED by the graph door: $1" >&2
    exit 2
}

# runs_window <cmd>: sets ran_window=1 when the command runs `brain serve` or `brain ui`, and fed_shell=1 when it
# hands the shell text this walk does not see. It splits the text into words as the shell does: quotes join a word
# and never end it, so "brain" and b''rain name brain, while a quoted string stays one word that runs nothing. It
# walks as commands $( ) and backticks, also inside double quotes and an unquoted here-document, the script of -c
# for a shell, su, runuser and script, and eval's words. A quoted here-document's body is data. A shell without -c,
# and source or . at the start of a command, set fed_shell. Run it as a plain command, never as a condition, so set
# -e and the ERR trap reach its failures.
runs_window() {
    local LC_ALL=C _src=$1 _pos=0 _len=${#1}
    if ((_len > walk_max)); then
        fed_shell=1
        return 0
    fi
    scan_words ""
}

# walk_script <text>: walks a word that a shell runs as a script.
walk_script() {
    local _src=$1 _pos=0 _len=${#1}
    scan_words ""
}

# scan_words <end>: walks _src from _pos up to an unquoted <end>, `)` or a backtick, or to the end of the text. Each
# step reads a window of walk_window characters and takes a run of ordinary ones at once, so a step costs the same
# however long the text is.
scan_words() {
    local end=$1 c n w="" inword=0 quoted=0 argc=0 state=0 shell=0 script=0 skip=0 depth=0 hd_next=0 hd_dash=0 hd_n=0
    local rest part
    local -a hd_delim=() hd_strip=() hd_quoted=()
    while ((_pos < _len)); do
        rest=${_src:_pos:walk_window}
        part=${rest%%[[:space:]\'\"\\\$\`\(\)\;\&\|\<\>\#]*}
        if [[ -n "$part" ]]; then
            w+=$part
            inword=1
            _pos=$((_pos + ${#part}))
            continue
        fi
        c=${rest:0:1}
        n=${rest:1:1}
        _pos=$((_pos + 1))
        case "$c" in
        \\)
            # A backslash before a newline joins two lines and makes no word.
            if [[ "$n" != $'\n' ]]; then
                w+=$n
                inword=1
                quoted=1
            fi
            _pos=$((_pos + 1))
            ;;
        "'")
            take_run 1 "'"
            inword=1
            quoted=1
            _pos=$((_pos + 1))
            ;;
        '"')
            inword=1
            quoted=1
            scan_quoted '"'
            ;;
        '$')
            inword=1
            if [[ "$n" == "(" ]]; then
                _pos=$((_pos + 1))
                scan_words ")"
            elif [[ "$n" == "'" ]]; then
                # $'...': a backslash escapes the next character, \' included.
                quoted=1
                _pos=$((_pos + 1))
                while ((_pos < _len)); do
                    take_run 1 "$ansi_stops"
                    c=${_src:_pos:1}
                    _pos=$((_pos + 1))
                    if [[ "$c" != \\ ]]; then break; fi
                    w+=${_src:_pos:1}
                    _pos=$((_pos + 1))
                done
            else
                w+=$c
            fi
            ;;
        '`')
            if [[ "$end" == '`' ]]; then
                word_done
                new_command
                return 0
            fi
            inword=1
            scan_words '`'
            ;;
        '(')
            word_done
            new_command
            depth=$((depth + 1))
            ;;
        ')')
            word_done
            if ((depth)); then
                depth=$((depth - 1))
            elif [[ "$end" == ")" ]]; then
                new_command
                return 0
            fi
            new_command
            ;;
        ';' | '|')
            word_done
            new_command
            ;;
        $'\n')
            word_done
            new_command
            read_heredocs
            ;;
        '&')
            word_done
            if [[ "$n" == ">" ]]; then
                # &> and &>> redirect; the next word is their file.
                _pos=$((_pos + 1))
                [[ "${_src:_pos:1}" != ">" ]] || _pos=$((_pos + 1))
                skip=1
            else
                new_command
            fi
            ;;
        '<' | '>')
            # A number right before the operator is its file descriptor, not a word.
            if ((inword)) && [[ "$w" =~ ^[0-9]+$ ]]; then
                w=""
                inword=0
            else
                word_done
            fi
            if [[ "$c$n" == "<<" ]]; then
                _pos=$((_pos + 1))
                if [[ "${_src:_pos:1}" == "<" ]]; then
                    _pos=$((_pos + 1))
                    skip=1
                else
                    hd_dash=0
                    if [[ "${_src:_pos:1}" == "-" ]]; then
                        hd_dash=1
                        _pos=$((_pos + 1))
                    fi
                    hd_next=1
                fi
            else
                case "$n" in '>' | '&' | '|') _pos=$((_pos + 1)) ;; esac
                skip=1
            fi
            ;;
        '#')
            if ((inword)); then
                w+=$c
            else
                take_run 0 $'\n'
            fi
            ;;
        *) word_done ;;
        esac
    done
    word_done
    new_command
}

# take_run <add> <stops>: moves _pos to the next of the characters <stops>, or to the end, a window at a time, and
# adds what it passes to the word when <add> is 1.
take_run() {
    local chunk part
    while ((_pos < _len)); do
        chunk=${_src:_pos:walk_window}
        # shellcheck disable=SC2295 # the stops are a bracket expression, escapes included, on purpose
        part=${chunk%%[$2]*}
        if (($1)); then w+=$part; fi
        _pos=$((_pos + ${#part}))
        if [[ "$part" != "$chunk" ]]; then return 0; fi
    done
}

# scan_quoted <stop>: walks the inside of double quotes up to an unescaped <stop>, or a here-document's body with no
# stop, where only a backslash, $( ) and backticks count. Inside double quotes the text joins the word.
scan_quoted() {
    local stop=$1 add=0 c n
    if [[ -n "$stop" ]]; then add=1; fi
    while ((_pos < _len)); do
        take_run "$add" "$quoted_stops"
        c=${_src:_pos:1}
        n=${_src:_pos+1:1}
        _pos=$((_pos + 1))
        case "$c" in
        '"')
            if [[ -n "$stop" ]]; then return 0; fi
            ;;
        \\)
            case "$n" in
            '$' | '`' | '"' | \\)
                if [[ -n "$stop" ]]; then w+=$n; fi
                _pos=$((_pos + 1))
                ;;
            $'\n') _pos=$((_pos + 1)) ;;
            *) if [[ -n "$stop" ]]; then w+=$c; fi ;;
            esac
            ;;
        '$')
            if [[ "$n" == "(" ]]; then
                _pos=$((_pos + 1))
                scan_words ")"
            elif [[ -n "$stop" ]]; then
                w+=$c
            fi
            ;;
        '`') scan_words '`' ;;
        esac
    done
}

# word_done, new_command, see_word and read_heredocs work on the locals of the scan_words that calls them.
# word_done: the word ends; it names a here-document's delimiter, a redirection's file or a word of the command.
word_done() {
    ((inword)) || return 0
    if ((hd_next)); then
        hd_delim[hd_n]=$w
        hd_strip[hd_n]=$hd_dash
        hd_quoted[hd_n]=$quoted
        hd_n=$((hd_n + 1))
        hd_next=0
    elif ((skip)); then
        skip=0
    else
        see_word "$w"
    fi
    w=""
    inword=0
    quoted=0
}

# new_command: a separator ends the simple command. A shell still waiting for -c or a script file reads its input.
new_command() {
    if ((shell == 1 || shell == 4)); then fed_shell=1; fi
    state=0 shell=0 script=0 skip=0 argc=0
}

# see_word <word>: brain, an optional --project and its value, then serve or ui, runs the window. shell follows a
# shell or a runner of a script: 1 a shell before -c, 2 su, runuser or script before -c, 3 after -c up to the script,
# the first word that is no option, 4 the argument of a shell's -o, 5 that of -o after -c.
see_word() {
    local word=$1 name=${1##*/}
    if ((script)); then walk_script "$word"; fi
    case "$shell" in
    3)
        case "$word" in
        -o | +o) shell=5 ;;
        [-+]*) ;;
        *)
            walk_script "$word"
            shell=0
            ;;
        esac
        return 0
        ;;
    4)
        shell=1
        return 0
        ;;
    5)
        shell=3
        return 0
        ;;
    esac
    if ((argc == 0)); then
        if [[ "$word" =~ ^[A-Za-z_][A-Za-z0-9_]*\+?= ]]; then return 0; fi
        case "$word" in
        '!' | '{' | then | do | else | elif | if | while | until | time) return 0 ;;
        .) fed_shell=1 ;;
        esac
    fi
    argc=$((argc + 1))
    case "$name" in
    source) fed_shell=1 ;;
    eval) script=2 ;;
    esac
    case "$state" in
    1)
        case "$word" in
        --project)
            state=2
            return 0
            ;;
        serve | ui) ran_window=1 ;;
        esac
        ;;
    2)
        state=1
        return 0
        ;;
    esac
    state=0
    if [[ "$name" == brain ]]; then state=1; fi
    case "$name" in
    sh | bash | zsh | dash | ksh | mksh | yash | ash | fish | csh | tcsh)
        shell=1
        return 0
        ;;
    su | runuser | script)
        shell=2
        return 0
        ;;
    esac
    case "$shell" in
    1)
        if [[ "$word" =~ ^-[a-zA-Z]*c[a-zA-Z]*$ ]]; then
            shell=3
        elif [[ "$word" == -o || "$word" == +o ]]; then
            shell=4
        elif [[ "$word" != [-+]* ]]; then
            # A script file: the walk does not read it.
            fed_shell=1
            shell=0
        fi
        ;;
    2)
        if [[ "$word" =~ ^-[a-zA-Z]*c[a-zA-Z]*$ || "$word" == --command ]]; then
            shell=3
        elif [[ "$word" == --command=* ]]; then
            walk_script "${word#--command=}"
        fi
        ;;
    esac
}

# read_heredocs: at a line's end, reads past the body of each here-document the line opened. An unquoted delimiter
# leaves $( ) and backticks in the body live, so that body is walked as the inside of double quotes.
read_heredocs() {
    local i=0 start at end chunk line
    while ((i < hd_n)); do
        start=$_pos
        end=$_len
        while ((_pos < _len)); do
            at=$_pos
            chunk=${_src:_pos:walk_window}
            line=${chunk%%$'\n'*}
            if [[ "$line" == "$chunk" ]]; then
                # A line longer than the window is no delimiter.
                take_run 0 $'\n'
                _pos=$((_pos + 1))
                continue
            fi
            _pos=$((_pos + ${#line} + 1))
            if ((hd_strip[i])); then
                while [[ "$line" == $'\t'* ]]; do line=${line#?}; done
            fi
            if [[ "$line" == "${hd_delim[i]}" ]]; then
                end=$at
                break
            fi
        done
        if ((!hd_quoted[i])); then walk_body "${_src:start:end-start}"; fi
        i=$((i + 1))
    done
    hd_n=0
}

# walk_body <text>: walks an unquoted here-document's body for $( ) and backticks.
walk_body() {
    local _src=$1 _pos=0 _len=${#1}
    scan_quoted ""
}

[[ "$event" == "PreToolUse" ]] || exit 0

if [[ "$tool" == "Bash" ]]; then
    if [[ "$cmd" =~ $land_re ]]; then
        jq -n '{hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "allow", permissionDecisionReason: "brain land lands nothing itself: the user answers in brain'"'"'s window"}}'
        exit 0
    fi
    # Quotes and backslashes split a word for the shell but not for this match, so they go first.
    unquoted=$(tr -d "\"'\\\\" <<<"$cmd")
    brain_word='(^|[^[:alnum:]_-])brain([[:space:]]+--project[[:space:]]+[^[:space:]]+)?[[:space:]]+'
    if grep -qE "${brain_word}land([[:space:]]|$)" <<<"$unquoted"; then
        refuse "brain land runs alone: \`brain land <id>\` as its own command, with nothing before or after it"
    fi
    # The words say whether the command runs the two. A shell fed text the walk does not see runs them when the text
    # names them.
    if [[ "$unquoted" == *brain* ]]; then
        ran_window=0 fed_shell=0
        runs_window "$cmd"
        if ((fed_shell)) && grep -qE "${brain_word}(serve|ui)${word_end}" <<<"$unquoted"; then ran_window=1; fi
        if ((ran_window)); then
            refuse "brain's window is the user's: they open it with \`brain ui\` from their own key or terminal, never from a session"
        fi
    fi
    loopback='(127\.[0-9.]+|localhost|0\.0\.0\.0|0x7f[0-9a-f]*|2130706433|\[?::1\]?|\[?::ffff:127\.[0-9.]+\]?|\[?0:0:0:0:0:0:0:1\]?)'
    if grep -qiE "${loopback}[:[:space:]]+${port}([^0-9]|$)|https?://[^/[:space:]]*:${port}([^0-9]|$)" <<<"$unquoted" \
        || grep -qE "/api/(answer|launch|conversations?|chats?|agents)([^[:alnum:]_-]|$)" <<<"$unquoted"; then
        refuse "brain's window server answers only the user's window; a session never calls it"
    fi
fi

# writes <cmd> <guarded>: exit 0 when the command writes a path the extended regex <guarded> matches.
writes() {
    local bare guarded=$2
    # One plain git or brain command, with no unquoted operator or redirection, writes only through itself.
    bare=$(tr '\n' ' ' <<<"$1" | sed -E "s/\"[^\"]*\"//g; s/'[^']*'//g")
    if grep -qE '^[[:space:]]*(git|brain)[[:space:]]' <<<"$bare" && ! grep -qE '[;&|<>`]|\$\(' <<<"$bare"; then
        return 1
    fi
    local seg
    # Each command of a chain or pipeline on its own, so a write and a guarded read in two commands don't mix.
    while IFS= read -r seg; do
        grep -qE "$guarded" <<<"$seg" || continue
        grep -qE ">>?[[:space:]]*[\"']?[^[:space:]\"'<>]*${guarded}" <<<"$seg" && return 0
        grep -qE "^[[:space:]]*(sudo[[:space:]]+)?(tee|rm|mv|touch|truncate)$word_end" <<<"$seg" && return 0
        grep -qE "$word_start(sed|perl)([^[:alnum:]_|;&][^|;&]*)?[[:space:]]-[a-zA-Z]*i" <<<"$seg" && return 0
        # cp writes only its last argument.
        if grep -qE "^[[:space:]]*(sudo[[:space:]]+)?cp$word_end" <<<"$seg"; then
            local last
            last=$(awk '{print $NF}' <<<"$seg")
            grep -qE "$guarded" <<<" $last" && return 0
        fi
    done < <(split_commands "$1")
    return 1
}

# The graph, the user's answers and the usage log, written with BRAIN_HOME in full or, when it lies in the user's home,
# after ~, $HOME or ${HOME}, or with a project's docs/brain, or relative to either.
graph_re="([\"']?$(path_re "$brain_home")/(graph|cache/pending|cache/answers|sources\\.tsv|usage)(/|[[:space:]\"']|$)|docs/brain/graph(/|[[:space:]\"']|$))"
if [[ "$cwd" == "$brain_home" || "$cwd" == */docs/brain ]]; then
    graph_re="($graph_re|(^|[[:space:]=\"'])(\\./)?(graph|cache/pending|cache/answers|sources\\.tsv|usage)(/|[[:space:]\"']|$))"
fi
# A conversation's trails, by any path that names conversations/<...>/trails, or relative to the conversation's folder.
trail_re="conversations/[^[:space:]\"']+/trails(/|[[:space:]\"']|$)"
if [[ "$cwd" == */conversations/* ]]; then
    trail_re="($trail_re|(^|[[:space:]=\"'])(\\./)?trails(/|[[:space:]\"']|$))"
fi

trail=0
if [[ "$tool" == "Bash" ]]; then
    if writes "$cmd" "$trail_re"; then
        trail=1
    else
        writes "$cmd" "$graph_re" || exit 0
    fi
else
    # The path as written with . and .. resolved, and as it resolves through symlinks: either one in a guarded place is guarded.
    data_resolved=$(path_resolved "$brain_home")
    written=$(path_written "${path:-.}")
    resolved=$(path_resolved "${path:-.}")
    guarded=0
    for spelling in "$written" "$resolved"; do
        case "$spelling" in
        */conversations/*/trails/*) trail=1 ;;
        esac
        case "$spelling" in
        "$brain_home"/graph/* | "$data_resolved"/graph/* | */docs/brain/graph/*) guarded=1 ;;
        "$pending"/* | "$data_resolved"/cache/pending/* | "$answers"/* | "$data_resolved"/cache/answers/* | "$brain_home"/sources.tsv | "$data_resolved"/sources.tsv) guarded=1 ;;
        "$brain_home"/usage/* | "$data_resolved"/usage/*) guarded=1 ;;
        esac
    done
    ((guarded || trail)) || exit 0
fi

if ((trail)); then
    cat >&2 <<'EOF'
BLOCKED by the graph door: a conversation's trail is written only by brain serve, from what its sitting said.

Store what you found in the conversation's research/ instead.
EOF
    exit 2
fi

cat >&2 <<'EOF'
BLOCKED by the graph door: the graph is written only through the `brain` CLI.

  brain add <scope> <slug> --kind K < document   a new node: a subject that lasts, not a work log entry
  brain edit <ref> < document           rewrite a node's title, body, why, subjects and edges
  brain link <from> <rel> <to>          add an edge
  brain unlink <from> <rel> <to>        remove an edge
  brain forget <ref...>                 remove nodes, such as a duplicate
  brain merge < block                   a block of node, subject and edge lines, a subject line per new node
  brain land <id>                       alone: the user says yes, no or change to a pending write

Run `brain check` after writing. A finding or a slice belongs in the commit message, not in the graph.
EOF
exit 2
