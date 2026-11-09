---
kind: information
subjects: [brain-code, navarone, review, agents]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# In brain code a validator sits right beside its builder, and its details show clearly how the review went

In the side bar, the validator that belongs to a builder is always vertically next to it, so which validator checks which builder is very clear.

Clicking a validator's details shows its results, so the user clearly sees how it went: whether the review passed and why, what it found and how each finding ended, and whether the merge went well, with the commits it put on the default branch. Where the work had Navarone signals, what it found includes each one and how it ended.

## Why
Being able to inspect the process is really big for the user. He wants to see what the validator and the builder disagree about and how the review went, without digging through two transcripts.

## Edges
- part-of -> brain-code
- refines -> brain-code-sent-agents
- relates-to -> navarone-in-brain
