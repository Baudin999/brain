---
kind: information
subjects: [brain-code, agents, landing, git]
named-by: user
born: 2026-11-13
session: d4793294-56f0-4654-812f-f644b2454134
---
# Whether a piece of work landed is asked of git, never read from what an agent printed

brain code's agents panel ends a builder's chain in a landing when the commits the builder made in its worktree are on
the repo's default branch. The server asks git: it holds after the worktree is removed and the branch deleted, and
after the validator rebased the branch, when the landed hashes differ from the builder's. Work whose commits never
reached the default branch does not show as landed. What a validator printed, and how it piped its git commands, plays
no part.

## Why
The user: "that should be deterministic, not through an LLM. I think we should check the worktree's commits with main
to see if it landed." The panel parsed git's fast-forward lines out of the validator's output; one `| tail -3` cut them
off, and work that was on main showed as "It ended without landing".

## Edges
- part-of -> brain-code-sent-agents
