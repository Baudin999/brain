---
kind: information
subjects: [brain-code, conversations, resume]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code lifts a session that runs in a terminal into its window, by the session's id

`brain code [agent] --resume <session-id>` moves a Claude Code session that runs elsewhere, as in a terminal, into the window. It continues by its id, in the folder it ran in, with its history drawn. A folder argument that differs from the session's folder is refused. The user closes the terminal's copy himself.

## Why
The user wants to lift and shift a whole conversation into the window, so moving to brain code never costs the work a terminal session already holds. It keeps the folder the session ran in because the path is really important.

## Edges
- part-of -> brain-code
- relates-to -> brain-code-agent-and-folder
