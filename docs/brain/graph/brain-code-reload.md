---
kind: information
subjects: [brain-code, install, window, update]
named-by: user
born: 2026-11-07
session: 56883eb1-778b-4d88-9c50-5d19a56e682e
---
# brain code reloads onto a new build from its own button, once no session is running

When a newer build of brain is published, by `./install.sh` or `./build.sh`, brain code shows a "reload brain code" button. Install does not restart brain code itself. The button is disabled while any session is running: a turn in progress, a process or agent it started still alive, or a question or permission ask waiting for an answer. As soon as it can safely run, the button is highlighted. A click has the old server start its successor on the new build, which resumes every session by its id, with its agent and in its folder, and shows the session that was open.

## Why
In the user's words: "We should have a button in brain code to 'reload brain code' when a new version is ready. It is disabled when there are running sessions and as soon as we can safely run it, the button should highlight." brain code's server runs every session's claude, so a restart that came from install would cut a session mid-turn and kill the agents it sent. The user decides when to reload, and only when nothing would be lost.

## Edges
- part-of -> brain-code
- relates-to -> install-restarts-window
- relates-to -> brain-code-several-sessions
- relates-to -> brain-code-lift-and-shift
