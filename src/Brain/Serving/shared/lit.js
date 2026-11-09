// lit-html for the window's classic scripts: html, render and nothing, as globals. lit-html ships only as a module, and
// a module script without async runs in the page's order with the deferred scripts, so every script after it sees them.
import { html, render, nothing } from '/lit-html.js';

Object.assign(window, { html, render, nothing });
