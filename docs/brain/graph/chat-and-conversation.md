---
kind: idea
subjects: [conversations]
named-by: user
born: 2026-11-02
session: 2bee16e4-e3b6-4714-98e3-efab1161b2f0
---
# A chat is a way to get knowledge out of the graph; a conversation is about something which lasts

A chat is summoned into brain's window with Alt+A on the tab in view, starts with what that page shows, and is gone when the user closes it. Each tab holds at most one chat. The context is what was in view when the chat was summoned; selecting something else afterwards does not change it. Page by page:

- Home: general questions. A node that is selected, or pinned with shift, goes into the context, and the chat will probably be about that node. With no node selected it is a chat with no context.
- Opened node, the node's own page, reached from a search result or by double clicking a node on Home: the chat starts with that node and is about it.
- Node review, which the user called very important: the chat starts with the write that waits for the user's yes, its full text and an edit's diff, so the user can ask it to change the write.
- Navarone, and any source's inbox: the chat is about the open problem in view, handed over as the generic blocks the inbox already renders, so brain never learns what a dispute is.
- Evidence: the chat is about the evidence in view.
- Dreams: the chat starts with the open dream. In the user's words, settled "might even evolve and change due to the chat".
- Conversations: no chat. A conversation already has its own agent.

Whatever the context, a chat is the open agent: the same agent as a sitting and as `claude --agent brain` in a terminal. It runs on Sonnet at medium effort unless the user picks another model, such as Opus. It reads the graph and the code, sends builders and validators after the readiness check, and changes nodes the way any open agent does: it stages the write, and the write waits for the user's yes in the window. There is no manual way to change a node, so a chat is where the user does it. What a chat never does is answer for the user: it may explain both sides of a dispute, but the judgment and every yes stay the user's own click.

The line between a chat and a conversation is time. A conversation is one topic the user comes back to over days, with its folder, trails and settled.md. A chat is something done now and closed: "clean up these three nodes", "what does the graph say about X", "how is project Y doing". What a chat taught lands in the graph or in a commit; the chat itself keeps nothing, and the window keeps no list of past chats, since that would be retention again.

## Edges
- relates-to -> sitting-is-the-open-agent
- relates-to -> one-door
- relates-to -> tool-disputes
