---
kind: information
subjects: []
named-by: user
born: 2026-11-04
session: d7d81c9b-2040-4e6d-b464-1e74159ba97a
---
# In brain's window, state, drawing and the viewport are decoupled: a state change never moves focus or the scroll position

A state change renders the page again, and that is all it does. Rendering draws; it does not focus an element or scroll the viewport. Focus and scroll move only where code is written to move them, on purpose, for a reason the user can see: the keys moving the cursor to another row brings that row into view, and a chat that an agent writes into scrolls to its newest message.

So a redraw that comes from somewhere else, a progress poll, a global setting saved, another page's state, leaves the viewport and the focus where the user put them.

## Why
The user: "state, draw and viewport should be decoupled. a state change should never interact with the scroll position directly unless specifically written". When every redraw also focused and scrolled, a switch's progress poll redrew the settings page twice a second and pulled the view back to the current row while the user read elsewhere.

## Edges
