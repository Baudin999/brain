---
kind: idea
subjects: [navarone, window, code-review, lens]
named-by: claude
born: 2026-11-03
session: 8b3c6b8a-f8f5-420c-b0ba-2a4ce70a1f71
---
# Navarone is the first tool brain's window hosts: the user judges where its builder and validator disagree

Navarone reviews code. Its signals mark places to look, and the builder answers each one: it fixes the code or records a reason. The validator then keeps or replaces each answer. A clash is the two disagreeing, and only clashes reach the user, in brain's window. His judgement is final: keep, where the builder is right, or replace, where the validator is right and the builder fixes. Words are needed only when he wants something neither side proposed. The original wish, in the user's words: "I see the code before the fix, the fix, the reasoning from both sides and I judge!"

What leads is what is wrong with the code, never the metric. In the user's words: "I do not care about the metrics like that 10 for cyclomatic complexity. I care about beautiful code which remains easy to change." A clash's title is the line of his taste it breaks, and under it one sentence says what is wrong in this code. The metric, ids, worktree, hashes and tags sit behind one details fold.

## The bench

A clash opens as a bench: one clash, one screen, read top to bottom. The problem first; then the code before and the builder's fix side by side, as real readable code with only the lines that matter lit; then the builder's and the validator's arguments as two voices, each in full; then the choices. The user's words on the screen it replaces: "Too much clutter, it does not show what the problem is, it lists data. Imagine being a human and having to arbiter between two bickering agents. I want to see the code, I want to see the reasoning."

## The lens is a modal for a deep dive

"Reading code is not about pushing it into a markdown block, reading code is about understanding." The lens is a modal the user opens on top of the bench, for a deep dive into that piece of code, and Esc returns him to the bench where he left it. Down its left side are simple buttons, one per lens view: show class, show callers, show calls. The code sits in the middle; a call in it is a mark, and clicking it makes that method the subject, with back and a breadcrumb to return. The lens always has an agent to talk with, as Alt-A gives one in the window, and it knows the clash, both arguments and the code on screen.

The context is `navarone lens --json`, run in the clash's repo. Navarone decides staleness from its fingerprint of the judged code, not from HEAD, because HEAD moves with every commit. The lens builds one model and renders it twice, as markdown for agents and terminals and as JSON for the window, so the two never drift apart.

While Navarone's config holds landing until the user judges, the window is on the critical path of a landing.

## Where we disagreed

Navarone wanted richer blocks for split methods; brain held that captioned code blocks are enough. A three-pane layout with code beside the lens was dropped because it showed the code twice. The lens as the clash's permanent left pane was dropped too: it rammed all data onto the screen instead of helping the user judge.

## Why
The user arbitrates between two agents. He needs to see the problem, the code and both arguments at a glance and decide, and to dig deeper only when he chooses to; a screen that lists data makes him do the work of finding the problem.

## Edges
- refines -> tool-disputes
