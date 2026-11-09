---
kind: information
subjects: [window]
named-by: user
born: 2026-11-01
session: 5deb77af-e36d-4efb-adfd-40465f020886
---
# There can be only one brain window

Every way brain's window comes up ends with exactly one window. A restart onto a new build keeps the window that is open and loads the new build in it, so no second window ever exists. `brain ui` or `brain code` started while a window is open opens a new one, which tells every older window to close once it has traded its launch code. The window closes itself on that message with `window.close()`; nothing kills a browser process, since every brain window shares one Chromium process. A window that loses its server without the message closes itself too. Only a window shut by the key alarm stays open, to say why. Closing the window never asks first.

## Why
The user said "there can be only one". A window that moves on in place needs no close to work and stays where the user put it. The close travels as a message to the page so it works on macOS as well as Hyprland, and so closing the old window never takes the new one down with it.

## Edges
- respects -> user-says-yes
- refines -> install-restarts-window
- relates-to -> brain-code-reload
