---
kind: information
subjects: [brain-code, window, agents]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code is its own window, apart from brain ui: it is where developers work with agents

brain code has its own window and server, apart from brain's window and every workspace's, and it is never pushed into brain ui. Each window has its own use and its own audience:

- brain ui is there for alerts and escalations, used by product owners and senior developers.
- brain workspaces give technical insight into a project, used by developers.
- brain code is how developers interact with agents for development, asking them to actually do the work.
- Navarone is used by senior developers and architects.

A sitting in brain ui is not the same as a brain code session. The questions an agent asks and the commands it hands over belong in brain code, as does the open agent asking for input before it sends a builder: that never goes through brain ui.

## Why
In the user's words, this is asking an agent to actually do the work, which is very different from an alert or an escalation. Mixing the two would put a developer's working flow into a window meant for people who judge and approve, and serve neither well. He could be convinced to move Navarone's clashes out of brain ui into brain code, but not to push brain code into brain ui.

## Edges
- part-of -> brain-code
- relates-to -> sitting-is-the-open-agent
- relates-to -> brain-workspace
- relates-to -> navarone-in-brain
