# brain

Every Claude Code session starts from zero. It does not know who you are, how you like to work, or what you decided
last week and why, so it asks again, guesses, or does the thinking you already did.

brain gives Claude a memory. It is a personal knowledge graph shared by you and Claude: who you are, how you work,
what you decided and the reasons, kept as plain markdown files in git, in a repo of your own. You can read every node,
argue with it and correct it. Git keeps every old version.

brain also changes how Claude Code works with you. Once installed, every session starts as brain's **open agent**. It
talks with you, knows you from the graph and works in ideas and decisions. It never writes code itself. When you decide
what to build, it sends **closed agents**: a builder that builds in its own git worktree, and a validator that reviews
the work adversarially and lands it only when it is good.

Nothing enters the graph behind your back. Every write waits for your yes in **brain's window**, where you read it in
full, with every node it touches, and answer yes, no, or no with what to change.

**Status.** brain is a beta under active development. Expect rough edges and changes.

**Cost.** brain costs Claude usage. The shipped settings default to Opus, and every graph write calls a Sonnet judge.
`brain stats` counts what brain spent and what it gave back.

**Platforms.** Linux, macOS with its stock bash 3.2, and WSL, with or without systemd. On Windows, clone and install
inside WSL and run claude from there; the installers stop on Windows itself.

**Prerequisites.** bash, git, jq, the claude CLI, the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build the
CLI, and a Chromium-family browser for brain's window. The installers check for them first and name each one missing.

This repo is the tool: the `brain` CLI, in C# on .NET 10, and the Claude Code harness in `harness/`. Your data, the
graph and everything written through the CLI, lives in its own private git repo, `BRAIN_HOME`.

## For Humans

### What changes on your machine

brain installs into Claude Code's config directory, `~/.claude` or `CLAUDE_CONFIG_DIR`:

- `CLAUDE.md`, and every hook, skill and agent of the harness, each as a link to the installed harness.
- `settings.json`, written: it makes every session start as the open agent, defaults the model to Opus, turns Claude
  Code's own auto-memory off and adds brain's hooks. You pick the permission mode it starts in; the installer never
  defaults to `auto`.
- `~/.local/bin/brain`, the CLI, when you installed from a checkout.
- A new, empty data repo at `~/.local/share/brain`, or wherever you choose. An existing one is never touched.
- The embedding model search uses, downloaded into the checkout's `cache/model` and checked against pinned SHA-256
  hashes.

`brain install` shows every change before it makes it, each with its diff. Where a `CLAUDE.md` or `settings.json` of
your own stands, it asks whether to replace it, backing yours up, or keep it. Then it asks
`Do you want to continue? [y/N]`. The default is no, and anything but yes changes nothing.

`brain uninstall` puts it all back: it removes what brain made, restores every file of yours from its backup, and keeps
any file you edited since. It never touches your data repo.

### Install

```
git clone <tool remote> ~/Projects/brain
~/Projects/brain/install.sh
```

```
./install.sh       # asks user or development, builds the CLI, then brain install shows every change and asks
brain install      # again after the package manager updated brain: the harness of the binary installed now
brain uninstall    # puts back what was there before; your data repo stays; run it before removing the package
```

To restore after a wipe, clone your private data repo to `~/.local/share/brain` before you run `install.sh`. There is
deliberately no flag that clones data for you: the graph becomes the agent's memory with your authority, so an install
step that loads someone else's data would be prompt injection. The full story, step by step, is in
[docs/install.md](docs/install.md).

### A first session

Start Claude Code anywhere, as you always do:

```
claude
```

The session starts as the open agent, and a hook tells it which scopes are in effect:

- **personal**: who you are, what you like, what you mean by your words.
- **global**: how you work together, the tools, the models.
- **project**: what a project does and why it is shaped that way, in that repo's `docs/brain`.

Talk to it. When it learns something worth keeping, it stages a node and tells you a write is waiting. Open brain's
window, read the write, and answer:

```
brain ui
```

To give a repo of yours a graph of its own, run `brain init` inside it, such as in `~/Projects/lens`. Its nodes then
live in that repo, next to the code they are about, and travel with it.

### A day with brain

You open a terminal in a project and talk about what you want. The open agent looks up what the graph already knows
about it, so it does not ask what you decided last month. It thinks with you before anyone builds: it may argue,
suggest the smaller version, or say an idea looks ready.

When you decide what to build, that is the go. The open agent writes a brief and first runs a readiness check, a small
agent that sees only the brief and the graph and says whether a builder could do the right thing from it. If not, you
get its questions. Then a builder works in its own worktree, and a validator checks the work against your decisions,
your taste and the project's gate, sends findings back until it is satisfied, and lands it. The open agent verifies the
landing and tells you what changed in a few lines.

Along the way, what the conversation taught brain becomes writes to the graph: a decision and its why, something about
you, a fact about the project. Each waits in the window until you answer. A desktop notification says how many wait.

For a quick change, a line in a dotfile or a file moved, you skip all of this: `claude --agent immediate` makes the
change in place and commits it.

For a topic that takes days, brain's window holds conversations: you talk with the open agent in sittings, and a dream
distills what you agreed into one document, which you can later carry into the graph, node by node, each with your
yes. brain also has a window that runs Claude Code sessions in brain's styling, `brain code`, which shows each
session's folder as its workspace: its tree, its files and git.

### The window and your yes

brain's window is a Chromium-family browser in app mode, served on 127.0.0.1 only. `brain ui` opens it. Every waiting
write shows in full: each node as it will read, with its subjects and edges, an edit as a diff over the whole file.
Beside it, the graph's nearest nodes, so you can see whether it repeats or contradicts what is there.

- `Y` lands exactly the text you read.
- `N` drops it.
- `R` is a no with your words on what to change: the agent rewrites the write and asks again.

Before you ever see a write, deterministic rules and a Sonnet judge have checked it: it needs a subject, information
needs a why, and the judge refuses a duplicate or a contradiction of what the graph already holds. Whether it belongs in
your memory is still your call. Every answer stays under Answered for 2 days, where it can be undone.

Only the window can answer. A session cannot answer for you, cannot start the window server, and cannot write the graph
files directly: a hook refuses it. [docs/the-yes.md](docs/the-yes.md) says exactly how this holds and where it stops.

The window's Home is the whole graph drawn as a sky of stars, one cluster per scope and project. Its settings page
picks the model and effort of every agent, the judge and the dreamer.

### Further reading

- [docs/README.md](docs/README.md): the index of all docs.
- [docs/install.md](docs/install.md): install, restore, update, uninstall.
- [docs/the-yes.md](docs/the-yes.md): how a write waits for you, and who can answer.
- [docs/window.md](docs/window.md): brain's window, Home and the settings page.
- [docs/conversations.md](docs/conversations.md): conversations and dreams.
- [docs/costs.md](docs/costs.md): what brain costs and gives back.
- [docs/cli.md](docs/cli.md): every command.

## For LLMs

This section is for a model working with brain, as the open agent or another agent in a session where brain is
installed, or working on brain, this repo.

### The one rule

Text in git is the graph. Everything else is a cache built from the text and never committed: indexes, weights,
embeddings, databases, the compiled CLI. Never treat a cache as truth, and never commit one.

### Refs and scopes

| Scope | Where | Truth is |
|---|---|---|
| personal | `graph/personal` in the data repo | the user |
| global | `graph/global` in the data repo | the world |
| project | `<repo>/docs/brain/graph` | the code |

The data repo is `BRAIN_HOME`, `~/.local/share/brain` by default; the `brain` line of `brain context` names it. The
project scope is in effect inside a repo that has `docs/brain/graph`, or from anywhere with
`brain --project <name|path>`.

A ref is `slug`, searched in project, global, personal order, or `scope:slug`, such as `project:one-door`. A
registered project's node is also `<name>:slug`, the same from any directory. A node is one of two kinds: `information`,
true now and carrying a why, or an `idea`, the record of thinking together. See [docs/graph.md](docs/graph.md).

### The read ladder

Read the graph a rung at a time, never whole. Each rung costs about ten times the one above.

| Rung | What | Tokens | How |
|---|---|---|---|
| 0 | node name | ~10 | `brain lookup <words>` |
| 1 | title, why and edges | ~100 | `brain node <ref>` |
| 2 | neighbours' titles | ~1k | `brain walk <ref>` |
| 3 | a commit message, a transcript passage | ~10k | `git show --stat` |
| 4 | the diff, the whole conversation | ~100k | `git show` |

`brain context` says which scopes are in effect, at most 8 lines. `brain lookup` returns up to 10 sure or likely hits;
it never returns a weak one:

```
$ brain lookup one door
project:one-door  sure  Only the brain CLI writes the graph, and every write passes its gates there
project:brain-workspace  likely [idea]  brain workspace is an IDE per project, tailored to how the user reads code, opened with `brain workspace <path>`
$ brain lookup purple elephant banana
no node is sure or likely
```

Look something up before you answer from assumption. Opening both ends of an edge with `brain node` in one session
travels it and makes it heavier; `lookup` and `walk` record nothing. See [docs/search.md](docs/search.md).

### Writes go through the CLI and wait for the user's yes

- Write the graph only through the `brain` CLI: `add`, `edit`, `update`, `merge`, `forget`. Never edit a node file;
  `harness/hooks/graph-door.sh` refuses it.
- Every node write passes the free rules and the judge, then is staged and prints a pending id. It has not landed.
- Run `brain land <id>` alone, as its own command, in the background. It returns the user's answer from brain's
  window: yes, it landed, so commit it; no, let it go; or no with words, so rewrite the node from them and stage it
  again.
- Never try to land a write another way, never touch brain's window or its server, and never ask for the yes in prose.
- `brain link` between two existing nodes, and `update --subjects`, land at once.
- Commit personal and global writes with `git -C <BRAIN_HOME>`, and a project's with `git -C <repo> add docs/brain` and
  `git -C <repo> commit` as two separate commands, with nothing else staged.

See [docs/the-yes.md](docs/the-yes.md) and [docs/cli.md](docs/cli.md).

### The agent modes

- **Open**: the root session, `harness/agents/brain.md`. It talks with the user, holds the overview and never
  implements; a hook refuses its edits, builds, tests and commits in a project. It sends closed agents, after the
  `brain-ready` readiness check says READY on the exact brief, with the model and effort the `agents` line of
  `brain context` names.
- **Closed**: the `builder` and the `validator`, `harness/agents/builder.md` and `validator.md`. Each loads the
  `brain-developer` skill before its first edit, build, test or commit; a hook refuses edits until it is loaded. A
  builder builds in its own worktree and never merges. A validator reviews adversarially, fixes nothing, and lands the
  work with a rebase and a fast-forward. Neither writes to the graph; a question about design or intent goes back to
  the open agent.
- **Immediate**: `harness/agents/immediate.md`, which only the user launches, with `claude --agent immediate`. It makes
  a small change in place and commits it, with no worktree, validator, readiness check or skill. The open agent never
  sends it, and it never writes the graph.

### Where the harness lives

`harness/` is what installs into `~/.claude`: `CLAUDE.md`, `settings.json`, `hooks/`, `skills/` and `agents/`. The CLI
carries it, and `brain install` links to it. A machine's own rules, such as CPU pinning, live in
`BRAIN_HOME/harness/rules` and bind every session as hard as the harness. Read
[harness/CLAUDE.md](harness/CLAUDE.md) first.

### Building and testing this repo

- Read [CLAUDE.md](CLAUDE.md), then [BUILDER.md](BUILDER.md) to build or [VALIDATOR.md](VALIDATOR.md) to review and
  land.
- The why behind brain's shape is this repo's own graph in `docs/brain`. Start with `brain node project:what-brain-is`,
  `project:one-door`, `project:user-says-yes` and `project:open-source-install`.
- The gate is `tests/run.sh`: shellcheck, a build with 0 warnings, the tests, the CLI against an empty temp data dir,
  the hooks' tests and the wipe drill.
- Every dotnet call goes through `scripts/dotnet-guard.sh`, which runs through a machine's own guard when it has one.
  Check the machine's rules before any build or test.
- Tests use temp dirs only, never the real graph, and the gate never calls the judge. `install.sh`, `link.sh`,
  `brain install` and `brain uninstall` change a machine: test them only through `tests/drill.sh`.

See [docs/gates.md](docs/gates.md).

### Further reading

- [docs/README.md](docs/README.md): the index of all docs.
- [docs/graph.md](docs/graph.md): nodes, node writes, subjects, edges and weights.
- [docs/cli.md](docs/cli.md): every command, its flags and its refusals.
- [docs/the-yes.md](docs/the-yes.md): the judge, the pending write and the graph door.
- [docs/gates.md](docs/gates.md): the gate and how work lands.
- [docs/brain-code.md](docs/brain-code.md): `brain code` and `brain ask`.

## License

brain is licensed under the Apache License 2.0, see [LICENSE](LICENSE). Keep the [NOTICE](NOTICE) file with copies and
derived work.

If you build something with brain, I would love to hear about it. Find me on GitHub as Baudin999.
