# The brain CLI

`brain` is the command line of brain: a knowledge graph of plain-text nodes in git, shared by you and Claude. Every
read of the graph and every write to it goes through this one binary. You run it in a terminal, Claude runs it from its
sessions, and the harness's hooks run a few of its commands on their own. This page is the reference for every command.

```
usage: brain [--project <name|path>] <command> [arguments]
```

A few words carry through the whole page:

- A **node** is one markdown file: a title, a body, a why, its fields and its edges. Its **slug** is the file name,
  lowercase-hyphenated.
- A **scope** is one graph. `personal` is about you, `global` about how we work and about the world, and `project`
  about the repo you stand in. Personal and global live in the data repo, `BRAIN_HOME`. A project's graph lives in its
  own repo, in `docs/brain/graph`. The project scope is in effect inside a git repo that has one, or anywhere with
  [`--project`](#--project).
- A **ref** names a node: `scope:slug`, such as `project:install`, or a bare slug, which brain looks for in project,
  global and personal order. A project registered in `projects.tsv` is a scope name from any directory:
  `brain:install` is the node `install` of the project named `brain`.
- A **pending write** is a node write that waits for **your yes** in brain's window. Nothing it holds is in the graph
  until you say yes there.

`brain help` prints the overview below. `brain <command> --help`, `brain <command> -h` and `brain help <command>` print
one command's usage, flags, notes and examples. `--help` counts only right after the command name.

## Contents

- [The overview](#the-overview)
- [How every command behaves](#how-every-command-behaves)
- [Read the graph](#read-the-graph):
  [context](#brain-context), [lookup](#brain-lookup), [nearest](#brain-nearest), [node](#brain-node),
  [walk](#brain-walk)
- [Write nodes](#write-nodes):
  [add](#brain-add), [edit](#brain-edit), [update](#brain-update), [forget](#brain-forget), [merge](#brain-merge),
  [subjects](#brain-subjects)
- [Edges](#edges): [link](#brain-link), [unlink](#brain-unlink), [faded](#brain-faded), [fold](#brain-fold)
- [Your yes, and the window](#your-yes-and-the-window): [land](#brain-land), [serve](#brain-serve), [ui](#brain-ui)
- [Read a project](#read-a-project): [workspace](#brain-workspace)
- [Work with agents](#work-with-agents): [code](#brain-code), [ask](#brain-ask)
- [Conversations](#conversations): [conversation](#brain-conversation), [dream](#brain-dream)
- [Upkeep](#upkeep): [check](#brain-check), [voices](#brain-voices), [migrate](#brain-migrate), [init](#brain-init)
- [Your use of brain](#your-use-of-brain): [stats](#brain-stats), [record](#brain-record)
- [Install](#install): [install](#brain-install), [uninstall](#brain-uninstall)
- [Global flag](#global-flag): [--project](#--project)
- [Exit codes](#exit-codes)
- [Environment variables](#environment-variables)

## The overview

This is the output of `brain help`, exactly. One table in `src/Brain/Help.cs` holds it and every command's help, and a
test keeps this block equal to it.

```
brain is a knowledge graph of plain-text nodes in git, shared by you and Claude.

usage: brain [--project <name|path>] <command> [arguments]

Read the graph
  context                             Show the scopes in effect, node counts and paths, and each
                                      agent's model; at most 8 lines.
  lookup [words...]                   Search the graph by keyword and meaning; up to 10 sure or
                                      likely hits come back.
  nearest [<ref>]                     Show the 5 nodes nearest to a node, by meaning and keywords;
                                      every write shows them.
  node <ref>                          Show a node: title, fields, why and edges, strongest first.
  walk <ref>                          Show the titles of a node's neighbours, within 4000
                                      characters.

Write nodes
  add <scope> <slug> ["<title>"]      Add a node; waits for your yes.
  edit <ref>                          Rewrite a node's title, body, why, subjects and edges from
                                      markdown; waits for your yes.
  update <ref> ["<title>"]            Change parts of a node in place; waits for your yes.
  forget <ref...>                     Remove nodes and every edge that points at them; all or
                                      nothing; waits for your yes.
  merge                               Apply a block of + lines from stdin; all or nothing; waits for
                                      your yes.
  subjects [scope]                    List the subjects in use in the scopes in effect, with node
                                      counts.

Edges
  link <from-ref> <rel> <to-ref>      Add an edge from one node to another.
  unlink <from-ref> <rel> <to-ref>    Remove an edge; exit 1 when it does not exist.
  faded [N]                           List the N weakest edges, the candidates to doubt; N defaults
                                      to 10.
  fold                                Fold the edge log of every scope to one line per edge.

Your yes, and the window
  land <id>                           Put a pending write before you in brain's window; print your
                                      answer.
  serve                               Serve brain's window on 127.0.0.1 and open it.
  ui                                  Open brain's window in place of any open one, starting it if
                                      needed.

Read a project
  workspace <path>                    Open a workspace window over a project folder, or bring its
                                      open one forward.

Work with agents
  code [agent] [folder]               Start a Claude Code session of an agent, brain unless named,
                                      in a folder, here unless named.
  ask                                 Ask the user questions in brain code's window, as JSON on
                                      stdin; only in a brain code session.

Conversations
  conversation start <scope|project>  Start a conversation in personal, global or a project, with
                                      its first sitting.
  dream <ref> [--deep]                Distill a conversation's unsettled sittings into its
                                      settled.md.

Upkeep
  check                               Validate every scope in effect; exit 1 on any error.
  voices                              Sync your typed Claude messages into voices/.
  migrate                             Move old-format nodes to the current format.
  init [name]                         Give this repo a docs/brain and register it in projects.tsv.

Your use of brain
  stats                               Report what brain cost and gave back, from the usage log;
                                      tokens are estimates.
  record                              Log the Claude Code hook JSON on stdin in the usage log; the
                                      harness runs it.

Install
  install                             Install brain into Claude Code, showing each change first.
  uninstall                           Put back what brain install changed; your data stays.

Global flag
  --project <name|path>               Put in front of any command: use that project's graph from any
                                      directory.

Run brain <command> --help for a command's flags and examples.
```

## How every command behaves

**Output.** What a command found or did goes to stdout. A refusal goes to stderr as one line that starts with
`brain: `, and a usage error adds a second line that points at the command's help:

```
$ brain walk
brain: usage: walk <ref> [--hops <1|2>]
run brain walk --help for its flags and examples
```

**Options.** An option is `--name value`, or a bare `--name` for a switch. There are no short forms but `-h`.
Positional arguments and options may come in any order. An option the command does not know, or one with no value
after it, is refused: `brain: unknown option or missing value: --hop`.

**Refs.** A command that takes a ref refuses text that is no node ref (`brain: x:Y is not a node ref`) and a ref that
names no node (`brain: no node project:nope`).

**Your yes.** `add`, `edit`, `update`, `forget` and `merge` never write the graph themselves. They check the write,
have the judge read it, stage it as a pending write in the data repo's gitignored `cache/pending`, and print it in full
with an id. [`brain land <id>`](#brain-land) then puts it before you in brain's window, and only the window lands it.
`link`, `unlink` and an `update` of subjects alone write at once. A write that would change nothing, such as an `edit`
that leaves the node as it is, stages nothing.

**The gate.** Before a node write is staged, the free rules read it, with no model: a kind of `information` or `idea`,
a why behind information, a new title that does not repeat a commit subject of its repo, and at least one subject on a
node write. Then the judge, a Claude model, reads it with the subjects in use in its scope and the 5 entries nearest to
it. It refuses a write that says the same as one of them, one that contradicts one, and a node write that carries no
edge while the judge names edges for it. The refusal says which and why, and writes nothing. `BRAIN_JUDGE=off` turns the
judge off; the free rules still run.

**Commits.** No graph command commits. Commit a project's nodes with `git -C <repo> add docs/brain/graph` and
`git -C <repo> commit`, and personal and global ones the same way in the data repo. The commands that commit say so in
their own section: `conversation commit`, `dream`, and the window's undo of a yes.

**The usage log.** Every run appends one `call` line to the data repo's `usage/<yyyy-mm>.jsonl`: when, the session
(`CLAUDE_CODE_SESSION_ID`, or none from your own terminal), the command and its arguments, the characters it printed,
how long it ran, its exit status, and the refs a `lookup` or `nearest` returned or a `node` or `walk` opened. `record`,
`install` and `uninstall` write no call line. A failure that is no refusal, such as a git that will not run, prints
`brain: <command> failed: <why>; the usage log keeps the whole error in <file>` and exits 1.

## Read the graph

These commands only read. None of them asks for your yes, and only `node` changes an edge's weight.

### brain context

Shows where brain is and what is in effect here: the data repo, each scope in effect with its node count and folder,
and the model and effort each closed agent runs on. It is a pointer for the start of a session, never content; the
harness runs it when a Claude Code session starts.

```
brain context
```

It takes no arguments. It prints at most 8 lines: `brain` and the data repo; `project   none here` when no project
scope is in effect; one line per scope in effect, project first; `agents` with each closed agent's model and effort from
the data repo's `settings.json`, or why that file cannot be read; and the commands to read with. When errors were
written to the usage log since you were last told, a last line says how many and names the latest, once.

Exit 0, or 1 on a usage error.

```
$ brain context
brain     /home/alice/.local/share/brain
project   59 nodes in /home/alice/Projects/brain/docs/brain/graph
global    44 nodes in /home/alice/.local/share/brain/graph/global
personal  60 nodes in /home/alice/.local/share/brain/graph/personal
agents    builder-easy sonnet medium, builder-difficult opus medium, validator opus medium, readiness sonnet medium
use: brain lookup <words>, brain node <ref>, brain walk <ref>
```

### brain lookup

Searches every scope in effect by keyword and by meaning, the same engine brain's window searches with. Use it first,
before you answer from memory or write a node.

```
brain lookup [words...] [--subject <S>]
```

| Argument | Default | What it does |
|---|---|---|
| `words...` | none | The query. Several words are one query. Needed unless `--subject` is given. |
| `--subject <S>` | none | Keep only nodes that carry subject `S`. With no words, list every node that carries it, by ref. |

Each hit is one line: its ref, its tier and its title. The tier is `sure` or `likely`; a weak hit never shows. An idea
carries `[idea]` after its tier. Up to 10 hits come back, best first. A query with no sure or likely hit prints
`no node is sure or likely`. `--subject` with no words prints each carrier as its ref and title, in ref order, or
`no node matches`. When the embedding model does not run, lookup says so on stderr in one line,
`lookup: keyword hits only: <why>`, and ranks by keywords alone.

It travels no edge. Exit 0 when it ran, hits or not; 1 with neither words nor `--subject`.

```
$ brain lookup install script
project:install  sure  brain install links brain's harness into the Claude config directory, after a guided walk through everything it changes
project:homebrew  likely  On macOS, and on Linux and WSL outside Arch, brain installs from Homebrew
project:install-restarts-window  likely  ./install.sh restarts a running window on the new build, and the old server starts its successor
project:aot-release  likely  Released brain binaries are Native AOT; dev and local installs run JIT
$ brain lookup --subject window
project:agents-never-steer-the-window  Agents never steer brain's window: the user finds what he works on himself
project:brain-code-apart-from-ui  brain code is its own window, apart from brain ui: it is where developers work with agents
$ brain lookup purple elephant banana
no node is sure or likely
```

### brain nearest

Shows the 5 entries nearest to a node, by meaning and keywords: the same entries the judge reads and every staged write
prints under it. Ask it before you write, to find the node you would duplicate or should link to.

```
brain nearest <ref>
brain nearest --scope <scope> [--file <path>]
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | The node to ask about. The node itself is left out. |
| `--scope <scope>` | none | Ask about a node not yet written: read its markdown, the markdown `add` reads, from stdin, as it would land in `personal`, `global` or `project`. |
| `--file <path>` | stdin | With `--scope`: read the markdown from this file. |

The graphs it reads follow the scope the node lands in. A project node is read against that project, global and
personal; a personal or global node against personal, global and every cloned project in `projects.tsv`. A project node
comes back under its `projects.tsv` name, such as `brain:update`. Each entry is its ref, its title and its scope in
brackets, under `nearest to <ref>:`, or `nearest to this markdown:` for `--scope`. With no entry it prints
`  no node is near`. Without the embedding model the heading ends in `, by keywords only: <why>`.

It travels no edge. Exit 0; 1 on a ref with `--scope`, `--file` without `--scope`, a scope that is not in effect here,
or markdown it cannot read.

```
$ brain nearest project:install
nearest to project:install:
  brain:update  Updating brain is running brain install again after the package manager updated the binary  (brain)
  brain:data-apart-from-tool  The brain tool is open source in ~/Projects/brain; the memory lives apart, private, and survives a wipe  (brain)
  brain:brain-code  brain code is our terminal replacement: Claude Code sessions run in a brain window, in brain's styling  (brain)
  brain:settings  Brain's settings live in the data repo's settings.json, committed there, and only the judge and dreamer may leave Claude  (brain)
  brain:aur  On Arch Linux brain installs from the AUR, with its binary in /usr/bin and its harness in /usr/share/brain/harness  (brain)
$ brain nearest --scope project --file pending-sweep.md
nearest to this markdown:
  brain:user-says-yes  Every node going into or out of brain waits for the user's yes, no or change  (brain)
  brain:answer-history  Every answer stays findable for 2 days in brain's window, and can be undone there  (brain)
  brain:write-origin  Every waiting write shows the graph it lands in, and the window's project focus filters on it  (brain)
  brain:one-door  Only the brain CLI writes the graph, and every write passes its gates there  (brain)
  brain:park-dream-remove  A session parks into a conversation, a conversation is removed whole, and a dream compresses and then embeds into the graph  (brain)
```

### brain node

Shows one node whole: rung 1 of the graph, about a hundred tokens.

```
brain node <ref>
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | A slug, looked for in project, global and personal order, or `scope:slug`. |

It prints the ref and the title; the fields as `name: value` joined by ` | `; the body; `why: ` and the why on one line;
then each edge as its weight, its relation and its target, strongest first.

Opening a node travels each of its edges whose other end this Claude Code session opened already, once per edge per
session. A travel adds to the edge's weight in the data repo's `cache/traversals`. From your own terminal, with no
`CLAUDE_CODE_SESSION_ID`, nothing is travelled.

Exit 0; 1 on a bad ref or a missing node.

```
$ brain node project:update
project:update  Updating brain is running brain install again after the package manager updated the binary
kind: information | subjects: [] | named-by: user | born: 2026-11-02 | session: 84ba6677-c4af-44cf-9737-601ab42eaa05
There is no `brain update` command. After pacman or Homebrew put a new binary and harness in place, you run `brain install` again, and it does the right thing. ...
why: A package manager already knows how to fetch and replace a binary, so brain does not do it twice. ...
  3.79  part-of -> install
```

### brain walk

Shows the titles of a node's neighbours: rung 2, about a thousand tokens. Use it to see what surrounds a node before you
build in its area.

```
brain walk <ref> [--hops <1|2>]
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | The node to start from. |
| `--hops <1\|2>` | `1` | How far to walk. Anything but 1 or 2 is refused. |

It follows edges both ways, the node's own and every edge another node writes into it, each hop strongest edge first,
and never visits a node twice. A node of another registered project is shown but not walked on. The first line is the
start node; every other line is indented by its hop and holds the weight, the edge as `from rel -> to` and the title at
the other end, or `(missing)` when the target node is gone. The output stays within 4000 characters; once a line does
not fit, it and every later line are left out, and a last line says `... N more left out`.

It travels no edge: showing an edge is not travelling it. Exit 0; 1 on a bad ref, a missing node or a bad `--hops`.

```
$ brain walk project:install
project:install  brain install links brain's harness into the Claude config directory, after a guided walk through everything it changes
  11.70  project:install refines -> project:open-source-install  Open sourcing brain ships the user's own workflow under Apache 2.0, with a NOTICE in his name
  3.97  project:aot-release refines -> project:install  Released brain binaries are Native AOT; dev and local installs run JIT
  3.79  project:update part-of -> project:install  Updating brain is running brain install again after the package manager updated the binary
  1.90  project:aur part-of -> project:install  On Arch Linux brain installs from the AUR, with its binary in /usr/bin and its harness in /usr/share/brain/harness
  1.90  project:homebrew part-of -> project:install  On macOS, and on Linux and WSL outside Arch, brain installs from Homebrew
```

## Write nodes

Every command here but `subjects` writes a node, and every node write but a change of subjects alone waits for your yes.

### The node markdown

`add`, `edit` and `nearest --scope` read a node as the markdown of a node file:

```
---
subjects: [pending, window]
---
# A pending write waits a day for brain land, then it is swept

A staged write that nobody puts before the user is removed from cache/pending after a day.

## Why
Working state should not pile up in the cache.

## Edges
- refines -> user-says-yes
```

- The frontmatter between `---` lines is optional and holds only `subjects: [a, b]`. Any other field is refused: the
  command gives the kind, the namer, the day and the session.
- The title comes first, after the frontmatter, with or without `# `. It is one line of at most 25 words.
- The body follows, as long as it needs to be. Then `## Why` and the why, then `## Edges` with one `- rel -> target`
  line per edge. A part left out is written empty.
- Neither the body nor the why may hold a line that opens a section of the node file, such as `## Why`.
- An edge target is a bare slug in the node's own scope, `personal:slug`, `global:slug` or `project:slug` across scopes,
  `<name>:slug` for a project in `projects.tsv`, or a typed ref: `session:<id>`, `file:<path>` or `url:<http(s) url>`.
  A personal or global node names a project by its name, never `project:`. A `commit:` target is refused: git holds
  what happened.

Every edge is checked as [`link`](#brain-link) checks it before the write is judged, and lands with the node on your
yes, never before.

### What a staged write prints

A write that passes the gate prints `brain asks before it writes:`, what the command does, and the full text of every
node it writes or forgets, as the file will read or as it read. Then the pending id, and under it the nearest entries
of each node whose title, body or why it writes.

```
$ brain add project pending-sweep --file pending-sweep.md
brain asks before it writes:
  added project:pending-sweep

project:pending-sweep, new:
---
kind: information
subjects: [pending, window]
named-by: claude
born: 2026-11-09
---
# A pending write waits a day for brain land, then it is swept

A staged write that nobody puts before the user is removed from cache/pending after a day.

## Why
Working state should not pile up in the cache.

## Edges
- refines -> user-says-yes

pending 3f9a0c1b2d4e: nothing is written yet. Run `brain land 3f9a0c1b2d4e` alone, as its own command, in the background: the user reads the write in brain's window and answers, and the command returns the answer.

nearest to project:pending-sweep:
  brain:user-says-yes  Every node going into or out of brain waits for the user's yes, no or change  (brain)
  brain:answer-history  Every answer stays findable for 2 days in brain's window, and can be undone there  (brain)
```

A pending write keeps the session that staged it, `CLAUDE_CODE_SESSION_ID` or `add --session`. One that nobody puts
before you is swept after a day. A write the gate refuses is never staged: the command prints the refusal on stderr
and exits 1.

### brain add

Adds one node, born today. Use it for a single new node; use [`merge`](#brain-merge) for a block of them.

```
brain add <scope> <slug> ["<title>"] [--why <text>] [--body <text>] [--subjects <a,b>]
    [--file <path>] [--kind <information|idea>] [--named-by <user|claude|agent>] [--session <id>]
```

| Argument | Default | What it does |
|---|---|---|
| `<scope>` | none | `personal`, `global` or `project`. The scope must be in effect here. |
| `<slug>` | none | The file name, lowercase-hyphenated. A node of that slug in that scope is refused. |
| `"<title>"` | none | The title. With it, the node comes from the flags and carries no edge. Without it, the node is read as markdown from stdin or `--file`. |
| `--why <text>` | empty | With a title: why the node is true or worth keeping. |
| `--body <text>` | empty | With a title: the node's text. |
| `--subjects <a,b>` | none | With a title: its subjects, comma-separated. A node needs one. |
| `--file <path>` | stdin | Without a title: read the markdown from this file. |
| `--kind <information\|idea>` | `information` | What the node is. Information needs a why; an idea does not. |
| `--named-by <user\|claude\|agent>` | `claude` | Who named it. |
| `--session <id>` | none | The Claude session that wrote it, written to the node and kept with the pending write. |

A title with `--file`, or no title with `--why`, `--body` or `--subjects`, is a usage error. It refuses a bad slug, an
existing node, a title that is empty, more than one line or over 25 words, a bad `--named-by`, a kind that is not one
of the two, information without a why, no subject, an edge `link` would refuse, and whatever the judge refuses.

Waits for your yes. Exit 0 when it staged the write; 1 on any refusal.

```
$ brain add project pending-sweep "A pending write waits a day for brain land, then it is swept" \
    --subjects pending,window --why "Working state should not pile up in the cache."
```

### brain edit

Rewrites a node's title, body, why, subjects and edges from markdown. Use it when a node changed: a node is edited in
place, and git keeps the old text.

```
brain edit <ref> [--file <path>]
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | The node to rewrite. |
| `--file <path>` | stdin | Read the markdown from this file. |

The markdown is `add`'s. It replaces the whole of what it carries: a part it leaves out is written empty, so an edge it
does not carry goes. To keep an edge, carry it. Kind and the other fields stay; `update --kind` changes the kind. An edit
that leaves the node as it was prints `unchanged <ref>` and stages nothing. It refuses what `add` refuses for a title,
the subjects and the edges, and whatever the judge refuses.

Waits for your yes. Exit 0 when it staged the write or found nothing to change; 1 on a refusal.

```
$ brain edit project:update --file update.md
brain asks before it writes:
  edited project:update
...
```

### brain update

Changes parts of a node in place, from flags. Every line it does not name stays.

```
brain update <ref> ["<title>"] [--why <text>] [--body <text>] [--kind <information|idea>] [--subjects <a,b>]
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | The node to change. |
| `"<title>"` | unchanged | The new title. |
| `--why <text>` | unchanged | The new why. |
| `--body <text>` | unchanged | The new body. |
| `--kind <information\|idea>` | unchanged | The new kind. |
| `--subjects <a,b>` | unchanged | The new subjects, comma-separated. An empty value, `--subjects ""`, clears them. |

It needs at least one change; with none it is a usage error. A change of subjects alone carries no prose: it meets the
free rules, not the judge, writes at once, prints `updated <ref>` and needs no yes. Any other change is judged and waits
for your yes.

Exit 0; 1 on a refusal.

```
$ brain update project:install --subjects install,harness
updated project:install
$ brain update project:install --why "Links make one install path and show which files are brain's."
brain asks before it writes:
  updated project:install
...
```

### brain forget

Removes nodes and every edge that points at them, all or nothing. Forget a node that was never a lasting subject, and
the one of two duplicates that says less. A node that only changed is edited, not forgotten.

```
brain forget <ref...>
```

| Argument | Default | What it does |
|---|---|---|
| `<ref...>` | none | One or more nodes to remove. |

It removes each node's file and every edge into it from any node of the scopes in effect, `<name>:slug` edges of
personal and global nodes included, and from every other cloned project. The staged write shows each forgotten node in
full, then `forgot <ref>` and `  unlinked <edge>` for each edge it removes. When it changes another project's graph, it
adds `changed <repo>/docs/brain; commit it`. A missing node refuses the whole call, and so does a node outside the
scopes in effect: forget a project node from inside its repo. Git keeps the old files.

Waits for your yes. Exit 0 when staged; 1 when a node is missing or a refusal stops it.

```
$ brain forget project:pending-sweep
brain asks before it writes:
  forgot project:pending-sweep

project:pending-sweep, forgotten:
...
```

### brain merge

Applies a block of `+` lines from stdin, all or nothing: the way an agent's report of several nodes, subjects and edges
lands as one write.

```
brain merge < report.txt
```

It takes no arguments. It reads three kinds of line; any line that does not start with `+` is prose and is ignored.

```
+ scope:slug | title | kind | named-by | why      a node; the why is optional, an empty kind is information
+ scope:slug [a, b]                               a node's subjects: a node of this block or one that exists
+ scope:slug -rel-> scope:slug                    an edge, as link adds it
```

Nodes apply first, then subjects and edges. Every new node needs a subjects line. A node line for a node that exists is
skipped, not refused, and its line says so. Every line is tried on a rehearsal first. One line it cannot read, one line
that fails, or one refusal applies nothing: it prints each failure on stderr as `line N: <why>`, then
`merge: N failed, nothing applied` on stdout, and exits 1. A block that passes stages one pending write, whose lines are
`added <ref>`, `subjects <ref> [a, b]`, `linked <from> -<rel>-> <to>` and `merge: N applied, 0 failed`.

Waits for your yes, as one write. Exit 0 when staged; 1 on any failure.

```
$ brain merge <<'EOF'
Two nodes from the install review.
+ project:install-needs-jq | brain install needs jq on PATH | information | claude | Hooks parse their JSON with jq.
+ project:install-needs-jq [install]
+ project:install-needs-jq -part-of-> project:install
EOF
brain asks before it writes:
  added project:install-needs-jq
  subjects project:install-needs-jq [install]
  linked project:install-needs-jq -part-of-> project:install
  merge: 3 applied, 0 failed
...
```

### brain subjects

Lists the subjects in use, with how many nodes carry each, the most used first. Run it before you pick a subject, and
reuse a word in use before you coin a near-synonym.

```
brain subjects [scope]
```

| Argument | Default | What it does |
|---|---|---|
| `[scope]` | every scope in effect | One scope in effect, or the name of a project cloned here. |

Each scope prints a heading, `<scope>: N subjects in use`, then a line per subject and its count. It reads the nodes'
frontmatter and writes nothing. Exit 0; 1 on more than one argument or a scope that is neither in effect nor a cloned
project.

```
$ brain subjects project
project: 30 subjects in use
  brain-code          13
  window              10
  agents               8
  conversations        5
  search               4
```

## Edges

An edge is a line in its source node's `## Edges`: `- rel -> target`. A relation is lowercase-hyphenated. An edge's
weight is the sum of `0.5 ^ (days / 90)` over its travels, the node's born date counting as one, and edges of a node
named by the user count double.

### brain link

Adds an edge from one node to another, once. Use it between two nodes that exist already; a new node carries its edges
in its own write.

```
brain link <from-ref> <rel> <to-ref>
```

| Argument | Default | What it does |
|---|---|---|
| `<from-ref>` | none | The node that holds the edge. |
| `<rel>` | none | The relation, lowercase-hyphenated, such as `refines`, `part-of` or `relates-to`. |
| `<to-ref>` | none | The target: a node ref or a typed ref, as in [the node markdown](#the-node-markdown). |

A same-scope target is stored as a bare slug. It prints `linked <from> <rel> <to>`, or `edge exists: <from> <rel> <to>`
when the edge is there already, and then changes nothing. It refuses a bad relation, a target node that does not exist,
a malformed or `commit:` typed ref, a personal or global node that targets `project:`, and a project name that is not
in `projects.tsv`. Linking travels nothing.

Writes at once; no yes. Exit 0; 1 on a refusal.

```
$ brain link project:update relates-to project:homebrew
linked project:update relates-to project:homebrew
```

### brain unlink

Removes one edge from its source node. Every other line stays, and git keeps the old edge.

```
brain unlink <from-ref> <rel> <to-ref>
```

The arguments are `link`'s. An edge whose target node is gone, or whose target is a ref no longer accepted, can still be
removed, matched as the source writes it. It prints `unlinked <from> <rel> <to>`.

Writes at once; no yes. Exit 0; 1 when the node has no such edge (`brain: no edge <from> <rel> <to>`).

```
$ brain unlink project:update relates-to project:homebrew
unlinked project:update relates-to project:homebrew
```

### brain faded

Lists the weakest edges across every scope in effect, weakest first: the candidates to doubt.

```
brain faded [N]
```

| Argument | Default | What it does |
|---|---|---|
| `[N]` | `10` | How many edges, a whole number above 0. |

Each line is the weight and the edge as `from -rel-> target`. Exit 0; 1 when `N` is no positive number.

```
$ brain faded 3
0.84  project:one-door -uses-> nearest-entries
0.94  project:tool-disputes -respects-> user-says-yes
1.50  project:aur -part-of-> install
```

### brain fold

Rewrites the edge log of global, personal and every project in `projects.tsv` folded, one line per edge. It is the
one-off migration of the old logs that held a line per travel; exponential decay makes the fold exact, so every weight
reads the same after it. A log already folded is rewritten unchanged.

```
brain fold
```

It takes no arguments. For each log it prints `<path>: N lines -> M lines`, or `<path>: no log`. The logs are the data
repo's gitignored `cache/traversals/*.tsv`. Exit 0.

```
$ brain fold
/home/alice/.local/share/brain/cache/traversals/global.tsv: 118 lines -> 118 lines
/home/alice/.local/share/brain/cache/traversals/personal.tsv: 96 lines -> 96 lines
/home/alice/.local/share/brain/cache/traversals/lens-3f2a9c1b7d4e.tsv: no log
```

## Your yes, and the window

brain's window is a Chromium-family browser in app mode, served by `brain serve` on `127.0.0.1`. It is where you read
every pending write and answer it: yes, no, or no with your own words on what to change. An answer is kept for two days
in the window, where you can undo it. The harness's graph door refuses `brain serve` and `brain ui` from a Claude Code
session: the window is yours to open.

### brain land

Puts a pending write before you in brain's window and waits for your answer. A session runs it after every staged
write.

```
brain land <id>
```

| Argument | Default | What it does |
|---|---|---|
| `<id>` | none | The id a staged write printed. |

It lands nothing by itself. It moves the write to `cache/pending/asked`, where the window lists it, says so in one
line, and blocks until you answer. While it waits it keeps one desktop notification up through `notify-send` that says how many writes
wait; a click runs `brain ui`. Then it prints the answer:

- On a yes, the write's lines and `landed <id> on the user's yes`.
- On a no with your words, those words and the instruction to rewrite the node from them and stage it again.
- On a plain no, that nothing landed and the write is not to be staged again unasked.

Run again, it waits again or returns the answer already given. Run it alone, as its own command, never in a chain: the
harness's graph door lets exactly that through without a permission prompt. A session runs it in the background.

Exit 0 on a yes or a no. Exit 1 when the id names no pending write, when the write is gone without an answer after a
day, or when your yes could not land, such as a node that changed since it was staged; nothing landed then.

```
$ brain land 3f9a0c1b2d4e
pending 3f9a0c1b2d4e is before the user in brain's window, which `brain ui` opens; this command returns the answer
added project:pending-sweep
landed 3f9a0c1b2d4e on the user's yes
```

### brain serve

Runs brain's window server on `127.0.0.1` in the foreground and opens the window. You rarely run it yourself: `brain ui`
starts it in the background.

```
brain serve
```

It takes no arguments. The port is `BRAIN_PORT`, 47474 by default. It prints
`brain serve: listening on http://127.0.0.1:47474` on stderr, and a line for every answer and undo it receives. It
keeps running when the terminal that started it closes, and stops on an interrupt or a terminate signal. Its log, when
`brain ui` starts it, is the data repo's `cache/serve.log`. Only the window lands a write, and only this server answers
for the window.

Exit 0 once it stops. Exit 1 when the port is taken, or when `BRAIN_PORT` is no port. When no browser starts it says
`brain serve: no Chromium-family browser would start; set BRAIN_BROWSER to one` and keeps serving.

### brain ui

Opens brain's window, starting `brain serve` in the background when it does not run. There is one window: a new one
closes every window open before it. Bind it to a key of your own.

```
brain ui [--restart]
```

| Flag | Default | What it does |
|---|---|---|
| `--restart` | off | Have the running `brain serve` start its successor on the build published beside it, and say which build that is. The window already open moves onto the successor in place; one opens only when none was open. `brain install` runs it. |

It prints one line: `brain ui: opened brain's window; it closes every window open before it`, or
`brain ui: started brain serve on 127.0.0.1:47474, and it opens the window`. With `--restart` it prints the restart and
the build, or that no `brain serve` runs and there is nothing to restart. A write that waits for your answer stays
waiting through a restart.

Exit 0. Exit 1 when no Chromium-family browser is found, when `brain serve` does not answer within 20 seconds, or when
the port answers but not to this brain's key, such as an old `brain serve` or another brain's: the message names the
command that fixes it, such as `kill <pid> && brain ui`, or says to set `BRAIN_PORT`.

```
$ brain ui
brain ui: started brain serve on 127.0.0.1:47474, and it opens the window
```

## Read a project

### brain workspace

Opens a workspace window over one project folder: its file tree beside the file open in it, live as files change on
disk, with what git has uncommitted. A folder that is open already has its window brought forward.

```
brain workspace <path> [--serve]
```

| Argument | Default | What it does |
|---|---|---|
| `<path>` | none | The folder, relative to the working directory. It must exist. |
| `--serve` | off | Run the folder's server in the foreground. `brain workspace` starts it in the background. |

Each folder has a server and a window of its own, apart from brain's window. The server ends with its window. It
writes nothing but what the window's git pane asks of git: a stage, an unstage, a commit and a push. Every command the
window runs and the server's work are recorded in the data repo's `perf/workspace`, outside git. Its log is in the data
repo's `cache/workspace/<id>/serve.log`.

It prints `brain workspace: opened a window over <folder>`, or, for a folder already open,
`brain workspace: <folder> is open; brought its window forward` (on Hyprland) or `...; opened its window again`
elsewhere. Exit 0; 1 when the path is no folder or no Chromium-family browser is found.

```
$ cd ~/Projects/lens && brain workspace .
brain workspace: opened a window over /home/alice/Projects/lens
```

## Work with agents

### brain code

Starts a Claude Code session of an agent in brain code's window. It is a terminal replacement: sessions run your own
logged-in `claude`, drawn in brain's styling.

```
brain code [agent] [folder] [--resume <session-id>] [--serve] [--permission <session-id>]
```

| Argument | Default | What it does |
|---|---|---|
| `[agent]` | `brain`, the open agent | Any agent file's name, such as `immediate`. |
| `[folder]` | the working directory | The folder the session runs in. A positional that names an existing folder is the folder and any other is the agent, in either order. |
| `--resume <session-id>` | none | Move a session that runs elsewhere, as in a terminal, into the window. It continues by its id, in the folder it ran in. Close the terminal's copy yourself. |
| `--serve` | off | Run the window's server in the foreground. `brain code` starts it in the background. |
| `--permission <session-id>` | none | The PermissionRequest hook of a session, which the server gives each session: it reads the request on stdin, waits for your answer in the window and prints the decision. |

There is one brain code window, apart from brain's window and every workspace's. A second `brain code` adds a session
to it and brings it forward. A tool call that would ask your permission waits in the window, in its session, until you
allow or deny it. The server ends with its window, and every session with it. Every call the window makes and the
server's work are recorded in the data repo's `perf/code`, outside git.

It prints `brain code: started <agent> in <folder>, session <id>`, or for `--resume`
`brain code: moved session <id> into the window, in <folder>; close its terminal yourself`. `--permission` always
prints a decision and exits 0: with no window, or one that went away, it denies, so it never hangs.

Exit 0. Exit 1 on two folders, two agents, a path that names no folder, no Chromium-family browser, or a session whose
`claude` ended within a second, with why.

```
$ brain code
brain code: started brain in /home/alice/Projects/lens, session 0b5e0d2c-1f7a-4f7e-9a51-0c2f55b0a7d1
$ brain code immediate ~/Projects/lens
$ brain code --resume 0b5e0d2c-1f7a-4f7e-9a51-0c2f55b0a7d1
```

### brain ask

Lets an agent in a brain code session ask you questions in brain code's window, as one JSON document on stdin. It is
for agents, not for you: outside a brain code session it refuses and the agent asks in prose.

```
brain ask < ask.json
```

It takes no arguments. The document is:

```json
{
  "wait": true,
  "questions": [
    {
      "text": "Which **port** should the workspace server listen on?",
      "options": [
        { "label": "free", "description": "A free port of 127.0.0.1, picked at start" },
        { "label": "fixed" }
      ],
      "recommend": "free",
      "why": "Several workspaces run at once, and a fixed port would collide.",
      "multiple": false
    }
  ]
}
```

| Field | Needed | What it holds |
|---|---|---|
| `wait` | yes | `true` blocks until you answered every question, then prints your answers: run it in the background. `false` prints that the questions wait and returns; your answers come to the session as your next message. |
| `questions` | yes, at least one | The questions, in order. |
| `text` | yes | The question, in markdown. |
| `options` | no | A list of `{"label", "description"}`, the description optional. Labels are unique. Leave it out for an answer in your own words. |
| `recommend` | yes | What the agent would answer; with options, one option's label. |
| `why` | yes | Why it recommends that. |
| `multiple` | no, `false` | Lets you choose more than one option. Needs options. |
| `own` | no | Accepted and changes nothing: you may always answer in your own words, beside an option or alone. |

A field brain does not know is refused, so a misspelt one never passes as one left out. A bad document is refused with
every problem it has, one per line, before it reaches the window.

With `wait` true it prints `The user answered your questions <id> in brain code's window.`, then each question, what you
chose and your own words. When you close the questions without answering, it returns at once and says so, and exits 0.
With `wait` false it prints `brain ask: your questions <id> wait for the user in brain code's window. ...` and returns.

Exit 0; 1 outside a brain code session, with no running brain code window, or on a bad document.

## Conversations

A conversation is one topic, talked through over days with an agent in brain's window, in sittings. Each lives in its
own folder outside every graph: a project's in its `docs/brain/conversations/`, personal and global ones in the data
repo's `conversations/personal/` and `conversations/global/`. A conversation's ref is `<scope|project>:<slug>`, a
project by its name in `projects.tsv`. The slug is the day it was born and a six-character id, such as
`brain:2026-11-01-a3f9c2`, fixed at birth, so a new title never moves the folder.

### brain conversation

The one door to a conversation's folder. brain's window runs the same code, so it never writes a folder itself.

```
brain conversation start <scope|project> [--model <haiku|sonnet|opus|fable>] [--effort <low|medium|high|xhigh|max>]
brain conversation park <session-id> <scope|project> [--model <m>] [--effort <e>]
brain conversation sitting <ref> [--model <m>] [--effort <e>]
brain conversation rename <ref> "<title>"
brain conversation commit <ref>
brain conversation remove <ref>
brain conversation list
```

| Flag | Default | What it does |
|---|---|---|
| `--model <m>` | the `sitting` setting, `sonnet` unless set | The sitting's model: `haiku`, `sonnet`, `opus` or `fable`. |
| `--effort <e>` | the `sitting` setting, `low` unless set | The sitting's effort: `low`, `medium`, `high`, `xhigh` or `max`. |

The settings are the data repo's `settings.json`. `rename`, `commit`, `remove` and `list` take no flags.

- **start** makes a conversation in `personal`, `global`, `project` (the one in effect) or a project named in
  `projects.tsv`, titled `New conversation`, with its first sitting. It prints `started <ref>: <title>` and
  `  sitting 1 on <model> at <effort> effort, in <folder>`.
- **park** turns a Claude Code session into a conversation; the open agent runs it with `$CLAUDE_CODE_SESSION_ID` when
  you say to park. It reads the session's transcript under Claude Code's `projects/` folder, keeps only your words and
  the agent's answers, and writes them as the trail of sitting 1, already ended. The sitting runs on `--model`, else
  the model the transcript records, else the `sitting` setting's; a transcript records no effort, so it names one only
  with `--effort`. A missing or unreadable transcript, or one with no words, refuses and writes nothing. It does not
  commit. It prints `parked <ref>: <title>` and the sitting line.
- **sitting** ends the current sitting and starts the next. It prints `new sitting in <ref>: <title>` and the sitting
  line.
- **rename** gives it a new title, one line of at most 120 characters. Its folder stays where it is.
- **commit** stages that conversation's folder and nothing else, `TEMP/` excluded, and commits it in the scope's repo:
  the project, or the data repo. The message is its title and the current sitting's date, or `removed <ref>` for a
  folder that `remove` deleted. It refuses when anything else is staged there. It prints `committed <ref>: <hash>`.
- **remove** deletes the conversation's whole folder, `settled.md`, `state.md`, the trails, `research/` and `TEMP/`,
  and rebuilds the index. It refuses, deleting nothing, while a dream runs on it or brain serve has a live sitting agent
  on it. It does not commit: `conversation commit <ref>` commits the deletion. It prints `removed <ref>: <title>` and
  `  deleted <folder>; the deletion is not committed`.
- **list** rebuilds the data repo's `cache/conversations.json` from every `state.md` and prints each conversation, then
  a count. A `state.md` it cannot read is named on stderr, never dropped in silence.

Every change rebuilds `cache/conversations.json`. A new sitting and a rename refuse while a dream holds the
conversation's lock. Exit 0; 1 on a refusal or a wrong form, which prints every form as the usage.

```
$ brain conversation start brain
started brain:2026-11-09-c41e07: New conversation
  sitting 1 on sonnet at low effort, in /home/alice/Projects/brain/docs/brain/conversations/2026-11-09-c41e07
$ brain conversation rename brain:2026-11-09-c41e07 "How the install restarts the window"
renamed brain:2026-11-09-c41e07: How the install restarts the window
  sitting 1 on sonnet at low effort, in /home/alice/Projects/brain/docs/brain/conversations/2026-11-09-c41e07
$ brain conversation sitting brain:2026-11-09-c41e07 --model opus --effort high
new sitting in brain:2026-11-09-c41e07: How the install restarts the window
  sitting 2 on opus at high effort, in /home/alice/Projects/brain/docs/brain/conversations/2026-11-09-c41e07
$ brain conversation list
brain:2026-11-01-a3f9c2  Search tiers  (active, 3 sittings)
brain:2026-11-09-c41e07  How the install restarts the window  (active, 2 sittings)
conversations: 2, 0 unreadable
```

### brain dream

Distills a conversation's unsettled sittings into its `settled.md`, what the conversation agreed so far. With `--deep`
it then carries what was settled into the graph.

```
brain dream <ref> [--deep]
```

| Argument | Default | What it does |
|---|---|---|
| `<ref>` | none | The conversation, `<scope|project>:<slug>`. |
| `--deep` | off | Dream into brain: settle first, then have the open agent update the graph from `settled.md`. |

The two models of the `dreamer` setting, Haiku and Sonnet at medium effort unless set, each draft the whole new
`settled.md`, and each says whether it is sure. When both are sure and Sonnet finds the drafts agree, Sonnet's draft
lands; otherwise Opus gets the sittings and both drafts, and its draft lands. A dream writes the conversation only, never
the graph: `settled.md`, and in `state.md` its id on each sitting it settled, so the next sitting's seed carries only
the unsettled ones. Every dream is logged in the data repo's `dreams/<id>.json` and committed there, alone.

It prints `dreamed <ref>: dream <id> settled sittings 1, 2`, a line per draft, `sure` or `unsure` with the doubt, the
outcome, and where it is logged. It refuses while a sitting is open (end it first with `brain conversation sitting`),
when no sitting is unsettled, while another dream holds the conversation's lock, and when the data repo has anything
else staged. A model that answers nothing fails the dream, and nothing changes.

With `--deep` it settles the unsettled sittings first, when there are any. Then it starts the open agent on the
`deep-dream` setting, Opus at medium effort unless set, in the conversation's repo. The agent searches the graph and
updates, forgets and adds nodes. **Each write waits for your yes in brain's window, one node at a time**, and the agent
commits each write once it lands. It never writes `settled.md` or a trail, and never commits the conversation's folder.
Its final words are printed, and logged and committed as a deep dream.

Exit 0; 1 on a refusal, or when the deep dream's agent failed.

```
$ brain dream brain:2026-11-09-c41e07
dreamed brain:2026-11-09-c41e07: dream 2026-11-09-7c2d91 settled sittings 1, 2
  haiku: sure
  sonnet: sure
  agreed: sonnet's draft is settled.md now
  logged and committed in /home/alice/.local/share/brain/dreams/2026-11-09-7c2d91.json
```

## Upkeep

### brain check

Validates every node of every scope in effect, and reads every other cloned project for edges whose target is gone.
Run it after a hand change to a graph, or before you commit one.

```
brain check
```

It takes no arguments. Each problem goes to stderr, one per line. A registered project an edge names that is not cloned
here is a `warning: ` line, never an error. A node in the old format or the old model is an error; information without a
why is a migration line on stderr, never an error. The last line, on stdout, is the summary.

Exit 0 when there is no error; 1 on any error.

```
$ brain check
check: 163 nodes, 412 edges, 0 errors in project, global, personal
```

### brain voices

Syncs your own typed messages from every Claude Code transcript into the data repo's `voices/`, one file per session.
A file is rewritten only when it changed.

```
brain voices
```

It takes no arguments. The transcripts are `BRAIN_TRANSCRIPTS`, or `projects/` in Claude Code's config directory. It
prints one summary line. It does not commit. Exit 0; 1 when the transcripts folder does not exist.

```
$ brain voices
voices: 412 transcripts, 298 sessions with typed messages, 5120 messages, 3 files written, 295 unchanged, 1843302 bytes
```

### brain migrate

Moves every node file of the scopes in effect from the old format, a one-line label under `# slug`, to frontmatter,
title, body and why, and every node of the old model to `information` or `idea`. It is mechanical: no model and no
judge. A label splits at its first "because" into title and why.

```
brain migrate [--dry-run]
```

| Flag | Default | What it does |
|---|---|---|
| `--dry-run` | off | Print every node it would write, its label, title and why, and each kind it would change; write nothing. |

A file already current is left alone, so a second run changes nothing. One file it cannot read refuses the run before
anything is written. It writes the files directly, with no yes, and commits nothing. The last line counts what it did.

Exit 0; 1 on a refusal.

```
$ brain migrate --dry-run
migrate: would migrate 0 nodes, 0 with a why and 0 without; 0 moved to the new model; 163 already in the new format
```

### brain init

Gives the repo you stand in a `docs/brain/graph` and registers it in the data repo's `projects.tsv`, so its graph is a
scope here and its name is a scope from anywhere.

```
brain init [name]
```

| Argument | Default | What it does |
|---|---|---|
| `[name]` | the repo folder's name, made lowercase-hyphenated | The project's name in `projects.tsv`. |

It prints `created <repo>/docs/brain`, or `exists <repo>/docs/brain`, then `registered <name> <path> <remote>` or
`already registered ...`. The path is written with `~` for your home, and the remote is `origin`'s url, or `-`. It
commits nothing.

It refuses outside a git repo, in the data repo itself, a name that is a scope or a ref kind (`personal`, `global`,
`project`, `commit`, `session`, `file`, `url`), a name that is not lowercase-hyphenated, a name registered for another
repo, and a repo registered under another name. Exit 0; 1 on a refusal.

```
$ cd ~/Projects/lens && brain init
created /home/alice/Projects/lens/docs/brain
registered lens ~/Projects/lens git@example.org:alice/lens.git
```

## Your use of brain

### brain stats

Reports what brain cost and what it gave back, from the usage log. Each section opens with its line of the story, the
evidence, and its first 5 cases, then says how many more it counted; `--json` and brain's window list every case.

```
brain stats [--since <yyyy-mm-dd>] [--until <yyyy-mm-dd>] [--json]
```

| Flag | Default | What it does |
|---|---|---|
| `--since <yyyy-mm-dd>` | the start of the log | Count only events from this day on. |
| `--until <yyyy-mm-dd>` | today | Count only events up to and including this day. |
| `--json` | off | Print the report as JSON. |

The sections are saves, validator rounds, knowledge reuse, readiness, guards, the judge, Navarone when its events are
there, cost, hit rate and rework. A token is estimated as four characters of output, and the report says so. A line of
the log it cannot read is counted and left out. It only reads.

Exit 0; 1 on a day that is not `yyyy-mm-dd`.

```
$ brain stats --since 2026-11-09
brain stats, from 2026-11-09 to today: 91 events. Tokens are estimates: output characters divided by 4.

Saves
  Without brain, your agent would have gone off the rails 0 times. You corrected it.
  0 answers: 0 yes, 0 no, 0 no with words

Knowledge reuse
  Without brain's graph, your agents would have started from zero 10 times, not knowing what you decided up to 22 days earlier.
    - brain:open-source-install: 15 days old, read in session 16a6e382-29b3-46f3-8e1e-cf10a92e53d9
...
```

### brain record

Logs the JSON of a Claude Code hook, on stdin, in the usage log as a fact. The harness's hooks run it; you do not.

```
brain record [--refused <hook>] [--reason <text>]
```

| Flag | Default | What it does |
|---|---|---|
| `--refused <hook>` | none | Log the tool call as refused by this hook, instead of what it carries. The harness's `refusal.sh` passes it when a PreToolUse hook refused. |
| `--reason <text>` | none | With `--refused`: the first line the hook said, cut to 200 characters. |

An Agent call logs a spawn, a SendMessage a message, a Skill a skill load; any other tool logs nothing. A readiness
check's stop logs its verdict; any other agent's stop logs nothing. It prints nothing and writes no `call` line of its
own. Exit 0; 1 when stdin is not JSON.

## Install

### brain install

Installs brain into Claude Code, showing each change on disk with its diff before it asks. Run it again after your
package manager updated brain: that is the update.

```
brain install [--yes] [--permission-mode <mode>] [--harness <dir>] [--harness-parts <list>]
```

| Flag | Default | What it does |
|---|---|---|
| `--yes` | off | Answer yes to everything, take each default, and replace your own CLAUDE.md and settings.json. |
| `--permission-mode <mode>` | asked | The mode Claude Code starts in, without the menu: `default`, `acceptEdits`, `plan`, `auto`, `dontAsk` or `bypassPermissions`. |
| `--harness <dir>` | the installed harness | Link the parts `--harness-parts` names to this harness, such as a checkout's `harness/`. It must hold a `settings.json`. |
| `--harness-parts <list>` | all, with `--harness` | Comma-separated, from `CLAUDE.md`, `agents`, `hooks`, `skills`. A list needs `--harness`. |
| `--harness-parts none` | | Link every part to the installed harness, a part linked to a checkout included. |

It checks first that `git`, `jq` and `claude` are on PATH, and stops on Windows itself; each missing tool is named.
Then it says what brain is and what it costs, asks where to keep your data repo (`BRAIN_HOME`, or
`~/.local/share/brain`), asks the permission mode (a first install never offers `auto` as the default), shows every
change with its diff, asks replace or keep for a CLAUDE.md or settings.json of your own, and asks
`Do you want to continue? [y/N]`. Nothing changes before that yes.

Everything it puts in Claude Code's config directory, `CLAUDE_CONFIG_DIR` or `~/.claude`, is a link but
`settings.json`, which it writes. A packaged binary links to the harness its package put in `../share/brain/harness`
beside it; a bare binary unpacks the harness it carries into the data repo's `cache/harness`. It also creates the data
repo when there is none, links `~/.local/bin/brain` (or `LOCAL_BIN`) when the binary was built from a checkout, links
the data repo's own `harness/` beside brain's, downloads the embedding model the settings name, and restarts a running
window with `brain ui --restart`. Whatever of yours stands in the way moves to the config directory's
`backups/pre-install-<epoch>/`, and `backups/brain-install.tsv` records it all for `brain uninstall`. Running it again
overwrites edits to the installed harness; a part linked to a checkout stays linked there.

It ends with a count, `install: N linked, N written, N backed up, N already in place, N kept, N downloaded`, and, when
your data repo is not at the default, the `export BRAIN_HOME=...` your shell profile needs. Run `brain uninstall`
before you remove brain's package: a package manager runs nothing per user.

Exit 0 when installed or nothing changed. Exit 1 when a tool is missing, when `--harness` names no harness, or when you
answer anything but yes; nothing changed then.

```
$ brain install --permission-mode acceptEdits
...
That is every change: 14 in all. Nothing has been changed yet.
Do you want to continue? [y/N] y
...
install: 12 linked, 1 written, 0 backed up, 0 already in place, 0 kept, 1 downloaded
Undo it with brain uninstall. Run it before you remove brain's package: a package manager runs nothing per user, so it cannot take brain out of /home/alice/.claude.
```

### brain uninstall

Puts back what every `brain install` changed, from its record. Your data repo stays.

```
brain uninstall [--yes]
```

| Flag | Default | What it does |
|---|---|---|
| `--yes` | off | Answer yes to the question. |

It says what it will undo and asks `Do you want to continue? [y/N]`. It removes every link brain made that still points
where brain pointed it and every file brain wrote that still holds what it wrote; one you edited since moves to
`backups/uninstall-<epoch>/`. It puts back every file of yours from before brain, removes the embedding model, the
record and every directory brain made that is left empty. It never touches the data repo, and says where it is.

It ends with `uninstall: N removed, N moved, N restored, N left`. With no install record it says there is nothing to
undo. Exit 0; 1 when you answer anything but yes.

## Global flag

### --project

Puts a project's graph in effect from any directory, for one command. An agent that may not leave its own folder uses
it to read or write another project's graph.

```
brain --project <name|path> <command> [arguments]
```

It must come first, before the command. A name is looked up in `projects.tsv`; anything else is a path inside the repo,
relative to the working directory. That project's scope is then in effect as `project`, under the same rules as inside
it. It refuses a value that is neither a registered project nor a git repo, a registered project not cloned here, the
data repo, and a repo without `docs/brain/graph` (`run brain init there`). Exit 1 on a refusal.

```
$ brain --project lens lookup render loop
$ brain --project ~/Projects/lens subjects project
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | It did what it was asked, or printed help. A pending write staged is a 0: it waits, it did not fail. A `brain land` that returns a no is a 0 too. |
| 1 | A refusal: a usage error, a bad ref, a missing node, a write the gate refused, a merge with a failed line, an error `brain check` found, an install or uninstall you did not confirm. Also any failure of brain or the machine, which the usage log keeps whole. |
| 2 | No command at all (the overview goes to stderr), or a command brain does not know, also as `brain help <name>`: `brain: unknown command: <name>`. |

## Environment variables

| Variable | Default | What it does |
|---|---|---|
| `BRAIN_HOME` | `$XDG_DATA_HOME/brain`, or `~/.local/share/brain` | The data repo: personal and global graphs, `projects.tsv`, settings, logs and caches. |
| `XDG_DATA_HOME` | `~/.local/share` | Where the default data repo lives when `BRAIN_HOME` is unset. |
| `BRAIN_PORT` | `47474` | The port of brain's window server on 127.0.0.1. `serve`, `ui` and `install` read it. |
| `BRAIN_BROWSER` | the first Chromium-family browser on PATH, or in /Applications on macOS | The browser every brain window opens in. |
| `CLAUDE_CONFIG_DIR` | `~/.claude` | Claude Code's config directory: where `install` links the harness, where the judge reads the brain skill, and where transcripts are found. |
| `BRAIN_TRANSCRIPTS` | `projects/` in Claude Code's config directory | The Claude Code transcripts `voices` and `conversation park` read. |
| `CLAUDE_CODE_SESSION_ID` | none | Set by Claude Code. The session a call, a pending write and a travel belong to. Without it, `node` travels nothing. |
| `BRAIN_JUDGE` | on | `off` turns the judge off; the free rules still run. |
| `BRAIN_NOTIFY_SEND` | `notify-send` | The program `land` shows its notification with; `off` shows none. |
| `BRAIN_NOTIFY_CLICK` | `brain ui` | A command, run through `sh -c`, for a click on that notification. |
| `BRAIN_MODEL_URL` | the urls each model pins | A mirror `install` downloads the embedding model from, at the same paths. The hashes stay pinned. |
| `LOCAL_BIN` | `~/.local/bin` | Where `install` links `brain` when the binary was built from a checkout. |
| `XDG_CONFIG_HOME` | `~/.config` | `install` links the data repo's `harness/guard` to `brain/guard` under it, when there is one. |
| `BRAIN_CODE_SESSION` | none | Set by brain code's server in each session it runs, so `brain ask` finds its session. Do not set it yourself. |
| `HYPRLAND_INSTANCE_SIGNATURE` | none | Set by Hyprland. With it, `workspace` and `code` bring an open window forward through `hyprctl`; without it they open a fresh one. |

One command is not in the help: `brain keep-agent`, the keeper brain puts between itself and every `claude` it starts,
so that each one ends when brain does. It is not for people.
