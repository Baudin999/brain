// The open questions of a session, in one place that never scrolls away: a line over the composer for each ask that
// waits for the user, its first question's first line, how many it holds, and whether the agent waits. A click shows
// the ask where it stands in the conversation, since the user asked to see it there.
import { html, nothing } from '/lit-html.js';
import { openQuestions } from '/session-state.js';

// The first line of an ask's first question, without its markdown marks.
function headline(asked) {
  const text = asked.questions[0].text || '';
  return text.split('\n')[0].replace(/[*_`#>]/g, '').trim();
}

function countOf(asked) {
  if (asked.questions.length === 1) return '';
  return `${asked.questions.length} questions`;
}

function waitOf(asked) {
  if (asked.wait) return 'the agent waits';
  return 'at your leisure';
}

function askRow(asked, onShow) {
  return html`
    <li>
      <button class="row compact code-open-question" @click=${() => onShow(asked)}>
        <span class="code-open-question-text">${headline(asked)}</span>
        <span class="code-open-question-meta">${countOf(asked)}</span>
        <span class="code-open-question-meta">${waitOf(asked)}</span>
      </button>
    </li>
  `;
}

export function openQuestionsView(session, { onShow }) {
  const asks = openQuestions(session);
  if (asks.length === 0) return nothing;
  return html`
    <section class="code-open-questions" aria-label="Open questions">
      <h2 class="code-open-questions-title">Open questions</h2>
      <ul class="code-open-questions-list">${asks.map((asked) => askRow(asked, onShow))}</ul>
    </section>
  `;
}
