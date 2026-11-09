---
kind: idea
subjects: []
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# Logging, telemetry and performance are in the workspace from day one, and never leave the machine

Every command is a record of its name, arguments, source, duration and outcome. The server records each file read, listing, watch event and git call the same way, so a slow command traces to the server work it caused. A probe in the window shows the last few seconds.

Budgets are measured on every command, not by benchmarks, and a command over budget is marked in the log: a keypress paints within a frame, a file opens under 100 ms, a large repo's tree shows under 200 ms, and an idle workspace uses next to no CPU. Files are watched through inotify, never by polling.

## Edges
- part-of -> brain-workspace
- refines -> workspace-commands
