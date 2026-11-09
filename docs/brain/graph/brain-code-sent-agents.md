---
kind: information
subjects: [brain-code, agents, window]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# A brain code session lists every agent it sent, and each opens in a view made for its kind

Beside its conversation, a session shows the agents it sent: the kind, what it was sent to do, whether it runs, finished, was stopped by the user or failed, and how long it ran. A new agent marks its session's row without taking the view.

The side panel is our view into how it ran, its timeline and evidence, and a way to click on an agent and see what it is doing. By default it draws a flow, blocks with lines between them. It always starts with the open agent, which can spawn several builders, and every agent it sent hangs from it. A readiness check is attached to the agent it checked. A builder and the validators that check its work are one chain, visibly apart from every other chain, and the lines between builder and validator show which way information went: the report to the validator, each round of findings back to the builder, each answer forward. A chain ends in an end node showing that it landed, with the hash, or says where it stands. The worktree the builder opened shows where it was opened and where it was deleted. Every process trail we know is on the drawing, and the panel takes the room the flow needs: it does not need to be small, it needs to be amazing. In the settings the user chooses the visualization: the flow or a plain list.

Each agent opens read only, live while it runs, in a view made for its kind, with its whole conversation one switch away. A readiness check shows its verdict, READY or NOT READY, and its questions. A builder and its validator show the trail of their work, and a click on a line in the panel opens it there.

## Why
The user dislikes being unorganized and likes to be in control. He wants to see when validators spin up, what they find, and what the builder agents do, instead of agents working out of sight. A long line of agents does not show which ones belong together, what their chain is or what the evidence chain is; a flow does. A view per kind shows first what matters for that kind, and the whole conversation stays one switch away for when he wants it.

## Edges
- part-of -> brain-code
- respects -> agents-never-steer-the-window
