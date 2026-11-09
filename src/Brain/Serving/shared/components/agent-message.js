// The agent message: words one agent sent another, as brain code's conversation draws them, and both ways in the
// Stylebook. A message from another agent, as a validator's findings reaching its builder, says who sent it; a message
// the agent sent, to another agent or as its report, hangs under the call that sent it. The words come drawn, as the
// markdown component draws them.
import { html, nothing } from '/lit-html.js';

// A message with its words drawn as prose: from names the agent that sent it, or is null for one this agent sent.
export function agentMessageView(from, prose) {
  if (from === null) return html`<div class="agent-message" data-way="sent">${prose}</div>`;
  return html`
    <div class="agent-message" data-way="from">
      <p class="agent-message-from">A message from ${from || 'another agent'}</p>
      ${prose || nothing}
    </div>
  `;
}
