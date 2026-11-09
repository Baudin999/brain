// Quoting: text selected in the conversation or in the information panel can go into the composer as markdown quote
// lines. A small Quote button sits beside the selection, and Alt+Q does the same. A plain selection never touches the
// box, so copying stays clean. Selected text is taken as shown, without rebuilding the markdown it came from. The
// composer owns the box: this module only reads the selection and draws the button; composerQuote() puts the words in.
import { html, nothing } from '/lit-html.js';

// Where text can be quoted from: the conversation's scroll and the information panel's body.
const QUOTABLE = '.code-scroll, .info-panel-body';

// The room kept free at the window's edges, so the button stays in view.
const EDGE = 120;

// The selection when it lies inside one quotable area, as { text, top, left } with the button's place just under it;
// null when nothing quotable is selected.
export function quoteOfSelection(selection) {
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null;
  const range = selection.getRangeAt(0);
  const common = range.commonAncestorContainer;
  const parent = common.nodeType === Node.TEXT_NODE ? common.parentElement : common;
  if (!parent.closest(QUOTABLE)) return null;
  const text = selection.toString().trim();
  if (text === '') return null;
  const box = range.getBoundingClientRect();
  return { text, top: box.bottom, left: Math.max(0, Math.min(innerWidth - EDGE, box.left)) };
}

// The Quote button beside the selection, or nothing; quotePlace() puts it where the selection is once it is drawn. A
// press with the pointer never takes the selection from the page.
export function quoteView(quote, onQuote) {
  if (!quote) return nothing;
  return html`
    <button class="button float code-quote" @mousedown=${(event) => event.preventDefault()} @click=${onQuote}>
      Quote <kbd>Alt Q</kbd>
    </button>
  `;
}

// Puts the drawn Quote button of host beside the selection it was read for.
export function quotePlace(host, quote) {
  const button = host.querySelector('.code-quote');
  if (!button || !quote) return;
  button.style.top = `${quote.top}px`;
  button.style.left = `${quote.left}px`;
}
