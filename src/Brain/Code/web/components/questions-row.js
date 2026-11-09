// The questions row: the line over the session list that opens the page of every open question, with how many wait
// across the sessions, the count in the focus color while any does. It is current while that page is in view. A click
// opens the page.
import { html } from '/lit-html.js';
import { openQuestions } from '/session-state.js';

export function questionsRowView(sessions, current, { onOpen }) {
  let count = 0;
  for (const session of sessions) {
    count += openQuestions(session).length;
  }
  let state = 'none';
  if (count > 0) state = 'asking';
  return html`
    <button class="row code-questions-row" aria-current=${current ? 'true' : 'false'} data-state=${state} @click=${onOpen}>
      <span class="code-questions-name">Open questions</span>
      <span class="code-questions-count">${count}</span>
    </button>
  `;
}
