---
kind: information
subjects: [brain-code, window, conversations, information-panel]
named-by: user
born: 2026-11-07
session: c7dfe40d-d63a-4157-b38f-2fb16c770b16
---
# A conversation has a main flow and a second flow; the second flow is inspected on a click, in the information panel

The main flow is the conversation: the user asks, the agent answers, the agent asks questions, the user answers. The second flow is what the user inspects: the tool calls, the brain queries, the thinking, and later kinds. Each kind of the second flow has a toggle in the window's settings to show or hide it. The second flow is drawn as clear lines, a run of calls in a row folded into one, and a click opens one in the information panel beside the conversation: the conversation on the left, extra information on the right. A brain query shows the items it found there.

## Why
The user wants to see tool calls very clearly without them crowding what he and the agent say to each other. The conversation column is narrow, so there is room beside it for details.

## Edges
- part-of -> brain-code
- respects -> agents-never-steer-the-window
- respects -> stylebook
