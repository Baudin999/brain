// The history: the button beside the settings cog, and the modal it opens. The modal lists the open agent's past
// sessions in the window's folder, newest first, from the transcripts Claude Code keeps; a click on one reads its
// conversation in the modal, which a back button leaves for the list. It is read only: the conversation draws through
// the one conversationView, under the user's own settings, and there is no way to continue the session.
import { html, nothing } from '/lit-html.js';
import { conversationView } from '/components/conversation.js';

function nobody() {}

export function historyButtonView({ onOpen }) {
  return html`
    <button class="icon" aria-label="History" title="History" @click=${onOpen}>
      <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="8" cy="8" r="6"/><path d="M8 4.5V8l2.5 1.5"/></svg>
    </button>
  `;
}

function pastRow(past, onPick) {
  return html`
    <li>
      <button class="row code-past" title=${past.title} @click=${() => onPick(past)}>
        <span class="code-past-title">${past.title}</span>
        <span class="code-past-at">${new Date(past.at).toLocaleString()}</span>
      </button>
    </li>
  `;
}

function listView(history, onPick) {
  if (history.sessions === null) return html`<p class="code-none">Reading the history.</p>`;
  if (history.sessions.length === 0) return html`<p class="code-none">The open agent has no past session in this folder.</p>`;
  return html`<ul class="code-history-list">${history.sessions.map((past) => pastRow(past, onPick))}</ul>`;
}

function readingView(open, kinds) {
  if (open.conversation === null) return html`<p class="code-none">Reading the conversation.</p>`;
  const view = conversationView(open.conversation, [], { onAgent: nobody, onCall: nobody, kinds });
  return html`<div class="code-history-read">${view}</div>`;
}

function headView(history, { onBack, onClose }) {
  const back = history.open
    ? html`<button class="button" @click=${onBack}>Back</button>`
    : nothing;
  const title = history.open ? history.open.title : 'History';
  return html`
    <header class="pane-head code-history-head">
      ${back}
      <h1 class="pane-title">${title}</h1>
      <button class="button" @click=${onClose}>Close</button>
    </header>
  `;
}

// The modal over the window, on its shade; a click on the shade outside the box closes it.
export function historyView(history, kinds, { onPick, onBack, onClose }) {
  if (!history) return nothing;
  const outside = (event) => {
    if (event.target === event.currentTarget) onClose();
  };
  const body = history.open ? readingView(history.open, kinds) : listView(history, onPick);
  return html`
    <div class="palette" @click=${outside}>
      <div class="palette-box code-history" role="dialog" aria-label="History">
        ${headView(history, { onBack, onClose })}
        ${history.problem ? html`<p class="code-problem" role="status">${history.problem}</p>` : nothing}
        ${body}
      </div>
    </div>
  `;
}
