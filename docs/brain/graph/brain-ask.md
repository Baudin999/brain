---
kind: information
subjects: [brain-code, brain-ask, agents]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# An agent asks the user questions through `brain ask`, as JSON, and brain code shows them as real questions

An agent in a brain code session asks with `brain ask`, one JSON document on stdin. Each question carries its text, optional options, the agent's recommendation and its reason. `wait` says which of two kinds the ask is: `true` is "I cannot continue without you", and `brain ask` blocks until every question is answered; `false` is "at your leisure", and the agent goes on while the answers come to it later as the user's next message.

The window draws each ask as a question in the conversation where it was asked, with the recommended option marked and its reason under it, and a field for the user's own words. Open questions stay over the composer, where they never scroll away, and an Open questions row lists every open question across the sessions. Outside a brain code session `brain ask` refuses, and the agent asks in prose.

## Why
In a terminal the agent's questions disappear in the flow, and Claude Code's default questions UI is horrible. The user likes to ask questions, learn and be in control, so the questions an agent asks are captured by the UI and presented as actual questions. Asking in JSON is what lets the UI parse them. A question either blocks the agent or not, and the JSON says which, so the agent waits only when it truly cannot go on.

## Edges
- part-of -> brain-code
- relates-to -> brain-code-several-sessions
