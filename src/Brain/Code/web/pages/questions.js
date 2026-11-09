// The questions page: every question that waits for the user, across all sessions, the oldest session first, each
// under a line naming its session, its agent and its folder, which opens that session. He answers each here as he
// would in its session.
import { html, nothing } from '/lit-html.js';
import { openQuestions } from '/session-state.js';
import { sessionQuestionView } from '/components/session-question.js';

function folderName(cwd) {
  const parts = cwd.split('/').filter((part) => part.length > 0);
  if (parts.length === 0) return cwd;
  return parts[parts.length - 1];
}

function askedView(session, asked, actions) {
  return html`
    <div class="code-asked">
      <button class="link code-asked-session" title=${session.cwd} @click=${() => actions.onPick(session.id)}>${session.agent} in ${folderName(session.cwd)}</button>
      ${sessionQuestionView(session, asked, actions)}
    </div>
  `;
}

export function questionsPage(sessions, actions) {
  const waiting = [];
  for (const session of sessions) {
    for (const asked of openQuestions(session)) {
      waiting.push(askedView(session, asked, actions));
    }
  }
  let none = nothing;
  if (waiting.length === 0) none = html`<p class="code-none">No question waits for you.</p>`;
  return html`
    <header class="pane-head code-head">
      <h1 class="pane-title">Open questions</h1>
    </header>
    <section class="code-scroll" aria-label="Open questions">
      <div class="code-events">${waiting}</div>
      ${none}
    </section>
  `;
}
