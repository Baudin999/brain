// The select: a value to change, a button that opens its picker in a dialog. It keeps no state. text is the value, and
// aside, when given, a second value beside it in small print. expanded says its picker is open, and focused that it is
// the one the keys act on in a row of two. The keys reach it through the row it sits in, so it takes no tab stop.
import { html, nothing } from '/lit-html.js';

export function select({ label, text, aside, expanded, focused, onOpen }) {
  return html`
    <button class="select" tabindex="-1" aria-haspopup="dialog" ?data-focus=${focused}
      aria-expanded=${String(expanded)} aria-label=${label} @click=${onOpen}>
      <span>${text}</span>
      ${aside === undefined ? nothing : html`<span class="select-aside">${aside}</span>`}
    </button>`;
}
