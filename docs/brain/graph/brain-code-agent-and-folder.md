---
kind: information
subjects: [brain-code, agents, folder]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code starts a session of any agent in any folder: `brain code [agent] [folder]`

`brain code` starts the open agent, `brain code immediate` the immediate agent, and any agent file's name works. A positional that names an existing folder, such as `.`, `~/x` or a full path, is the folder; any other is the agent, in either order. A folder that does not exist is refused, as are two folders or two agents. The session list and each session's head show the session's full folder.

## Why
The user picks which kind of agent he works with when he starts it, as he does in a terminal. The path is really important: an agent that runs in the wrong folder works on the wrong repo, as the first `brain code .` did when it opened in the home folder instead of the project.

## Edges
- part-of -> brain-code
