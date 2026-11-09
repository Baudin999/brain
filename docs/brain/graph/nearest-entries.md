---
kind: information
subjects: [search, nearest, judge]
named-by: user
born: 2026-11-01
session: 5deb77af-e36d-4efb-adfd-40465f020886
---
# Every node write shows the entries nearest to it, in the CLI first and in the window while the user reviews

One query, the entries nearest to a node, lives in the CLI's search engine and both the CLI and the window use it. It searches with the whole node, title, body and why, by meaning and keywords, and returns at most 5 and possibly none. Which graphs it reads follows the three tiers: a project node is checked against its project, personal and global; a personal or global node against those and every registered project.

The nearest are a quality gate on the information. Before a write is staged, the Sonnet judge reads it against its nearest entries and refuses a write that says the same as one of them or contradicts one, naming that node. It names which of them the write should have an edge to, and a write that carries no edge while the judge names one is refused with those edges. Each caught duplicate, contradiction and missing edge is logged as evidence that brain works.

A staged write prints its nearest entries under the pending write, and `brain nearest` asks before a write. In the window, opening a waiting write fills the right panel with its nearest entries, and one opens beside the write as two documents.

## Why
Finding the relevant entries is what keeps brain free of duplicates and contradictions, and it is what AI agents need too. The judge needs to know whether we are duplicating or contradicting ourselves, so it reads them before the user does. The CLI is the core, so agents get it there; the window is how the user interacts with brain, so the user sees the same entries while reviewing.

## Edges
- follows -> personal:three-tiers
- respects -> user-says-yes
