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

## The lens is four panels that slide vertically

The lens is how the workspace makes a method understood, and how the bench digs into a clash. The user wants to see "caller, callee and class" together. IDEs have no such view: they show one file and make you rebuild the context in your head. The lens lays it out as four panels stacked top to bottom: the callers, the class with the method lit, the callees, and Navarone's findings.

Showing all the code takes space, so only the panel in focus shows its code in full. The closed panels show cards: the assembly, the namespace, the class name and the method name. A card unfolds with a satisfying animation when its panel is selected. A panel can be pinned to stay unfolded, for larger screens.

- The class panel is the whole class, with the method in focus in the centre. There is no traditional cursor, because the workspace is not made for editing; the focus is the method. The panel says what the class's public API is and how the method fits into it. When the class has LCOM problems, a colour-coded explainer shows its groupings: the methods and properties of one grouping share one colour.
- The callers panel has a card per call site, under the calling method's name, the calling line lit.
- The callees panel has a card per method in the codebase the method calls, in call order.
- The findings panel has Navarone's problems on the method, each saying the line of taste it breaks and the one sentence on what is wrong.

Opening a file shows its class view at once. Selecting a method fills the other panels, cached. Walking the graph matters: selecting a caller or a callee makes it the method in focus, a trail on top names the methods walked, and a step back returns along it.

It also opens on a problem in the tree and from a clash's bench with the clash's lines lit. Its context comes from `navarone lens --json`, run in the folder or the clash's worktree. Navarone decides staleness from its fingerprint of the judged code, not from HEAD, because HEAD moves with every commit. The lens builds one model and renders it twice, as markdown for agents and terminals and as JSON for the window, so the two never drift apart.

Review and annotation come later, on top of the lens, so that the system learns from what the user says about the code.

While Navarone's config holds landing until the user judges, the workspace is on the critical path of a landing.

## Where we disagreed

Navarone wanted richer blocks for split methods; brain held that captioned code blocks are enough. The first lens was a modal with one view at a time, picked by buttons down its left side; it gave way to a flow with callers on the left, callees on the right and the class as a thin outline band above. The flow gave way in turn to four vertical panels, because the method is understood inside its whole class, not beside an outline of it. A three-pane layout with the code beside the lens was dropped because it showed the code twice, and the lens as the clash's permanent left pane because it rammed all data onto the screen instead of helping the user judge.

## Why
The user arbitrates between two agents, and he understands code by its structure, not its keywords. He needs to see the problem, the code and both arguments at a glance and decide, and to dig deeper only when he chooses to; a screen that lists data makes him do the work of finding the problem. Putting Navarone in the workspace puts the judgement next to the code it is about.

## Edges
- refines -> tool-disputes
- part-of -> brain-workspace
- relates-to -> workspace-reading
- relates-to -> brain-code-apart-from-ui
