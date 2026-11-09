// The information panel: the second flow's place, on the right of a conversation, where what the user inspects is shown
// so it never crowds the conversation. It exists only while the user has something open in it: it shows one thing under
// a head that names it and what kind it is, with a button that closes it. It keeps no state and changes only when its
// caller gives it another thing or closes it, which the caller does only on the user's click.
import { html } from '/lit-html.js';

// The panel showing body under a head: title is what it shows and kind the sort of thing it is; onClose closes it.
export function infoPanelView({ title, kind }, body, { onClose }) {
  return html`
    <aside class="info-panel" aria-label="Information">
      <header class="info-panel-head">
        <div class="info-panel-heading">
          <span class="info-panel-kind">${kind}</span>
          <h2 class="info-panel-title">${title}</h2>
        </div>
        <button class="icon" aria-label="Close the panel" @click=${onClose}>\u00d7</button>
      </header>
      <div class="info-panel-body">${body}</div>
    </aside>
  `;
}
