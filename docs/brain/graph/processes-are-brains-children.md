---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 2bee16e4-e3b6-4714-98e3-efab1161b2f0
---
# Every process is a child of brain; when brain stops, all the child processes stop

Brain serve keeps a registry of what it runs, in memory only, keyed by the window's tab. Each tab holds at most one chat, and several tabs' chats live at once, because the user switches tabs to accept the node writes a chat staged. The registry holds each chat's model, effort and start time, and everything that runs under it: the chat's agent, the builders and validators it sent, and the builds and test runs they hold a slot for. The window marks a tab whose chat is alive, and closing a chat that still has an agent or a build running under it names what will stop and asks first.

Every one of those processes is a descendant of brain serve, and none outlives it: a clean stop, a kill, a crash and `./install.sh` restarting the window all end them, even one started in the background or detached from its parent. The user called this a must. Nothing about the registry is written to disk; after a restart there is nothing left to list, since everything it named stopped with serve.

## Why
A chat stays open while the user switches tabs to accept what it staged, and it can spawn agents for deeper verification or to run tests, so tabs and processes interleave. In the user's words: "This needs a registry." Without one, nobody can see what still runs under a chat, and closing it would silently kill a builder mid-build. Every process has to be a child of brain because an orphan keeps running with nobody watching it: an agent or a build left behind holds one of the machine's two build slots and loads a CPU that is already dying.

## Edges
- relates-to -> chat-and-conversation
- relates-to -> install-restarts-window
- relates-to -> global:cpu-guard
