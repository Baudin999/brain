---
kind: information
subjects: []
named-by: user
born: 2026-11-01
session: 5deb77af-e36d-4efb-adfd-40465f020886
---
# There can be only one brain window

`brain ui` and a restart always end with exactly one window, the fresh one. A new window, once it has traded its launch code, tells every older window to close, and a server that stops tells its windows to close before it goes. The window closes itself on that message with `window.close()`; nothing kills a browser process, since every brain window shares one Chromium process. A window that loses its server without the message, through a crash or a kill by hand, closes itself too. Only a window shut by the key alarm stays open, to say why.

## Why
After an install the user found two windows: a dead one saying "This window is closed" beside the new one. The user said "there can be only one". The close travels as a message to the page so it works on macOS as well as Hyprland, and so closing the old window never takes the new one down with it.

## Edges
- refines -> install-restarts-window
- respects -> user-says-yes
