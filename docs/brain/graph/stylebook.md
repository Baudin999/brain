---
kind: information
subjects: []
named-by: user
born: 2026-11-03
session: b706475d-b634-4e79-8b08-a01f894aad81
---
# The window's look lives in one stylebook: tokens and named components, shown in a Stylebook view and held by a gate

Every value brain's window and a workspace window draw with, colour, text size, radius, spacing and shadow, is a token in `src/Brain/Serving/shared/tokens.css`, and nowhere else. Every control is a named component drawn once in the shared `components.css`: button, chip, segmented switch, row, pane head (also the page head, as a variant), icon button, link, fold, field, select, toggle, field row, code mark and palette, each with variants where views genuinely differ, like the workspace's tree row. Feature sheets only place a component, with a margin, a flex or grid value, a size or a position; they never restyle it. Where two controls differ on purpose, the look is kept through a variant, not forced into one. The Stylebook view in brain's window shows every token and every component in each state, read live from the sheets, so the user judges the look by seeing it.

The gate reads the sheets and scripts of both windows. It fails the build on a raw colour, font size or radius anywhere but tokens.css, every CSS named colour included, in a sheet or in a style a script sets. It fails when a sheet other than components.css sets more than placement on a component's class, a control's tag or a control's id, and when a control the scripts or pages make names no component. A control is an element with a control's tag or role, a tabindex or an onclick. The gate cannot see a class set through a variable or classList.

## Why
The user was "very afraid that every agent is crafting their own" components: the window had grown ten hand-typed font sizes, nine radii and the same segmented switch styled twice. Prose about taste doesn't stop an agent writing CSS; a gate does, and a page lets the user judge the look by eye instead of by numbers.

## Edges
