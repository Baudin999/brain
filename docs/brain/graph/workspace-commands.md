---
kind: idea
subjects: []
named-by: user
born: 2026-11-04
session: 812f9ff1-d454-4044-831e-354cf3c8e7cb
---
# The workspace is a few fundamentals, and everything the user or the workspace does is a command

The tool will change a lot, so the core holds only five fundamentals and the rest is a plugin:

- **Workspace** holds all state: root, active worktree, open files, focus, layout.
- **Command**: a name, a title, arguments and a run against the workspace. A key, the bar, a click, a link and a file change on disk all end in a command; nothing changes the workspace another way.
- **Keymap** is data: a key, the focus it applies in, a command. Every command has a key, and the mouse is optional.
- **View** draws from the workspace's state with lit-html and never changes it: state goes in, the page comes out, as in Elm.
- **Plugin** registers commands, keys, views and renderers. The tree and the reader are plugins on the same API as anything later.

Plugins come only from this codebase. Navarone will not ship plugins, so the API stays internal and changes freely.

## Edges
- part-of -> brain-workspace
