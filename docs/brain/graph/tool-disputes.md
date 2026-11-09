---
kind: idea
subjects: [window, review]
named-by: claude
born: 2026-11-01
session: 24411209-7a20-477e-bd6c-d4c2af8eb124
---
# Brain's window can hold other tools' questions for the user to judge, each tool in its own inbox beside node review

A tool that needs the user's judgement can ask for it in brain's window. The question stays in the tool's own state, and brain never copies it into its pending store. Brain is only the interface, and the tool works fine without it.

## Many sources, filtered on three axes

Several sources are coming: a code-review tool's disputes, email drafts to send or not, a builder's or validator's design question that waits for the user, and a feature shown as a screenshot for the user to comment on, since the user wants to judge features from an image. The window has to stay clear with five sources at once, so it filters on three axes, and brain knows the meaning of none of them.

- Source. The window has one inbox per source, each with its count, in a thin strip down the window's left edge, their labels rotated -90 degrees, like VS Code's activity bar. Node review is the first inbox. The window opens on the tab the user last opened, and on Home when it remembers none; nothing arriving switches the tab. Home lists along its bottom the projects with something waiting. Items from different sources never mix in one queue: node review is a quick yes or no, and a tool's question can be a sitting that takes time. A source that has nothing waiting shows no count.
- Project. One project focus for the whole window: personal, global, a project from projects.tsv, or all. Focused on one project, every inbox and every count shows only that project's work, almost distraction free. The focus sits at the top of the queue. It and Alt+P open a scrolling list with one row per graph: its name, its path on disk, and its node reviews and disputes waiting, searched fuzzily. The focus survives a reload.
- Tags. A source puts a few short tags on each item. The window shows them as small chips and filters on them within that inbox. A tag is the source's text: brain shows it and filters on it, nothing more.

## What brain renders

A tool joins the window through its own install script, which the user runs: in the user's words, "you should have an install script and that should add your functionality to the sources.tsv". The install adds the tool's line to brain's sources.tsv when brain is installed and the line is missing. No agent may write that file, because brain runs its commands as the user, and a command an agent chose could write the user's answers itself.

Brain owns the rendering, and a tool never draws inside the window. A tool lists its open questions as read-only JSON, and an agent may run that command because it changes nothing. A question is a document of generic blocks: text, code with path, lines, highlights and a caption, a diff that can span several files, a list of claims each with evidence and whether it holds, and the choices with optional words. A screenshot source would add an image block. The caption is the tool's text, so brain never learns what the code means. The answer goes into brain's own guarded store, and the tool pulls it on its next run. Before storing it, the tool drops an answer whose subject changed after the user answered.

## A question, and its context

A question opens on its own, one question on one screen, in the order of the blocks the source sent; navarone-in-brain holds how that screen reads. A source can give a question a `group`, a `commit`, a `stale` flag, a `context` query and `focus` lines. The context is what the source's context command, the third column of sources.tsv, prints for the query, as brain's own blocks, and it opens over the question, never beside it. Brain learns no tool's query language: the queries and the views to offer come from the source. Brain never compares hashes; the source says when a question is stale. The window runs the command in the question's repo, with the words as separate arguments that no shell parses.

## What we rejected

A separate window per tool was rejected, because a second window is a second surface the user has to trust. A tool rendering its own HTML in brain was rejected for the same reason: the user could no longer tell the window is brain's.

## Edges
- refined-by -> navarone-in-brain
- respects -> user-says-yes
