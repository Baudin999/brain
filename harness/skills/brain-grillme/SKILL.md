---
name: brain-grillme
description: >
  Interview the user until we share one understanding of a topic: a seed draft,
  a plan, an idea or a design. Use after /brain-seedbrain merges a draft, before a
  plan goes to a closed agent, when information in the graph has no why, or when the
  user invokes /brain-grillme. One question at a time, each with my own guess.
argument-hint: "[topic]"
---

# Grill me

What I drafted from docs is my reading of the docs, one step from what the user meant. So a draft is a hypothesis, and
the user is the one who confirms it. Seeding is not static: if it were, I would never make mistakes.

This is not devil's advocate. The user is the source of truth here, not an opponent. I ask to learn, not to win.

## Find the weak spots first

Walk the topic before the first question: `brain walk <topic> --hops 2`, `brain node` on what looks thin, and the draft
or plan itself. List where the understanding is weakest. Ask there, never at random. The kinds:

- Information with no why. "The pallet exists so a world gets its own look. Is that the reason, or is there a deeper one?"
- A contradiction between sources. "The doc says the template is used once, wg-14 regenerates from it. Which is true
  now?"
- Something that died quietly. "wg-9 folded with no lesson. What did it teach?"
- A guess about weight. "I'd say funnels are central. Are they, or a detail?"
- A hidden assumption in a plan. "This assumes one server per world. Does it?"

## Ask

- One question at a time, in prose. Never the `AskUserQuestion` tool. Then stop and wait for the answer.
- Each question carries my own guess or recommendation and its reason, so the user corrects instead of writing from
  scratch.
- Walk the tree branch by branch. Finish a branch before the next, and let an answer open a branch I hadn't listed.
- Take the answer as given. If it contradicts a source, say which source once and ask whether to rewrite it.

## The answers become the graph

Write after each answer, not at the end.

- A confirmed node gets walked, `brain walk <node>`, or an edge to what the answer tied it to.
- A why the user gives becomes the node's why: `brain update <node> --why "<their words>"`.
- A correction rewrites the node: `brain update` or `brain edit` for its title, body and why, `link` and `unlink` for
  its edges. An edit carries the node's subjects and edges too, and drops what it leaves out.
- A new idea becomes a node, written as a document with its subjects and edges: `brain add <scope> <slug> --kind idea --named-by user < document`.
- Everything that came from the user's answer is `--named-by user`, so it outweighs the draft. A node I rewrite from
  the answer becomes `named-by: user`. Quote the user's words in a title or a why where they carry the meaning.
- `brain check` passes before the next question.

## Ending

It ends when I can explain the topic back in five lines and the user doesn't correct them. Those five lines become the
topic's top node: a one-line title, and the five lines as its body, `named-by user`.

The user can stop it at any time. Then record what was settled, and list in a few lines what stayed open. Nodes nobody
asked about stay as they were.
