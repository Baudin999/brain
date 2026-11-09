---
kind: information
subjects: [brain-code, agents, window]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# A brain code session lists every agent it sent, and each opens in a view made for its kind

Beside its conversation, a session lists the agents it sent, in the order sent: the kind, what it was sent to do, whether it runs, finished, was stopped by the user or failed, and how long it ran. A builder and the validators that check its work show together as one piece of work. A new agent marks its session's row without taking the view.

Each agent opens read only, live while it runs, in a view made for its kind, with its whole conversation one switch away. A readiness check shows its verdict, READY or NOT READY, and its questions. A builder and its validator show the trail of their work.

## Why
The user dislikes being unorganized and likes to be in control. He wants to see when validators spin up, what they find, and what the builder agents do, instead of agents working out of sight. A view per kind shows first what matters for that kind, and the whole conversation stays one switch away for when he wants it.

## Edges
- part-of -> brain-code
- respects -> agents-never-steer-the-window
