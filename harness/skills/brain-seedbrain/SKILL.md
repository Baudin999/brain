---
name: brain-seedbrain
description: Work an existing project's history into its project graph, one area per run. Called only by the user, as /brain-seedbrain, from inside the project.
argument-hint: "[area]"
disable-model-invocation: true
---

# Seed brain

A project that predates the graph already holds its past: intent in its docs, plans in its todo files, what landed in
its changelog, why in its decision records. This skill turns what is still true of that past, and the thinking behind
it, into nodes and edges in `<repo>/docs/brain/graph`. Git keeps the rest. The open agent runs it and sends the closed agents; a closed agent cannot send one of its own.

A seed drafted from docs is my reading of the docs, one step from what the user meant. So a seed is a hypothesis: a
closed agent drafts it, the open agent merges it, and the open agent grills the user on it with `brain-grillme` when the draft has weak spots
worth the user's time. Seeding is not static. If it were, I would never make mistakes.

Seeded nodes are a guess about what matters. They are `named-by agent`, start weaker than what the user names, and grow
only by being walked. Nodes nobody asked about stay `named-by agent`, weak until walked.

## What the graph takes

- Nodes of two kinds. `information` is what is true of the project now, functional or technical, and carries its why
  in the fifth column, in the docs' own words. An `idea` is thinking the docs record: what was considered, what was
  rejected, and the reasons. It needs no why. When the docs give no why for something true, it stays an idea; never
  invent one.
- Never the code's structure: the code holds that. Never a commit trail: a changelog entry, a landed todo phase or a
  commit is history, and git holds it. What it taught that is still true becomes information; no node points at a
  commit.
- Never a copy of a list.
- A node is a document. Titles in the docs' own words, at most 25 words. When the docs say more than a title, the node
  gets a body in their words: what it is, and an example where one helps.
- Every node carries at least one subject, a search keyword for what it is about, in a `+ project:slug [a, b]` line,
  reusing a word `brain subjects project` lists as in use, and its edges to what it belongs with. A merge refuses a
  new node without a subject line, and the judge refuses one with no edge when it names a nearest entry it belongs
  with.

## 1. List the areas

If `docs/brain` is missing, run `brain init` first: it lays out `docs/brain` and registers the project.

Read the project's CLAUDE.md doc index, the `docs/*.md` names and the `todo-*.md` and `handoff-*.md` files in the root.
Group them into areas, one per topic the index names: for afterlife, world generation, water, abilities, combat,
creatures, characters, server, visuals, the protocols. Read `docs/brain/seeded.tsv` for the watermark.

Show the list: one line per area, with why it is one, its files, and `seeded <hash> <date>` or `new`. If the user
named an area, skip to step 2. Otherwise ask which to seed and wait. One area per run unless the user names more.

## 2. Gather the area's past

- Its files: the docs, the todo and handoff files.
- Its changelog entries: those whose hash appears in its todo file, and those whose lines name the area.
- On a later run of a seeded area, only what moved since the watermark: `git log --oneline <commit>..HEAD -- <files>`
  and the changelog entries above the watermark's date.
- What the graph already holds: `brain lookup` on the area's words, `brain node` on each hit. These are the nodes to
  link to, not to duplicate.

## 3. Send one closed agent

One fresh `general-purpose` agent. It reads and returns a block; it edits nothing, builds nothing and reads no code.
Its prompt carries:

- the area, and why the open agent is seeding it;
- the exact files to read and the changelog hashes;
- the existing nodes, as `project:slug | title`, to link to instead of making again;
- the rules of "What the graph takes" above;
- the report: the block below, at most 40 lines, every node `named-by agent`, every slug lowercase and hyphenated, a
  todo phase's slug the todo file's own id where it has one; then, for each node the docs say more about, its body as a
  document under a `body: project:slug` line, at most 2k tokens in all;
- the subjects in use in the project, from `brain subjects project`, and that every node needs a subject line whose
  words say what it is about, reusing a word in use over a near-synonym;
- that an edge may name only a node the graph holds or the block adds, and never a commit: `brain merge` refuses both.

```
nodes:
+ project:world-generation | Worlds people want to play, built from a plan of named places | information | agent | A world nobody wants to explore is not worth generating.
+ project:wg-27 | testing-default is the unchanging fixture | information | agent | A test needs a world that never moves.
+ project:wg-13 | Bases become built places | idea | agent
subjects:
+ project:world-generation [worldgen]
+ project:wg-27 [worldgen, testing]
+ project:wg-13 [worldgen]
edges:
+ project:wg-27 -part-of-> project:world-generation
+ project:wg-27 -taught-> project:wg-13
+ project:world-generation -defined-in-> file:docs/world-generation.md

```

```
body: project:wg-27
---
subjects: [worldgen, testing]
---
# testing-default is the unchanging fixture

Every worldgen test loads testing-default. Nothing regenerates it, so a test that fails points at the code, not at
the world.

## Why
A test needs a world that never moves.

## Edges
- part-of -> world-generation
- taught -> wg-13
```

## 4. Merge the draft

From inside the project:

1. `brain merge` the block. It applies all or nothing: when a line fails, fix or drop it in the block and merge again.
2. Write each body: `brain edit project:<slug> < document`, the document without its `body:` line. The title, the
   why, the subjects and the edges in the document match the block's: an edit replaces the node's subjects and edges
   with the ones it carries.
3. `brain check` exits 0.
4. Show the user the new nodes, a few lines: slug and title.

## 5. Grill the user when the draft needs it

Read the draft for weak spots worth the user's time: something true left an idea for want of a why, sources that
disagree, a todo phase that folded with no lesson, a guess about weight that matters. Tell the user the decision and
its reason in one line either way.

- Weak spots found: load `brain-grillme` with the area as its topic and aim at them. One question at a time, and wait. The
  answers rewrite the draft `--named-by user`. It ends with the area's five-line top node, or when the user stops it;
  then list what stayed open.
- None found, as on a small incremental run: skip the grilling. The new nodes from step 4 stand as the summary. Say I
  can grill on request.

## 6. Check and commit

1. `brain check` exits 0.
2. Append or replace the area's line in `docs/brain/seeded.tsv`: `area<TAB><HEAD short hash><TAB><date>`.
3. `git -C <repo> add docs/brain`, then, as a separate command, `git -C <repo> commit -m "Brain: seed <area>"`. Only
   `docs/brain/` may be staged. If the user has anything else staged, the hook refuses: say so and stop.
