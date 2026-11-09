// The session list: every session of the window on the side, the one in view current. Each row names its agent, the
// folder it runs in, what it is doing: it asks the user's permission or a question, is answering, has ended, or waits
// for the user's words, and how many of the agents it sent still run, so a new agent marks its session without taking
// the user's view. A click picks it. Each row is headed by the session's name, the title /rename gives it, or else the
// first words of the user's first prompt, which a hover shows whole. A double-click or F2 on a row opens a field to
// rename it: Enter applies, Escape cancels, and a name that breaks the rule stays in the field with its reason.
import { html, nothing } from '/lit-html.js';
import { sessionState } from '/session-state.js';

const NAME_LENGTH = 60;
const STATE_WORDS = { ended: 'ended', asking: 'asks you', working: 'working', waiting: 'waiting' };

// How many of the agents a session sent still run, in words, or nothing when none does.
function agentsView(session) {
  if (session.agents === 0) return nothing;
  let words = `${session.agents} agents running`;
  if (session.agents === 1) words = '1 agent running';
  return html`<span class="code-session-agents">${words}</span>`;
}

// Why a name is refused, or null when it is fine: it holds only letters and digits, at most 60 of them, and no other
// session of the window carries it, whatever the case. The server checks the same rule.
export function nameProblem(name, session, sessions) {
  const trimmed = name.trim();
  if (trimmed.length === 0) return 'a name is empty';
  if (!/^[A-Za-z0-9]+$/.test(trimmed)) return 'a name holds only letters and digits';
  if (trimmed.length > NAME_LENGTH) return `a name is at most ${NAME_LENGTH} characters`;
  const taken = sessions.some((one) => one.id !== session.id && one.name?.toLowerCase() === trimmed.toLowerCase());
  if (taken) return `another session is named ${trimmed}`;
  return null;
}

function renameKeyed(event, session, handlers) {
  if (event.key === 'Enter') {
    event.preventDefault();
    handlers.onRename(session, event.target.value);
  }
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    handlers.onRenameCancel();
  }
}

function renameRow(session, renaming, handlers) {
  return html`
    <li>
      <div class="row code-session code-session-renaming">
        <input class="field mono code-session-rename" aria-label="Name of the session" aria-invalid=${renaming.problem ? 'true' : 'false'}
          .value=${session.name ?? ''} @keydown=${(event) => renameKeyed(event, session, handlers)} @blur=${handlers.onRenameCancel}>
        ${renaming.problem ? html`<span class="code-session-problem" role="alert">${renaming.problem}</span>` : nothing}
      </div>
    </li>
  `;
}

function renameStarted(event, session, handlers) {
  if (event.key === 'F2') {
    handlers.onRenameStart(session.id);
  }
}

function titleView(session) {
  if (!session.title) return nothing;
  return html`<span class="code-session-title">${session.title}</span>`;
}

function sessionRow(session, current, handlers) {
  const state = sessionState(session);
  return html`
    <li>
      <button class="row code-session" aria-current=${current ? 'true' : 'false'} title=${session.prompt ?? nothing}
        @click=${() => handlers.onPick(session.id)} @dblclick=${() => handlers.onRenameStart(session.id)}
        @keydown=${(event) => renameStarted(event, session, handlers)}>
        ${titleView(session)}
        <span class="code-session-agent">${session.agent}</span>
        <span class="code-session-state" data-state=${state}>${STATE_WORDS[state]}</span>
        <span class="code-session-folder">${session.cwd}</span>
        ${agentsView(session)}
      </button>
    </li>
  `;
}

function rowOf(session, selected, renaming, handlers) {
  if (renaming?.id === session.id) return renameRow(session, renaming, handlers);
  return sessionRow(session, session.id === selected, handlers);
}

export function sessionListView(sessions, selected, renaming, handlers) {
  if (sessions.length === 0) {
    return html`<p class="code-none">No session yet. Start one below, or run brain code in a terminal.</p>`;
  }
  return html`<ul class="code-sessions">${sessions.map((session) => rowOf(session, selected, renaming, handlers))}</ul>`;
}
