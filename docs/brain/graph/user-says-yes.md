---
kind: information
subjects: [user-says-yes, review, window]
named-by: user
born: 2026-10-28
session: 18a5fe99-0ac8-4435-9002-6f25e9b648e3
---
# Every node going into or out of brain waits for the user's yes, no or change

After a node passes review, Claude stages it and runs `brain land`, which waits. The user reads the write in full in brain's window and answers there. Yes lands exactly that text. No drops it. No with words is a change: the user says in their own words what to change, and that answer goes back to the agent that asked, which rewrites the node, has it reviewed again and stages it again, so it shows up in the window as a new item. Edits and merges are asked the same way; an edit shows as a diff. Forget shows each node in full and asks yes or no. This holds for every node in every scope, from every session and agent.

A node write carries its subjects and its edges, and the window shows them with it. They land with the node on the yes, and a no lands none of them, so edges exist only on accepted nodes. An edge between two nodes that already exist, through `brain link`, is not asked.

## Why
Agents put things into brain too quickly. The review checks shape, but whether something belongs in brain is the user's call, so the user says OK on everything that goes in or out. The yes moved out of Claude Code's permission prompt into brain's window because the terminal cut off long writes, and a yes to a text you cannot read is no yes. Edges travel with the node because we only want edges on accepted documents.

## Edges
- refines -> one-door
