// The session list: every session of the window on the side, the one in view current. Each row names its agent, the
// folder it runs in, what it is doing: it asks the user's permission or a question, is answering, has ended, or waits
// for the user's words, and how many of the agents it sent still run, so a new agent marks its session without taking
// the user's view. A click picks it.
import { html, nothing } from '/lit-html.js';
import { sessionState } from '/session-state.js';

const STATE_WORDS = { ended: 'ended', asking: 'asks you', working: 'working', waiting: 'waiting' };

// How many of the agents a session sent still run, in words, or nothing when none does.
function agentsView(session) {
  if (session.agents === 0) return nothing;
  let words = `${session.agents} agents running`;
  if (session.agents === 1) words = '1 agent running';
  return html`<span class="code-session-agents">${words}</span>`;
}

function sessionRow(session, current, onPick) {
  const state = sessionState(session);
  return html`
    <li>
      <button class="row code-session" aria-current=${current ? 'true' : 'false'} title=${session.cwd} @click=${() => onPick(session.id)}>
        <span class="code-session-agent">${session.agent}</span>
        <span class="code-session-state" data-state=${state}>${STATE_WORDS[state]}</span>
        <span class="code-session-folder">${session.cwd}</span>
        ${agentsView(session)}
      </button>
    </li>
  `;
}

export function sessionListView(sessions, selected, { onPick }) {
  if (sessions.length === 0) {
    return html`<p class="code-none">No session yet. Start one below, or run brain code in a terminal.</p>`;
  }
  return html`<ul class="code-sessions">${sessions.map((session) => sessionRow(session, session.id === selected, onPick))}</ul>`;
}
