// The strip down the window's left edge, the strip of brain's window and its inboxes: for the session in view, one tab
// shows its conversation and one its folder's code, its workspace, and at the far end the toggle that shows the two side
// by side. A tab is current while what it shows is in view, both of them side by side, and the toggle pressed while it
// is on. With no session in view the strip is empty. A click does what the tab's key does.
import { html } from '/lit-html.js';

const TABS = [
  { view: 'conversation', label: 'Conversation', key: 'Alt+C' },
  { view: 'code', label: 'Code', key: 'Alt+W' },
];

function tab(each, shown, onShow) {
  return html`
    <button class="row vertical" aria-current=${String(shown[each.view])} title=${`${each.label}, ${each.key}`} @click=${() => onShow(each.view)}>
      ${each.label}
    </button>
  `;
}

// shown is { conversation, code }, what is in view, or null with no session in view; beside says side by side is on.
export function viewStripView(shown, beside, { onShow, onBeside }) {
  if (!shown) return html`<nav class="strip" aria-label="Views"></nav>`;
  return html`
    <nav class="strip" aria-label="Views">
      ${TABS.map((each) => tab(each, shown, onShow))}
      <span class="strip-gap"></span>
      <button class="row vertical" aria-pressed=${String(beside)} title="Side by side, Alt+V" @click=${onBeside}>
        Side by side
      </button>
    </nav>
  `;
}
