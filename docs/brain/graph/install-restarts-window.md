---
kind: information
subjects: []
named-by: user
born: 2026-11-01
session: 5deb77af-e36d-4efb-adfd-40465f020886
---
# ./install.sh restarts a running window on the new build, and the old server starts its successor

When install has rebuilt brain and a window server for this brain is running, install asks it to restart. The old server lets an answer already being written land, refuses later ones until it is gone, and then starts its own successor on the new build, from its own environment. So install restarts the window from a Claude session too, and no session owns or steers the new server. Waiting writes and a blocked `brain land` carry over to the new window. When no server is running, install starts none. Install does not look for servers by process name: one from before this behaviour is stopped by hand once.

## Why
The user rebuilt and installed brain and saw no difference, because the window server kept running the old build. The user wants an install to show the new build without killing anything by hand. A server a session starts could be steered to answer the user's writes for them, so the restart must not let a session own the new server; having the old server start its successor gives the restart from anywhere without that.

## Edges
- refines -> open-source-install
- respects -> user-says-yes
