// The agent row: one agent a session sent, a whole line to click, as brain code's window lists it beside a
// conversation and draws it in the conversation where it was sent, and as the Stylebook shows it. It names the agent's
// kind, what it was sent to do, and its state with how long it ran: running, finished or failed, as the server says, a
// readiness check that finished saying its verdict, READY or NOT READY. A click opens the agent's own view.
//
// The agent is the server's shape: { agent, tool, type, kind, description, state, started, ended, verdict }.
import { html, nothing } from '/lit-html.js';

const MINUTE_MS = 60000;
const HOUR_MINUTES = 60;

const KINDS = { builder: 'Builder', validator: 'Validator', ready: 'Readiness check' };

// The agent's kind in words: builder, validator, readiness check, or its own type for any other.
export function kindLabel(sent) {
  return KINDS[sent.kind] || sent.type;
}

// How long an agent ran, or runs so far, in words; empty before it wrote.
export function ranFor(sent, now) {
  if (!sent.started) return '';
  let until = now;
  if (sent.ended) until = Date.parse(sent.ended);
  const minutes = Math.floor((until - Date.parse(sent.started)) / MINUTE_MS);
  if (minutes < 1) return 'under a minute';
  if (minutes < HOUR_MINUTES) return `${minutes} min`;
  const hours = Math.floor(minutes / HOUR_MINUTES);
  return `${hours} h ${minutes % HOUR_MINUTES} min`;
}

// What the agent is now, in words: a readiness check that finished says its verdict.
function stateLabel(sent) {
  if (sent.state === 'finished' && sent.verdict) return sent.verdict;
  return sent.state;
}

// The agent's state and how long it ran, as its row says them.
function stateWords(sent, now) {
  const ran = ranFor(sent, now);
  if (!ran) return stateLabel(sent);
  return `${stateLabel(sent)} · ${ran}`;
}

// The row of one agent; opened marks the one open, now is the time a running agent's run is counted to, and
// onOpen(tool) opens it by the id of the call that sent it.
export function agentRowView(sent, { opened = false, now = Date.now(), onOpen }) {
  return html`
    <button class="row compact agent-row" data-kind=${sent.kind} data-state=${sent.state} data-verdict=${sent.verdict || nothing}
      aria-current=${opened ? 'true' : 'false'} @click=${() => onOpen(sent.tool)}>
      <span class="agent-row-kind">${kindLabel(sent)}</span>
      <span class="agent-row-what">${sent.description}</span>
      <span class="agent-row-state">${stateWords(sent, now)}</span>
    </button>
  `;
}
