// The option list: a listbox of rows the keys move a cursor over, as a picker in a dialog shows its choices. It keeps no
// state: cursor is the index the cursor is on, and current says the keys are in this list, so its cursor is also the
// current row. Each option is { text, hint, disabled, chosen }: a hint is small print beside the text, and the chosen
// one, the value in use now, carries a check. A press puts the cursor on an option through onPoint(index), and keeps
// the focus where it is; a double click runs onChoose.
import { html, nothing } from '/lit-html.js';

export function optionList({ label, labelledBy, options, cursor, current, onPoint, onChoose }) {
  return html`
    <ul class="option-list" role="listbox" aria-label=${label ?? nothing} aria-labelledby=${labelledBy ?? nothing}>
      ${options.map((option, index) => html`
        <li class="row option compact option-list-item" role="option" aria-selected=${String(index === cursor)}
          aria-disabled=${option.disabled ? 'true' : nothing} aria-current=${current && index === cursor ? 'true' : nothing}
          @mousedown=${(event) => point(event, index, onPoint)} @dblclick=${onChoose}>
          <span class="option-list-text">${option.text}</span>
          ${option.hint ? html`<span class="option-list-hint">${option.hint}</span>` : nothing}
          ${option.chosen ? html`<span class="option-list-now" aria-label="now">✓</span>` : nothing}
        </li>`)}
    </ul>`;
}

function point(event, index, onPoint) {
  event.preventDefault();
  onPoint(index);
}

// The cursor's step for a key that moves it down or up a list, the vim motion or the arrow, and 0 for any other key.
export function stepOfKey(key) {
  if (key === 'j' || key === 'ArrowDown') {
    return 1;
  }
  if (key === 'k' || key === 'ArrowUp') {
    return -1;
  }
  return 0;
}

// The cursor moved by a step in a list of count options, held within the list.
export function movedCursor(cursor, by, count) {
  return Math.max(0, Math.min(count - 1, cursor + by));
}
