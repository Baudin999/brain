# The graph

This file covers what the graph is made of: the one rule that separates text from cache, where everything lives on
disk, the three scopes, the shape of a node and of a node write, subjects, edges and their weights, the rungs a reader
climbs, and how a node is forgotten. How the graph is searched is in [search.md](search.md), and how a write waits for
your yes is in [the-yes.md](the-yes.md). Every command named here has its full reference in [cli.md](cli.md).

## The one rule

Text in git is the graph. Everything else is a cache built from the text and never committed: indexes, weights,
embeddings, databases, the compiled CLI. The CLI build and the embedding model live in this repo's `cache/`. The derived
data caches live in the data repo's `cache/`. `.gitignore` keeps both out.

## Layout

There are two repos. This repo is the tool: the `brain` CLI and the Claude Code harness. The data, the graph and
everything written through the CLI, lives in its own private git repo, `BRAIN_HOME`.

This repo:

```
README.md            what brain is, for humans and for models
docs/                the reference: this folder; docs/brain is this repo's own graph
bin/brain            launcher for the CLI; runs cache/publish/brain
build.sh             publishes src/Brain into cache/publish through scripts/dotnet-guard.sh
install-aot.sh       installs brain with a Native AOT build of the CLI, a trial; ./install.sh puts the JIT build back
harness/             the Claude Code harness: CLAUDE.md, settings.json, hooks/, skills/, agents/; the CLI carries it
src/Brain/           the CLI, C# on .NET 10
tests/Brain.Tests/   xUnit tests; temp dirs only, never the real graph
tests/run.sh         the gate
tests/drill.sh       the wipe drill: clones this repo, installs it into temp dirs, checks the result
tests/hooks.sh       the hooks' tests
install.sh           the bootstrap of a checkout: asks user or development, builds the CLI when needed and hands
                     over to brain install, or to link.sh
link.sh              development: links the parts of the harness you pick to this checkout, through brain install
scripts/             dotnet-guard.sh: every dotnet call, through ~/.config/brain/guard when a machine has one;
                     search-eval.sh: scores lookup against a test set of queries
cache/               derived and gitignored: publish/, the CLI build, and model/, the embedding model
```

The data repo, `BRAIN_HOME`, is `${XDG_DATA_HOME:-~/.local/share}/brain` by default:

```
graph/personal/      nodes about the user: who they are, what they like
graph/global/        nodes about how we work and about the world: tools, models
projects.tsv         every project with a docs/brain: name, path, remote, date; written by brain init
voices/              the user's own typed messages, synced from Claude Code transcripts
harness/             optional, your own machine's harness: hooks/, skills/, agents/, rules/, settings.json, guard
conversations/       personal/ and global/: each scope's conversations, one folder each, outside the graph
dreams/              the dream log, one JSON file per dream, each committed by its dream
usage/               the usage log, one JSON line per event in a file per month, outside the graph
perf/                the window's perf log, CSV files of 1 MB at most, ignored by git and kept 14 days; workspace/,
                     each workspace's records, and code/, brain code's, a JSON line each in a file per day, ignored by
                     git and kept 14 days
settings.json        the settings page's choices, committed; a missing file or key is the default
sources.tsv          optional, written by you: other tools whose questions the window shows
cache/               derived and gitignored: trail.sqlite, search.sqlite, traversals/<scope>.tsv, conversations.json
```

Two environment variables move these places. `BRAIN_HOME` moves the data repo. `BRAIN_TRANSCRIPTS` moves the
transcripts directory, `~/.claude/projects` by default. Tests and the restore drill use both.

## Scopes

| Scope | Where | Truth is |
|---|---|---|
| personal | `graph/personal` | the user |
| global | `graph/global` | the world |
| project | `<repo>/docs/brain/graph` | the code |

The project scope is in effect when the working directory is inside a git repo that is not the brain and that repo has
`docs/brain/graph`, or when `brain --project <name|path>` names one from anywhere: a name from `projects.tsv` or a path
inside the repo. Every command then reads and writes that project's scope under the same rules. An unknown name, a
project not cloned here and a repo without `docs/brain/graph` are refused. A project's `docs/brain` holds `graph/`, and
its conversations in `conversations/` (see [conversations.md](conversations.md)). [`brain init`](cli.md#brain-init) gives a
repo its `docs/brain` and registers it in `projects.tsv`.

## Refs

A ref names a node. It is `slug`, searched in project, global, personal order, or `scope:slug`, such as
`personal:tea` or `project:one-door`. A node of a registered project can also be named `<name>:slug`, by its name in
`projects.tsv`, such as `studio:cups`; that ref opens the same node from any directory.

## Nodes

One file per node, `<scope dir>/<slug>.md`, the slug lowercase and hyphenated:

```
---
kind: information
subjects: []
named-by: user
born: 2026-10-03
session: f1d7df00-04e9-4bbc-a483-2a380757d424
---
# Fine Elwynn ground cover on open ground

Grass first, then moss, lit like the ground.

## Why
Bare ground read as unfinished.

## Edges
- part-of -> wg-12
```

A node is a document: a title, prose that says what it is, and a why. It is one of two kinds.

- `information` is what is true now. Its scope says which kind of information it is: personal is about the user,
  global is general, a project is functional and technical. Information carries a why.
- An `idea` records thinking together: what we discussed, what we rejected, and the reasons. An idea needs no why.

The frontmatter holds `kind`, `subjects`, `named-by`, `born` and an optional `session`. `subjects` is a list of
lowercase-hyphenated words, `[]` when it has none; a node written with `add`, `edit` or `merge` carries at least one.
`named-by` is `user`, `claude` or `agent`. The title is one line of at most 25 words; the limit only goes down. The
body is prose on what the node is, as long as it needs to be. `## Why` holds the reason in full sentences. The slug is
the file name. Neither the body nor the why may hold a `## Why` or `## Edges` line.

A node that changed is edited in place, and git keeps the old text. A duplicate is forgotten in favour of the other.
Nothing supersedes a node, and no node points at a commit: git holds what happened.

## A node write

A node write carries its subjects and its edges, in the markdown the node file uses.
[`brain add <scope> <slug>`](cli.md#brain-add) and [`brain edit <ref>`](cli.md#brain-edit) read it from stdin or `--file`:

- a frontmatter between `---` lines with `subjects: [a, b]` and no other field, since the command gives the kind, the
  namer, the day and the session;
- then the title line, the body, `## Why` and the why;
- and `## Edges` with one `- rel -> target` line per edge.

```
---
subjects: [ground]
---
# Fine Elwynn ground cover on open ground

Grass first, then moss, lit like the ground.

## Why
Bare ground read as unfinished.

## Edges
- part-of -> wg-12
```

Every edge is checked as [`brain link`](cli.md#brain-link) checks it before the write is judged or staged, and linked
through `brain link`'s own code into the staged write. So it enters the graph only when your yes lands the node, never
before; a no links none.

An edit replaces the node's subjects and edges with the ones it carries, as it writes an omitted part empty. To keep an
edge, the edit carries it, and the window's diff shows every edge that goes.

`brain add <scope> <slug> "<title>"` takes the body, why and subjects from `--body`, `--why` and `--subjects a,b`, and
carries no edge.

In a [merge](cli.md#brain-merge), a new node's `+ scope:slug [a, b]` line carries its subjects and its `+ from -rel-> to`
lines its edges. A new node with no subject line is refused.

## Subjects

A subject is something the nodes have in their keywords; a project is a folder. A subject is a free search keyword in
the node's frontmatter, lowercase and hyphenated, and no list holds them. [`brain lookup --subject S`](cli.md#brain-lookup)
finds the nodes that carry one, and [`brain subjects [scope]`](cli.md#brain-subjects) lists the subjects in use in a scope,
read from its nodes, with how many nodes carry each, so a writer reuses a word in use instead of coining a
near-synonym.

A node written with `add`, `edit` or `merge` needs at least one subject. The free rules refuse one with none before the
judge, and the refusal names `brain subjects <scope>`. The judge reads the subjects as search keywords, with the
subjects in use in the scope, and refuses a subject that does not say what the node is about or would not find it in a
search, saying which and why. The subjects land with the node on your yes. A change of a node's subjects alone carries
no prose, so it meets the free rules and not the judge.

## Edges

An edge is a line `- rel -> target` under a node's `## Edges`. Its targets:

- A bare slug is a node in the same scope.
- `personal:slug`, `global:slug` and `project:slug` cross scopes.
- `<name>:slug` names a node of a project registered in `projects.tsv`. Personal and global nodes use it, never
  `project:`.
- `session:`, `file:` and `url:` are typed refs to things that are not nodes.
- A `commit:` target is refused.

[`brain link`](cli.md#brain-link) adds an edge between two nodes that already exist and lands at once, with no yes.
[`brain unlink`](cli.md#brain-unlink) removes one.

## Weights

An edge is travelled when both its ends are opened with [`brain node`](cli.md#brain-node) in the same Claude Code session
(`CLAUDE_CODE_SESSION_ID`), once per edge per session. `walk`, `lookup` and `link` record nothing. Which nodes a session
opened lives in `cache/trail.sqlite`, derived and never committed.

An edge weighs the sum of `0.5 ^ (days_since / 90)` over its travels. The node's born date counts as one travel. Edges
on a node named by the user count double.

The log of the scope that owns the source node keeps each edge's sum folded: `weight` is the sum as of `asof`, and
today's sum is `weight * 0.5 ^ ((today - asof) / 90)`. Exponential decay makes the fold exact.
[`brain fold`](cli.md#brain-fold) rewrites an old row log, one line per travel, folded, and every weight reads the same after
it. [`brain faded`](cli.md#brain-faded) lists the weakest edges, the candidates to doubt.

Each scope's log is a file in the data repo's gitignored `cache/traversals/`: `global.tsv`, `personal.tsv`, and for a
project its folder name and a hash of its repo path, such as `studio-3f2a9c1b7d4e.tsv`. Reading a graph never changes a
project repo; in the data repo it adds only a line to the usage log. Weights are a cache: a wipe loses them like the
embeddings and the session trail, and every edge starts again from its node's born date.

## The rungs

A reader climbs the graph a rung at a time, never whole. Each rung costs about ten times the one above.

| Rung | What | Tokens | How |
|---|---|---|---|
| 0 | node name | ~10 | [`brain lookup`](cli.md#brain-lookup) |
| 1 | title, why and edges | ~100 | [`brain node`](cli.md#brain-node) |
| 2 | neighbours' titles, edges out and in | ~1k | [`brain walk`](cli.md#brain-walk) |
| 3 | a commit message, a transcript passage | ~10k | `git show --stat`, read `voices/` |
| 4 | the diff, the whole conversation | ~100k | `git show`, the transcript |

The CLI serves rungs 0 to 2. Rungs 3 and 4 are deliberate unwrapping by hand.

## Forgetting

From a session, a forget waits for your yes like any node write. Then [`brain forget A B`](cli.md#brain-forget) deletes the
files of A and B and removes every edge into them from any node of the scopes in effect, `<name>:slug` edges of
personal and global nodes included, so `brain check` stays green. It prints each node and each edge it removed. It
refuses a missing node and a node outside the scopes in effect, and any refusal changes nothing. Forget a project node
from inside its repo. Git keeps the old files.

The traversal log keeps its rows; no edge reads them. Search drops the nodes from its index on its next refresh.

Forget a node that was never a lasting subject, and the one of two duplicates that says less. A node that changed is
edited in place.

## Migrating the old format

[`brain migrate`](cli.md#brain-migrate) moved the old format, a one-line label under `# slug` with `- Name: value` fields, to
this one. It splits a label at its first ", because " or " because ": the left part is the title and the right part the
why. The old `## Notes` became the body. [`brain check`](cli.md#brain-check) fails a file still in the old format.

`brain migrate` also moves a node of the old model to the two kinds. A decision in phase idea becomes an idea, and so
does a concept, lesson or principle. Every other decision, goal, tool, person, phase or project becomes information.
The `phase` and `superseded-by` fields and every `commit:` edge are dropped; title, body and why stay as written.
`brain check` fails an old kind, a phase, a superseded-by and a commit edge, and lists information without a why as a
migration line.

## Related

- [search.md](search.md): how lookup ranks, and the nearest entries every write shows.
- [the-yes.md](the-yes.md): the judge, the pending write and your yes.
- [cli.md](cli.md): every command, its flags and what it refuses.
