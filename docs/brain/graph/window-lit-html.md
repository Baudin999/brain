---
kind: information
subjects: []
named-by: user
born: 2026-11-04
session: a06e425d-4a30-470b-b165-f739a9f3a0ab
---
# The window is ES modules on lit-html: pages in web/pages, components in web/components, state at three levels, no hooks, no build step

A page is an ES module in `web/pages` and builds its template from its state with lit-html's `html`; a handler is a plain function that changes the state and renders again. A component is a file in `web/components` with its CSS beside it. Most components are plain functions from values to a template; one that keeps state, like the model selector with its cursor, defines its state and handlers in its own file, and the page holds the instance, so there is one render path and nothing hidden. Global state lives in `state.js`, the only module that writes it, and a change to it renders the page in view again.

lit-html is vendored as a single file under `src/Brain/Serving/shared/vendor`, which brain's window and every workspace window both serve. Modules import it directly. The views not yet moved are still classic scripts sharing one global scope: `seam.js` is the one seam between them and the modules, and `lit.js` puts `html` and `render` on `window` for them until they move. The stylebook's gates read the templates and every component sheet: a control that names no component fails, and so does a raw style.

Settings is the first page in this shape. The other views move one at a time.

## Why
The user wanted "a very, very simple js UI library, so that we do not need to do the dom manipulations by hand". Svelte was turned down because it needs a Node build toolchain, a second toolchain beside .NET. Razor with htmx was turned down because it fights the window's state in the browser and would mean rewriting the JSON API. Preact was passed over because its hooks are magic, where lit-html stays low-level: a template and a render call. The flat folder of classic scripts was "hacking this together": a component in /components and a page in /pages "keeps the code neat", and modules end the shared scope that let two scripts break each other.

## Edges
