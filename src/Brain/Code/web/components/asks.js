// The asks: the permission requests of a session that wait for the user, each the shared permission request, in the
// order they came, under the conversation they happened in. onAnswer(session, ask, allow, words) answers one, and
// answering(session, ask) says whether its answer is on its way.
import { html, nothing } from '/lit-html.js';
import { permissionRequestView } from '/components/permission-request.js';

function askView(session, ask, { answering, onAnswer }) {
  return permissionRequestView(ask, {
    answering: answering(session, ask),
    onAllow: () => onAnswer(session, ask, true, ''),
    onDeny: (words) => onAnswer(session, ask, false, words),
  });
}

export function asksView(session, asks, actions) {
  if (asks.length === 0) return nothing;
  return html`<div class="code-asks">${asks.map((ask) => askView(session, ask, actions))}</div>`;
}
