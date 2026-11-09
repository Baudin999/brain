// The field row: one line of a form, its label and its hint, then its controls. It keeps no state: current says the keys
// are on it. data-field carries its id, so the page finds the row to scroll to and to focus.
import { html, nothing } from '/lit-html.js';

export function fieldRow({ id, label, hint, current }, controls) {
  return html`
    <div class="field-row" aria-current=${current ? 'true' : nothing} data-field=${id} tabindex="-1" role="group" aria-label=${label}>
      <div class="field-row-label">
        <span>${label}</span>
        ${hint ? html`<span class="field-row-hint">${hint}</span>` : nothing}
      </div>
      <div class="field-row-controls">${controls}</div>
    </div>`;
}
