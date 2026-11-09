---
kind: idea
subjects: [brain-code, brain-execute, terminal]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain execute: a command the agent gives the user is clickable in brain code and runs in a modal terminal

Sometimes the agent gives the user a command to run. With `brain execute`, the agent outputs that command as JSON, as `brain ask` does with questions, so the UI can parse it and make it clickable. A click runs it in a modal terminal. A chain of commands runs one at a time. When a command fails, its error is pasted directly into the chat, so the agent sees it without the user copying it over.

We weighed `brain terminal` as the name, since it is more on the nose, and chose `brain execute`. Interaction with a normal terminal should feel much smoother than copying commands out of the conversation and errors back into it.

## Edges
- part-of -> brain-code
- relates-to -> brain-ask
