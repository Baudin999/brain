---
kind: information
subjects: [brain-code, agents, window]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# A brain code session lists every agent it sent, and each opens in a view made for its kind

Beside its conversation, a session lists the agents it sent, in the order sent: the kind, what it was sent to do, whether it runs, finished, was stopped by the user or failed, and how long it ran. A new agent marks its session's row without taking the view.

The list reads like a trail. A readiness check is attached to the agent it checked. A builder and the validators that check its work show as one piece of work, joined top to bottom in time, and what passed between them reads as its own step: the builder's report, each time the validator sends findings back to the builder, the builder's answer, and the landing. The side panel is our view into how it ran, its timeline and evidence, and a way to click on an agent and see what it is doing: a running agent's row says what it does now.

Each agent opens read only, live while it runs, in a view made for its kind, with its whole conversation one switch away. A readiness check shows its verdict, READY or NOT READY, and its questions. A builder and its validator show the trail of their work, and a click on a step in the list opens it there.

## Why
The user dislikes being unorganized and likes to be in control. He wants to see when validators spin up, what they find, and what the builder agents do, instead of agents working out of sight. A view per kind shows first what matters for that kind, and the whole conversation stays one switch away for when he wants it.

## Edges
- part-of -> brain-code
- respects -> agents-never-steer-the-window
