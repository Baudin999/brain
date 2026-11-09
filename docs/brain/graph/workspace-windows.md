---
kind: idea
subjects: []
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# Each project has its own workspace window, run by its own small server process

There can be many workspace windows, one per project. Running `brain workspace` on a project that is already open brings its window forward instead of opening a second. brain's window stays the only one of its kind; that rule is brain's, not the workspace's.

Each workspace runs its own small server process from the same `brain` binary, separate from brain's window's server. It owns only its root and ends when its window closes. A workspace that hangs takes nothing else down. It lives in the brain repo, beside brain's window.

## Edges
- part-of -> brain-workspace
