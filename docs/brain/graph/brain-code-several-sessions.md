---
kind: information
subjects: [brain-code, window, agents, project]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code is one window per project, holding that project's sessions; brain ui is the one shared window

brain code is project based. A project is a folder the user opens with his project opener, SUPER+SHIFT+P, the same
folder brain workspace opens its window over. Each project has its own brain code window. A `brain code` over a project
whose window is open adds a session to that window, shows it and brings the window forward; over a project with no
window it opens a new one. Two projects are two windows, side by side on their own workspaces, and opening one never
pulls the other's window away. Inside a window the sessions are listed on the side, several alive at once, and its
"New session" block starts one with an agent, its folder set to the window's project. Each session is a Claude Code
session of its own, and any of them can land a write before the user. brain ui stays one window for everything.

## Why
The user works on several projects at once, each on its own workspace, as he did with a Zed and a terminal per project.
One shared brain code window followed him from workspace to workspace and left the other project without one. Within a
project he works with several sessions at once, and each must be able to land, since each is a full session and not a
view of one.

## Edges
- part-of -> brain-code
- respects -> user-says-yes
