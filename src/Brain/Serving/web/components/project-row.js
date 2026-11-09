// The project row: one graph in the focus picker's list, on two lines. The first has its chip, the project in its own
// color, then how many node reviews and how many disputes wait for it. The second has the graph's path on disk, when it
// has one. It keeps no state: selected says the cursor is on it, current says the window is focused on it, a press puts
// the cursor there and runs onChoose, and the chip is a node the caller made, so the row never sets a style.
import { html, nothing } from '/lit-html.js';
import { waitingCounts } from '/components/waiting-counts.js';

export function projectRow({ chip, path, reviews, disputes, selected, current, onChoose }) {
  return html`
    <li class="row option project-row" role="option" aria-selected=${String(selected)} aria-current=${current ? 'true' : nothing}
      @mousedown=${(event) => choose(event, onChoose)}>
      <span class="project-row-head">${chip}${waitingCounts({ reviews, disputes })}</span>
      ${path ? html`<span class="project-row-path" title=${path}>${path}</span>` : nothing}
    </li>`;
}

function choose(event, onChoose) {
  event.preventDefault();
  onChoose();
}
