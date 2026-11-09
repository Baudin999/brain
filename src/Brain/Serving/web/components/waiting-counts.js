// The waiting counts: how many node reviews and how many disputes wait, as a project row and the pending bar both say
// them. A count of nothing turns quiet.
import { html } from '/lit-html.js';

export function waitingCounts({ reviews, disputes }) {
  return html`
    <span class="waiting-counts">
      <span class="waiting-counts-figure" data-none=${String(reviews === 0)}>${reviews} ${reviews === 1 ? 'review' : 'reviews'}</span>
      <span class="waiting-counts-figure" data-none=${String(disputes === 0)}>${disputes} ${disputes === 1 ? 'dispute' : 'disputes'}</span>
    </span>`;
}
