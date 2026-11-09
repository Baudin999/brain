---
kind: information
subjects: [gate, judge, free-rules]
named-by: claude
born: 2026-10-18
session: 70b17ada-fe1a-4411-b5c6-e5d3f3577a72
---
# Only the brain CLI writes the graph, and every write passes its gates there

Nothing writes a graph file or an edge log except the brain CLI. A hook refuses direct edits to graph files, and to the pending writes that wait for the user.

A write first meets free rules: deterministic checks of shape that are instant and never down. A node write needs at least one subject, a keyword a search finds it by; information needs a why; a new title may not repeat a commit subject; and the edges it carries are checked as `brain link` checks them. Only what passes them goes to a Sonnet judge, which reads the meaning of title, body, why and subjects, never the structure. The judge also reads the write against its nearest entries and refuses a duplicate, a contradiction, or a write that carries no edge while it names one. It refuses when the model cannot answer, and it refuses a missing why without suggesting one: the writer asks the user. Files are written to a temp file of their own and renamed into place, and a write refuses a node that another command changed since it was read.

Gates check shape and keep the graph consistent; whether something belongs in brain is the user's call. Every node that passes review is staged as a pending write and waits for the user's yes, no or change in brain's window, and every forget waits for a yes. The window is part of the same door: brain's own server lands a write only on the user's answer to the exact text the window showed, through the same pending store and the same check that nothing changed since staging.

## Why
One door is one place where the shape of every entry can be enforced. The user trusts machine gates over promises, so a gate fails closed instead of letting a write through. An invented reason is worse than none. Several sessions write the same brain at once, and a judge call holds a write open for seconds, so a reader must always see a whole file and no write may silently undo another. The window lands through the CLI's own code so it can never write past the gates.

## Edges
- respects -> user-says-yes
- uses -> nearest-entries
