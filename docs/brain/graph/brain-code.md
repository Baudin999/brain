---
kind: information
subjects: [brain-code, window, agents, herdr, terminal]
named-by: user
born: 2026-11-07
session: eb2b8519-f279-4098-a18e-cfb794315943
---
# brain code is our terminal replacement: Claude Code sessions run in a brain window, in brain's styling

`brain code` replaces herdr. It runs Claude Code sessions in a brain window and not in a terminal. `brain code` opens the open agent, `brain code immediate` opens the immediate agent, and any agent file's name works. Each session runs in a folder, and the window shows that folder, because the path matters.

The window uses brain's components and styling. It renders markdown and mermaid, and it holds several sessions alive at once, any of which can land a write. A conversation that runs in a terminal can be lifted and shifted into the window, keeping its history.

It is an interface over sessions, made to grow. Clicking an agent a session sent, a builder or a validator, opens that agent's conversation. The window shows the evidence trail between a builder and its validator, so you can read what they disagree about clearly. Things that need the user, such as a permission request or an agent's questions through `brain ask`, wait for him in the window.

## Why
The user does not like herdr's way of working, but he does want to use Claude Code on his own plan. What he dislikes is being unorganized. He likes to ask questions, to learn and to be in control, and to see when validators spin up, what they find and what the builders do. He wants his harness to feel native to the app. Being able to inspect the process matters most: what the builder and the validator disagree about, made visible in a clear, easy to understand way. A terminal cannot show that, and a brain window can.

## Edges
- part-of -> what-brain-is
- relates-to -> sitting-is-the-open-agent
- relates-to -> navarone-in-brain
- respects -> user-says-yes
- respects -> agents-never-steer-the-window
- defined-in -> file:README.md
