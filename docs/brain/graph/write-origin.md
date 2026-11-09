---
kind: information
subjects: []
named-by: user
born: 2026-11-01
session: 5deb77af-e36d-4efb-adfd-40465f020886
---
# Every waiting write shows the graph it lands in, and the window's project focus filters on it

A write waiting for the user's yes belongs to the graph it lands in: personal, global, or a project by its name in projects.tsv. A personal note staged while working on CkLang waits under personal, not CkLang. In the user's words: "under personal of course". Brain's window shows that project as a chip in its color on every waiting write and counts the writes per project. The window's one project focus, chosen at the top of the queue or with Alt+P, shows only that project's work in every inbox.

## Why
Writes from several projects waited at once and the window did not say where each belonged. The user works with full focus on one project and does not want to be bothered by others, and wants to keep the nodes of one graph clean in one sitting. Each focus showing exactly one graph serves both. This replaces the earlier choice of the session's project, under which a personal note from CkLang work counted as CkLang.

## Edges
- refines -> user-says-yes
- uses -> project-colors
