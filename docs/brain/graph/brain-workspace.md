---
kind: idea
subjects: []
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# brain workspace is an IDE per project, tailored to how the user reads code, opened with `brain workspace <path>`

`brain workspace <path>`, often `brain workspace .`, opens a workspace over one project. It is a different thing from brain's window. The two feel connected through the shared look and the brain CLI, but neither is inside the other.

The user used Zed only to watch a project's tree, click through it and read files. The workspace keeps that part and makes it amazing:

- exploring the file structure
- reading files: code and markdown
- writing markdown. The user no longer writes code, so the workspace doesn't type code.
- switching worktrees, very fast and very easily
- showing git: what agents are working on, which worktrees can be cleaned up, pushing to remote

Out of scope: CLI integration, sign-in, collaboration and typing code.

Questions about what the user reads go through a door into a sitting with the open agent, with the file as context. There is no separate AI inside the workspace.

This node is the thesis. Each feature has its own node, because features change and the thesis stays the same.

## Edges
- respects -> stylebook
- respects -> sitting-is-the-open-agent
