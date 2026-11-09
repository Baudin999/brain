---
name: brain
description: >
  The knowledge graph we share: who the user is, how we work, what we decided
  and why, per project. Use it to look something up before answering from
  assumption, to walk what we know about a topic before building in it, and to
  write back what a conversation taught us. Also when the user invokes /brain.
---

# Brain

The graph is plain text in git: the data repo, `BRAIN_HOME`, for the personal and global scopes, `<repo>/docs/brain`
for a project. The `brain` line of `brain context` names the data repo. Everything else is a cache. The session-start
line says which scopes apply here. The project scope is in effect inside that project's repo. From anywhere else,
`brain --project <name|path> <command>` puts it in effect, the name from projects.tsv, e.g.
`brain --project studio lookup worldgen`.

| scope | truth is | holds |
|---|---|---|
| personal | the user | who they are, what they like, how they mean their words |
| global | the world | general information: how we work, tools, models |
| project | the code | functional and technical information, and the ideas we had about it |

## Reading: unwrap on purpose

Each rung costs about ten times the one above. Go down a rung only for a reason you can state.

```
brain lookup <up to 10 words>    rung 0: which nodes exist
brain nearest <ref>              rung 0: the 5 nodes nearest to a node; --scope <scope> < document before it exists
brain node <ref>                 rung 1: a title, its why and its edges, with weights
brain walk <ref> [--hops 2]      rung 2: the neighbours' titles, edges out and in, ~1k tokens
```

Rung 3 and 4 are `git show <hash>` and the user's own words in the data repo's `voices/`. Use them
when rung 2 isn't enough, not by default.

An edge strengthens when both its ends are opened with `brain node` in one session; walking past it does not. An
edge nobody travels fades. `brain faded` lists the faded edges, which are doubt
candidates.

## Writing

- The brain is an information store, not a work log or a project management tool. Git holds what happened and the
  code holds what was built. If you find yourself repeating a commit message, you made a big mistake. A node never
  points at a commit.
- A node is one of two kinds. `information` is what is true now: about the user in personal, general in global,
  functional and technical in a project. An `idea` is a record of thinking together: what we discussed, what we
  rejected, and the reasons. Findings, review items and open work are working state; they belong in a todo or review,
  not in the graph.
- A node is a document, not a line. The title names the subject in at most 25 words: "We do Y", never "The user
  decided that X should be Y". The body says what it is, in prose, as long as it needs to be. Examples are good; a
  log is not. The why gives the reason in full sentences. Information carries a why; an idea needs none. Never invent
  a why: when it is missing, ask the user.
- Never supersede. A node that changed is edited in place, and git keeps the old text. If two nodes say the same,
  forget one in favour of the other. Nine times out of ten the node was not wrong, the writing was.
- The conversation draws the nodes. The user's words carry weight: a node or edge that came from the user's typing is
  `--named-by user`. Name it in their words, not mine.
- Write a node as a document on stdin: `brain add <scope> <slug> --kind <information|idea> --named-by <user|claude|agent> --session $CLAUDE_CODE_SESSION_ID < document`.
  The document is the markdown of a node file, and it carries the node's subjects and edges: `subjects: [a, b]`
  between `---` lines, the keywords a search finds it by, then the title line, the body, `## Why` and the why, and
  `## Edges` with one `- rel -> target` line per edge. `--file <path>` reads it from a file. Before writing, run
  `brain nearest --scope <scope> < document` and give the node an edge to each nearest entry it belongs with.
  A short node can come from arguments instead: `brain add <scope> <slug> "<title>" --subjects a,b --why "<why>" [--body "<body>"]`;
  it carries no edge. The kind defaults to information.

  ```
  ---
  subjects: [ground]
  ---
  # Open ground gets fine cover

  Grass first, then moss.

  ## Why
  Bare ground read as unfinished.

  ## Edges
  - part-of -> wg-12
  ```
- A node that is wrong or out of date gets fixed in place. `brain edit <ref> < document` rewrites its title, body,
  why, subjects and edges together from the same markdown; a part left out is written empty, so carry every subject
  and edge the node keeps: an edge the edit leaves out goes.
  `brain update <ref> ["<new title>"] [--why W] [--body B] [--kind K]` changes one part; name only what changes.
- The node file is markdown: frontmatter between `---` lines with `kind`, `subjects`, `named-by`, `born` and
  `session`, then `# title`, the body, `## Why` and `## Edges`. `subjects` is a list of lowercase-hyphenated words,
  free search keywords that say what the node is about; a node written with `add`, `edit` or `merge` carries at least
  one. `brain subjects [scope]` lists the subjects in use in a scope with node counts: reuse a word in use instead of
  coining a near-synonym. `brain update <ref> --subjects a,b` sets an existing node's subjects without the judge.
  `brain lookup --subject S` lists the nodes that carry S.
- Free rules check every write before the judge and refuse at once: a kind that is not information or idea, information
  without a why, a title that repeats a commit subject of the scope's repo, and a node write with no subject. An edge
  target the write carries is checked as `brain link` checks it. Then Sonnet judges the meaning of title, body and why
  apart, each subject as a search keyword for the node, and the write against its 5 nearest entries: it refuses a write
  that says the same as one, naming it, and one that contradicts one, naming it and the contradiction. A write that
  carries no edge while the judge names edges to its nearest entries is refused with those edges: add them and write
  again. When it refuses a why, ask the user; do not write one yourself. When it refuses a duplicate, edit the node it
  names instead.
- `brain link <from> <rel> <to>` adds an edge to an existing node and lands at once; a new node carries its edges in
  its write. Targets are nodes (`slug`, `personal:slug`) or refs: `session:`, `file:`,
  `url:`. A `commit:`
 target is refused. From a personal or global node, a project node is `<name>:slug`
  with the name from projects.tsv, e.g. `afterlife:wg-19`; `project:` is for project nodes only.
- `brain unlink <from> <rel> <to>` removes a stale or wrong edge; re-point is unlink then link.
- `brain forget <ref> [<ref> ...]` removes a node that was never a lasting subject, or the one of two duplicates that
  says less, with every edge into it.
- `brain check` must pass after writing. Commit the graph with `git -C` on the data repo, a project's docs/brain
  with the project.
- The personal scope is the user's to read and argue with. Never hide a node about them.

## The user's yes

Every node write from a session waits for the user: `add`, `edit`, `update` of a title, body, why or kind, `merge` and
`forget`. The subjects and edges of an `add`, `edit` or `merge` travel inside the write and land only with the yes.
`brain link` and `update --subjects` land at once. The free rules and the judge read the write first, as before. What
passes does not land: the command prints the full text the user will see and a pending id. Under the pending id it
prints the nearest entries of each node it writes. Read them before `brain land`: an entry that says the same is a
likely duplicate, and the user sees the same entries in the window.

1. Run `brain land <id>` alone, as its own command, nothing before or after it, in the background. It puts the write
   before the user in brain's window, which shows it in full: scope, slug, kind, title, body, why, subjects and edges,
   a diff for an edit, each node a forget removes. It returns when the user answers there. Tell the user a write waits
   for them; they open the window with `brain ui`.
2. Yes: it lands exactly that text, and the command says what landed. Commit it as usual.
3. No, with no words: nothing lands and the pending write is gone. Leave it; don't stage it again unasked.
4. No, with words: the command returns the user's words. That is a change. Rewrite the node from their words, in their
   terms, and run the write again. It passes the free rules and the judge again and prints a new id; run
   `brain land <new id>`, so the user sees the new version as a new write and answers yes, no or change again.

`brain land` refuses from anywhere but its own command; it runs in every permission mode. If
it is stopped before the answer, run it again: it waits again, or returns the answer already given. A pending write is
answered once: after a no, stage it again. One that waits a day is swept. If a node changed since its write was
staged, the yes lands nothing and the command says so: read the node again and stage the write again. Pending writes
live in the data repo's gitignored `cache/pending`, never in the graph. Never run `brain serve` or `brain ui` and never
call the window's server: the window and its answers are the user's.

## Merging a block

`brain merge < block` applies a block of `+` lines, all or nothing, as `/brain-seedbrain` drafts it. A node line is
`+ scope:slug | title | kind | named-by | why`. The kind is `information` or `idea`. The why may be left out, but
information needs one; it runs to the end of the line. A line `+ scope:slug [a, b]` sets a node's subjects, its search
keywords; every new node needs one. An edge line is `+ from -rel-> to`; a new node's edge lines are its edges, which the
judge reads with it and which land with it. A merge creates nodes and skips one that exists. A line holds no body, so a
node that needs one gets it with `brain edit` after the merge, the document carrying the node's subjects and edges. A
merge applies all or nothing: one failing line applies none, so fix the block and merge it again. The whole block is one
pending write: the user sees every node in it as one write and lands all or none.

```
nodes:
+ project:meadow | Open ground gets fine cover | information | agent | Bare ground read as unfinished.
+ project:living-ground | Ground that grows back where players walk | idea | agent
subjects:
+ project:meadow [ground]
+ project:living-ground [ground]
edges:
+ project:meadow -part-of-> project:wg-12
+ project:meadow -defined-in-> file:docs/world-generation.md
```
