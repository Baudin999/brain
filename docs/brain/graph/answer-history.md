---
kind: information
subjects: []
named-by: user
born: 2026-11-04
session: 5dd2fe5d-2bfb-4d53-9dd7-b741de5f1fa4
---
# Every answer stays findable for 2 days in brain's window, and can be undone there

Brain's window has an Answered page: every write the user answered in the last 2 days, yes, no and no with words, newest first, each shown exactly as it was asked, with the answer and when it was given. Undo on a yes reverses exactly what that write did, all or nothing. Undo on a no puts the write back in the queue to be answered again. The click is the user's yes, so an undo asks nothing more, and it goes through the pending store like an answer. An undo refuses when a node no longer reads as the yes left it, or when an edge from outside the write points at a node it would remove, and shows what changed. The history lives in the data repo's gitignored cache, never in the graph or in git, and is swept after 2 days.

## Why
A yes or a no pressed by mistake could not be found again, and undoing it took a new forget or edit that waited for another yes. Two days is enough to catch a mistake; an older one is what edit and forget are for. The history is a record of answers, not knowledge, so it stays out of the graph and out of git.

## Edges
