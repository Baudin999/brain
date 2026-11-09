# Your yes

This file covers how a write reaches the graph: the free rules and the judge that read it first, the pending write it
becomes, [`brain land`](cli.md#brain-land) that puts it before you, your answer and how to undo it, the desktop notification
that says a write waits, and who can answer at all. The window you answer in is described in [window.md](window.md).
The shape of a node write is in [graph.md](graph.md#a-node-write), and the nearest entries the judge reads are in
[search.md](search.md#nearest-entries).

## Every node write waits for you

Every node write from the command line waits for you: `add`, `edit`, `update` of a title, body, why or kind, `merge`
and `forget`. The free rules and the judge read it first, the judge with the nearest entries of each node it writes.

What passes is staged as a pending write in the data repo's gitignored `cache/pending`, never the graph. The command
prints the write's text and an id: every node it writes in full as the file will read, and every node it forgets in
full as it read. The pending write also keeps where it came from: the session that staged it, `CLAUDE_CODE_SESSION_ID`
or `add --session`. Which project a write is for is the graph its changes land in, never the session's.

The subjects and edges of an `add`, an `edit` and a `merge` travel inside the write and land with it on the yes.
[`brain link`](cli.md#brain-link) and [`update --subjects`](cli.md#brain-update) serve existing nodes and land at once.

## The judge and the nearest entries

Before the judge runs, the gate finds the 5 entries nearest to each node it judges, the query `brain nearest` asks: at
most 5 and maybe none, in any scope the node's tier reads. The judge reads them as a quality gate on the information,
so the graph neither duplicates nor contradicts itself.

- It refuses a write that says the same as one of them, naming that node.
- It refuses a write that contradicts one, naming that node and the contradiction.
- The two exclude each other: when the judge names both for the same node, the write is refused as a contradiction
  only, and the refusal names the contradiction.
- It names which of them the node should have an edge to and with which relation, a cross-scope edge wherever
  `brain link` allows one. A node write that carries no edge while the judge names one is refused with the named edges,
  so the writer adds them and writes again. With no nearest entry the judge names no edge.

Each such refusal is logged in the usage log (see [costs.md](costs.md)).

Under the pending write the command prints the nearest entries of every node whose title, body or why it writes, up to
5 per node, so the agent that wrote it sees them too: an add, an edit, an update of a title, body or why, and each node
a merge writes. A forget, and a change of kind, subjects or edges alone, print none. A search under the staged write
that fails says so in one line.

The judge reads its rules from the installed brain skill, in Claude Code's config directory, so it works from a package
and from a bare binary alike. Its model and effort are a setting, Sonnet at medium effort unless changed (see
[window.md](window.md#the-settings-page)).

## brain land

[`brain land <id>`](cli.md#brain-land), run alone as its own command, puts the write before you. It moves to
`cache/pending/asked`, and brain's window lists it. The command blocks until you answer there, then returns the answer:

- what landed, on a yes;
- a no;
- or a no with your own words on what to change.

A session runs it in the background. On a change the session rewrites the node from your words and stages it again,
and the window shows it as a new write. A `brain land` stopped early waits again when run again, or returns the answer
already given.

## Answered, and undo

Every answer is kept for 2 days in `cache/pending/history`, gitignored like the rest of `cache/pending`, never in the
graph and never in git, and then swept. It keeps the write exactly as it was asked, the answer, your words for a change
and when it was given. Brain's window lists them under Answered, where an answer can be undone.

Undo on a yes reverses exactly what the write did, every node or none:

- an add is removed with the edges it brought;
- an edit or an update gets back the text before it;
- a forget brings back each node with the edges it removed;
- a merge reverses all its lines.

It refuses when a node no longer reads as the yes left it, and the window shows what changed since, the way a yes
refuses a stale write. It also refuses while a node outside the write links to a node the undo would remove.

Undo on a no, with or without words, puts the write before you again, as a write waiting for its answer.

An undo clicked in the window is your yes and asks for no other: `brain serve` undoes it through the pending store's own
code, as it lands an answer. An answer is undone once. An undone yes drops the answer `brain land` returns, so the
write no longer reads as landed, and commits itself: brain stages exactly the node files the undo changed and commits
them as "Undo of brain write <id>", in the project repo for a project node and in the data repo for personal and
global. Whatever else is staged or changed there stays as it was. A commit that fails leaves the undo standing, and the
window says why. An undone no changes no file and commits nothing.

## The desktop notification

While a write waits, brain also shows one desktop notification through `notify-send`: "brain is waiting on your yes"
and how many writes wait, never what they say. It updates in place as writes arrive or are answered, and closes when
none waits. The waiting `brain land` that holds `cache/pending/notice.lock` owns it, so several sessions show one, and
the server need not run.

A click runs `brain ui`. Set `BRAIN_NOTIFY_CLICK` to another shell command; on Hyprland with the window on a special
workspace that is `hyprctl dispatch 'hl.dsp.workspace.toggle_special("brain")'`. Without `notify-send`, or with
`BRAIN_NOTIFY_SEND=off`, nothing shows and nothing fails. It closes through `gdbus` and, without it, by replacing the
notification with one that expires at once. `brain serve` closes it after an answer when the `brain land` that owned it
was killed.

## Who can answer

Only the window answers. The rest of this section is how that holds, and where it stops.

- **The launch code.** `brain serve` writes a launch file, mode 0600 in the data repo's `cache/serve`, a directory of
  mode 0700, and opens the browser on that file. The file sends the browser on to the window's URL with a launch code
  in the fragment, so the code never stands on the browser's command line, which every account on the machine can
  read. The window trades the code once, within a minute, for a session token it keeps in memory only and sends in a
  header with every call, and the launch file goes. Without the token the API answers nothing. A reload loses the
  token, so the window refuses the reload keys, and `brain ui` opens a fresh window.
- **The key.** `brain ui` reaches a running `brain serve` with the key in `cache/serve/key`, mode 0600. Without it the
  server neither says hello nor opens a window. So another account on the machine reaches nothing but the page's
  static files. `/theme.css` is one of them; the window's event stream, which says when the theme switched, needs its
  session token. brain reads only the one `colors.toml` path, which no request names, and only `#rrggbb` colors reach
  the stylesheet.
- **The host and the origin.** Every request must name `127.0.0.1:<port>` as its host, which shuts out DNS rebinding,
  and a POST from a browser must come from the window's own origin, which shuts out other web pages. The page runs
  under a content security policy that allows only its own script.
- **The exact text.** An answer names the SHA-256 of the write the window showed, and lands only when it matches. What
  lands is that text: the write keeps each file's text before and after, and lands only when every file still reads as
  it did when it was staged. A change since then lands nothing, and `brain land` says so. The window shows every change
  as the raw file too, `D`: a new node as a diff from nothing, a forgotten one as a diff to nothing.
- **The graph door.** `harness/hooks/graph-door.sh` lets `brain land <id>` run alone without a permission prompt, since
  it lands nothing itself, in every permission mode and in a session nobody attends: you answer in the window whatever
  the session is. It refuses `brain land` in a chain or a shell. It refuses a command that runs `brain serve` or
  `brain ui` from a session: in a chain, in `$( )` or backticks, also inside double quotes and an unquoted
  here-document, in the `-c` script of a shell, `su`, `runuser` or `script`, and after `eval`. It refuses any command
  that pipes or feeds text naming them to a shell, or to `source` or `.`, and a command longer than 16384 characters
  that names them. It reads the command's words as the shell splits them, so a quoted mention, in a commit message, a
  search pattern or a doc edit, passes. It refuses any command that names the window server's port, as the hook's own
  `BRAIN_PORT` sets it, on a loopback address, or its answer and launch calls. It refuses tool calls that write
  `cache/pending`, `cache/answers` or `sources.tsv`, as it refuses those that write the graph: `brain serve` runs each
  command in `sources.tsv` as you, outside every session, so a session that wrote it could write an answer. Any failure
  in the hook refuses the tool call.
- **A spent code.** A spent launch code tried again closes every session and the window says so, and a new session
  answers only once it is two seconds old. That catches a careless race for a code; it is no protection against a
  process of yours.
- **The limit.** A process of your own account that the door does not see, from an interpreter or a script, can land a
  write. It can write a node file directly. It can read the key and the launch file. It can stop or kill the browser
  before the window trades its code, so no alarm fires. It can start its own `brain serve` on another port with its own
  program as `BRAIN_BROWSER`, receive a code and answer; the door guards only the port in its own environment. It can
  read a process's memory or send the window keys. The door's refusals reach only the commands whose text it reads. A
  quoted `brain serve` that another program runs as a command line, such as `watch`, `tmux`, `ssh` or `sudo -s`, gets
  past them.

## Related

- [window.md](window.md): node review, Answered, and the rest of brain's window.
- [graph.md](graph.md): what a node write carries.
- [sources.md](sources.md): other tools' questions, answered in the same window.
- [costs.md](costs.md): the usage log that records every answer and every refusal.
