---
kind: information
subjects: [brain-code, window, agents, workspace, navarone]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code is its own window, apart from brain ui, and it holds the workspaces and Navarone

brain code has its own window and server, apart from brain's window, and it is never pushed into brain ui. In the user's words: "I do not want a 3e UI application". So there are two windows, each with its own use and its own audience:

- brain ui is there for alerts and node review, used by product owners and senior developers.
- brain code is how developers work with agents, asking them to actually do the work, and where they read the code those agents work in. The workspace and Navarone are folded into it.

The workspace keeps its own server for its folder; brain code's window is only the aggregation that shows it. A session's conversation and its folder's workspace are tabs in the same strip brain ui uses, and a side by side view, which a toggle turns on and off, shows the two at once. Navarone's clashes are seen and judged through the workspace, not in brain ui.

A sitting in brain ui is not the same as a brain code session. The questions an agent asks and the commands it hands over belong in brain code, as does the open agent asking for input before it sends a builder: that never goes through brain ui.

## Why
Asking an agent to do the work is very different from an alert or a node to approve, and mixing the two would serve neither. A third window for the workspace would split one working flow over two surfaces: when a session runs in a folder, the user wants to see the code itself right beside it. Resolution and understanding are at the root of both Navarone and the workspace, so they belong in one place.

## Edges
- part-of -> brain-code
- relates-to -> sitting-is-the-open-agent
- relates-to -> brain-workspace
- relates-to -> navarone-in-brain
