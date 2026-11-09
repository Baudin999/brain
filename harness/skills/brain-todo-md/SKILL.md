---
name: brain-todo-md
description: >
  The house format for a repo's todo.md: a multi-commit plan whose phases are
  each sized to one commit, and which folds every finished phase down to a
  single line carrying the commit hash. Use when writing or restructuring a
  todo.md, plan.md, or any checked-in plan for work spanning several commits;
  when the user says "implement todo.md phase N" and the phase then needs
  folding; when a plan file has grown into a log of what already happened; and
  whenever a refactor is too big for one commit and needs sequencing. Governs
  the plan file only, never the code it plans.
---

# todo.md

A `todo.md` is a plan, not a log. It holds what has not happened yet. The
moment a phase lands, its checklist stops being a plan and becomes a worse copy
of the commit that delivered it, so it gets folded to one line and the hash
carries the rest.

That fold is the whole discipline. Everything below is a consequence of it.

## Why folding, and not a changelog

`git show <hash>` reproduces every file the phase touched, in full, forever. A
checklist left behind reproduces a summary of an intention, and it starts
drifting the first time anyone edits the code it described. Two records of the
same event where one is authoritative and one decays is not thoroughness, it is
a maintenance burden with a countdown on it.

So the finished phase keeps exactly the thing git cannot give you: a name, one
sentence of intent, and the hash. Everything else is recoverable, and recovering
it is one command.

The test for whether a line belongs in the file: **can I get this back from
`git show`?** If yes, fold it. If no, it stays.

## The file

```markdown
# <What this plan does>

<Two or three sentences. What is being cut, built, or moved, and why.>

## How this file works

<The fold rule, stated for whoever opens the file next.>

## Done

- [x] **1. <Phase name>.** <One sentence of what landed.> `<hash>`
- [x] **2. <Phase name>.** <One sentence of what landed.> `<hash>`

## <Reference the remaining phases need>

<Optional. A table of surviving crates, a target layout, an invariant. Keep it
only while a later phase still refers to it.>

## 3. <Next phase>

<Why this phase exists and what makes it safe to do now.>

- [ ] <A concrete edit, naming the file.>
- [ ] <Another.>
- [ ] `<the gate command>` green before committing.
```

## Writing a phase

**Size it to one commit.** A phase that cannot land as one commit with the gates
green is two phases. This is the constraint that makes everything else work, and
it is the one most likely to be violated by a plan written in a hurry.

**Order so no intermediate state is invalid.** Some work resists splitting
because the contract has to move with the code. Deleting a crate and removing
its declared dependency edge is one commit, not two, because the state in
between fails the gate. When a phase is large for that reason, say so in the
phase, in one line. The next person will otherwise try to split it and lose an
hour finding out why they cannot.

**Name files, not intentions.** "Repoint the stale fixture in
`cli/test-runner/src/cli.rs:189`" is an item. "Clean up test fixtures" is a
wish. If you cannot name the file, the phase is not planned yet, and writing
that down honestly is better than writing a placeholder that reads as planned.

**End every phase with its gate.** The last item is the command that proves the
phase, named exactly as it is run. A phase with no gate line has no definition
of done.

**Reference paths as code spans, not links.** A repo with a dead-link check
scans the plan file too, and a Markdown link to a file the plan deletes fails
the gate the moment the phase lands. Backticks are never checked.

## Folding a finished phase

When the phase is committed:

1. Delete the whole section, checklist and prose.
2. Add one line under `## Done`: the number and name in bold, one sentence
   naming what actually landed, then the short hash in backticks.
3. Delete any reference section that only that phase used.
4. Leave the remaining phase numbers alone.

Numbers are stable identifiers, not positions. Renumbering after a fold breaks
every reference to "phase 4" in commit messages, in other docs, and in whatever
the user said an hour ago. Gaps in the sequence are correct and cost nothing.

Write the Done line from what the commit did, not from what the checklist said.
Those differ more often than they should, and the commit is the one that
happened.

## What the work teaches goes into the plan, not the Done line

Doing a phase almost always changes a later one. A threshold turns out to cite
deleted code; a doc turns out to have a sibling in the same state; the shape of
the fix is different from what the plan assumed.

Put that in the phase it affects. Do not put it in the Done line, where it reads
as history and gets folded away, and do not leave it only in the commit message,
where nobody reading the plan will look.

## Loose ends get their own phase

Some work no gate will ever report. A stale binary in `~/.cargo/bin`, an
unstaged deletion sitting in the working tree, a config on a machine rather than
in the repo. These are exactly the items that evaporate, because a green test
run says nothing about them and nothing else will ever raise them again.

Give them a phase. Say in one line why no gate covers them, which is also the
argument for why they need writing down.

## The file shrinks

Over the life of a plan the file gets shorter. If it is getting longer, one of
two things is happening, and they need different responses.

You are discovering work. Fine, and expected. Add the phase, keep it sized to
one commit, and note what turned it up.

You are logging. Stop. The Done section is names and hashes. A Done line that
grew a sub-list, a rationale, or a "note that we also..." is a checklist that
was not folded, only reformatted.

## Relationship to the session todo list

They are different tools and both are worth having open. The in-session list is
scratch for the current turn and dies with the session. `todo.md` is the
durable plan, survives across sessions and machines, and is the thing a
teammate or a future session reads to find out where the work stopped.

Do not mirror one into the other. Read `todo.md` to pick the phase, then track
the steps of executing it however you normally would.
