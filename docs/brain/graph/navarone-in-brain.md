---
kind: idea
subjects: [navarone, workspace, code-review, lens]
named-by: claude
born: 2026-11-03
session: 8b3c6b8a-f8f5-420c-b0ba-2a4ce70a1f71
---
# Navarone lives in brain code's workspace: the user reads its problems in the code and judges where builder and validator disagree

Navarone reviews code. Its signals mark places to look, and the builder answers each one: it fixes the code or records a reason. The validator then keeps or replaces each answer. A clash is the two disagreeing, and only clashes need the user. His judgement is final: keep, where the builder is right, or replace, where the validator is right and the builder fixes. Words are needed only when he wants something neither side proposed. The original wish, in the user's words: "I see the code before the fix, the fix, the reasoning from both sides and I judge!"

Clashes are seen and resolved through the workspace, inside brain code, and no longer in brain ui. The workspace's tree shows the problems in the codebase, so the user sees where the code is unhealthy before he opens a file. In the user's words, resolution and understanding are at the root of both Navarone and the workspace.

What leads is what is wrong with the code, never the metric. In the user's words: "I do not care about the metrics like that 10 for cyclomatic complexity. I care about beautiful code which remains easy to change." A clash's title is the line of his taste it breaks, and under it one sentence says what is wrong in this code. The metric, ids, worktree, hashes and tags sit behind one details fold.

## The bench

A clash opens as a bench: one clash, one screen, read top to bottom. The problem first; then the code before and the builder's fix side by side, as real readable code with only the lines that matter lit; then the builder's and the validator's arguments as two voices, each in full; then the choices. The user's words on the screen it replaced: "Too much clutter, it does not show what the problem is, it lists data. Imagine being a human and having to arbiter between two bickering agents. I want to see the code, I want to see the reasoning."

## The lens reads a method as a flow

The lens is how the workspace makes a method understood, and how the bench digs into a clash. The user wants to see "caller, callee and class" together. IDEs have no such view: they show one file and make you rebuild the context in your head. The lens lays the context out around the method at once.

- The subject sits in the middle: its full code, plain, with Navarone's problems marked in the margin at their lines, each saying the line of taste it breaks and the one sentence on what is wrong.
- Its callers come in from the left: a card per call site, under the calling method's name, the calling line lit with a few lines around it.
- What it calls goes out to the right: a card per method in the codebase it calls, in call order, its signature first and its code folded until opened.
- Its class sits above as a thin band: the type's name and its members as a compact outline, the subject's row lit, and the fields and properties the subject uses lit too. The band folds.

Each call in the subject's code is a mark. With the cursor or the mouse on it, its card lights up and a thin line runs from the call to the card, so the eye follows the call; a caller's lit line links to the subject's signature the same way. A card whose method has problems carries their count. A click or Enter on a card makes it the subject, and the columns slide in the direction of travel, so the method you left stays in view on its side. A trail on top names the methods walked, `u` or Backspace steps back, `h` and `l` move between the columns, `j` and `k` within one, `c` folds the class band, and Esc closes the lens where it opened.

It opens on any method in the workspace's reader, on a problem in the tree, and from a clash's bench with the clash's lines lit. Its context is `navarone lens --json`, run in the folder or the clash's worktree. Navarone decides staleness from its fingerprint of the judged code, not from HEAD, because HEAD moves with every commit. The lens builds one model and renders it twice, as markdown for agents and terminals and as JSON for the window, so the two never drift apart.

While Navarone's config holds landing until the user judges, the workspace is on the critical path of a landing.

## Where we disagreed

Navarone wanted richer blocks for split methods; brain held that captioned code blocks are enough. The first lens was a modal with one view at a time, picked by buttons down its left side; it gave way to the flow, which shows caller, callee and class together and still shows each piece of code once. A three-pane layout with the code beside the lens was dropped because it showed the code twice, and the lens as the clash's permanent left pane because it rammed all data onto the screen instead of helping the user judge.

## Why
The user arbitrates between two agents, and he understands code by its structure, not its keywords. He needs to see the problem, the code and both arguments at a glance and decide, and to dig deeper only when he chooses to; a screen that lists data makes him do the work of finding the problem. Putting Navarone in the workspace puts the judgement next to the code it is about.

## Edges
- refines -> tool-disputes
- part-of -> brain-workspace
- relates-to -> workspace-reading
- relates-to -> brain-code-apart-from-ui
