---
kind: idea
subjects: [conversations, dreaming]
named-by: user
born: 2026-11-03
session: 2e6e1b51-5f4b-4f70-887e-453b61fcca7c
---
# A session parks into a conversation, a conversation is removed whole, and a dream compresses and then embeds into the graph

The user keeps no history of sessions with the models, by design. Sometimes he wants to, for example when he goes to bed in the middle of something. Then the unfinished session is parked "directly into a conversation": it becomes a conversation in brain, and the next session resumes from there, the way a new sitting starts from what the conversation holds.

When a conversation is no longer needed, removing it deletes its folder, all of it: `settled.md`, the trails, the research and everything else. Nothing of it is kept as a closed conversation.

"Dream" means what the user means by it, in two steps:

1. Compress the sittings into `settled.md`, so the context over multiple sessions does not bloat too much.
2. Really dream: embed into brain. Search the graph, update the nodes that are stale, delete the nodes that are no longer needed and add what is new. This step runs only when the user asks for it, from a second button, "dream into brain". The open agent runs it, not the tool-less dreamer of step one, because only the open agent searches the graph and stages writes. It prioritizes `settled.md` but may read anything in the conversation it wants. A dream cannot blindly change the graph: every write it makes is staged through the CLI and waits for the user's yes, one node at a time, so he can refuse one change and keep the rest.

## Edges
