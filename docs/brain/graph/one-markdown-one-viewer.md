---
kind: information
subjects: []
named-by: user
born: 2026-11-04
session: 95440cc7-9c8b-4597-8e6d-0d73dd3a1a4c
---
# Both windows have one way of rendering markdown and one way to work with images

Every place in brain's window and the workspace draws markdown through one component, `markdownView` in `src/Brain/Serving/shared/components/markdown.js`: node docs, question text, conversations, chat, dreams, Home's tip and the workspace reader. It always draws mermaid, and it draws its own diagrams, so no page has a step to forget. What differs between places, like the reader's links and line marks, is an option of the component, never a second path.

Clicking a diagram opens it in the image viewer, `shared/components/image-viewer.js`, the one way to look at an image: an overlay where scroll zooms, a drag pans, Alt+R resets and Esc closes. Each window holds the viewer in its own state, as it holds its other overlays. While the viewer is open it takes Esc and Alt+R first, so Home's Alt+R never fires underneath it.

## Why
Landing a node drew no mermaid while a conversation did, because three wrappers each chose whether diagrams were drawn. The user read that as "a signal that we are duplicating and not doing things correctly", and asked for "1 way of rendering markdown, 1 way to work with images".

## Edges
