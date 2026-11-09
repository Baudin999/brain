---
kind: information
subjects: []
named-by: claude
born: 2026-10-18
session: 70b17ada-fe1a-4411-b5c6-e5d3f3577a72
---
# Brain is read a rung at a time, broad first, drilling down on purpose toward the code

Reading goes down in rungs, each about ten times the cost of the one above. brain lookup finds which nodes exist, brain node shows one whole, with its body, why and edges, and brain walk shows its neighbours within about a thousand tokens. Below that lie git show and the user's own words in voices, and finally the source. A minimap of subjects and projects gives the broad view to start from.

Lookup and the window's search box share one engine, and it searches the graph's nodes only. Conversations, voices and research docs stay out of search until what they hold lands in the brain as a node. An exact slug or title comes first. Then three rankings are fused: structure (title prefix and subjects), full text over slug, title, subjects, why and body, and meaning from a small embedding model that runs inside brain with no service. Which model that is, and the bars its cosines are read against, live in that model's profile; search itself names no model. Neighbours of the top hits get a small boost by edge weight. Without the model, which brain install downloads, lookup searches structure and full text alone.

Edges carry weights. An edge is travelled, and strengthens, only when both its ends are opened with brain node in one session; cache/trail.sqlite remembers what each session opened. Walking past an edge does not count. The weights live in the data repo's cache, so reading never changes a repo. Edges nobody travels decay, and brain faded lists them: a faded edge is a question to raise in conversation, whether the connection still holds.

## Why
Context is the scarce thing: reading the whole graph would drown the question, so each step down has to be a choice, and a good ranking means fewer nodes to read. Keywords alone miss a subject whose words differ from the question, but reading must never depend on a model or a service, so the model runs inside brain and search still works without it. A shown edge often leads nowhere, so only an edge whose both ends were opened says the connection was useful.

## Edges
