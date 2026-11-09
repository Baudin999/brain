// The tool call: one line of a conversation's second flow, a tool the agent used and what it worked on, a whole line to
// click. A click opens the call in the information panel and the line never shows more itself. A brain query says so
// and names what it asked. The line says only a state that needs saying, running until the result came or failed.
//
// The call is brain's shape: { id, name, subject, brain, state }, state being running, done or failed.
import { html, nothing } from '/lit-html.js';

// What the line names: the tool, or for a brain query, the query and the verb it ran.
export function toolCallName(call) {
  if (call.brain) return `brain ${call.brain}`;
  return call.name;
}

// What sort of call it is, as the line and the panel say it: a brain query, or the tool's own name.
export function toolCallKind(call) {
  if (call.brain) return 'Brain query';
  return call.name;
}

function stateView(call) {
  if (call.state === 'done') return nothing;
  return html`<span class="tool-call-state">${call.state}</span>`;
}

function kindView(call) {
  if (!call.brain) return nothing;
  return html`<span class="tool-call-kind">${toolCallKind(call)}</span>`;
}

// The line of one call; selected marks the call the panel shows, and onOpen(call) opens it there.
export function toolCallView(call, { selected = false, onOpen }) {
  return html`
    <button class="tool-call" data-state=${call.state} data-kind=${call.brain ? 'brain' : 'tool'}
      aria-pressed=${selected ? 'true' : 'false'} @click=${() => onOpen(call)}>
      ${kindView(call)}
      <span class="tool-call-name">${toolCallName(call)}</span>
      <span class="tool-call-subject">${call.subject || ''}</span>
      ${stateView(call)}
    </button>
  `;
}
