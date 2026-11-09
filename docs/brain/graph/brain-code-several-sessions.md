---
kind: information
subjects: [brain-code, window, agents]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# One brain code window holds several sessions alive at once, and any of them can land

There is one brain code window. A `brain code` while it is open adds a session to it, shows that session and brings the window forward. The window lists its sessions on the side, several alive at once, and starts a new one from its own button with an agent and a folder. Each session is a Claude Code session of its own, and any of them can land a write before the user.

## Why
It is just an interface over sessions. The user works with several sessions at once, as he did with several terminals, and each must be able to land, since each is a full session and not a view of one.

## Edges
- part-of -> brain-code
- respects -> user-says-yes
