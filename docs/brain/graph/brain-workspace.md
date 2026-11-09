---
kind: idea
subjects: [workspace, brain-code, navarone]
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# The workspace is a session's folder in brain code, tailored to how the user reads and understands code

Every brain code session runs in a folder, and its workspace is that folder's code, shown in brain code's window. It is not a window of its own: in the user's words, "I do not want a 3e UI application". The workspace keeps its own server over its folder, and brain code's window is only the aggregation that shows it. The session's conversation and its workspace are tabs in the same strip brain ui uses, and a side by side view, turned on and off with a toggle, shows the conversation and the code at once. `brain workspace <path>` and its own window go.

The user used Zed only to watch a project's tree, click through it and read files. The workspace keeps that part and makes it amazing:

- exploring the file structure, with the codebase's problems shown in the tree
- reading files: code and markdown
- understanding a method through Navarone's lens: its callers, what it calls and its class
- resolving Navarone's clashes, next to the code they are about
- writing markdown. The user no longer writes code, so the workspace doesn't type code.
- switching worktrees, very fast and very easily
- showing git: what agents are working on, which worktrees can be cleaned up, pushing to remote

Out of scope: sign-in, collaboration and typing code.

Resolution and understanding are at the root of both the workspace and Navarone. Questions about what the user reads go to the session's own agent, right beside it. There is no separate AI inside the workspace.

This node is the thesis. Each feature has its own node, because features change and the thesis stays the same.

## Edges
- respects -> stylebook
- respects -> sitting-is-the-open-agent
- part-of -> brain-code
