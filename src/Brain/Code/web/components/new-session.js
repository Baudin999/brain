// The new session form: an agent, offered from Claude Code's agents folder and brain unless typed, the window's
// project, the folder every session of the window runs in, and a button that starts the session. Like brain code in a
// terminal, any agent file's name works. A second button starts the immediate agent, for small changes.
//
// Component state: the agent typed, kept across redraws.
import { html } from '/lit-html.js';

const typed = { agent: 'brain' };

function submitted(event, onStart) {
  event.preventDefault();
  onStart(typed.agent.trim());
}

export function newSessionView(agents, project, { onStart }) {
  return html`
    <form class="code-new" @submit=${(event) => submitted(event, onStart)}>
      <h2 class="code-new-title">New session</h2>
      <label class="code-new-label" for="new-agent">Agent</label>
      <input id="new-agent" class="field mono" list="new-agents" .value=${typed.agent} @input=${(event) => { typed.agent = event.target.value; }}>
      <datalist id="new-agents">${agents.map((agent) => html`<option value=${agent}></option>`)}</datalist>
      <span class="code-new-label">Folder</span>
      <span class="code-new-folder mono">${project}</span>
      <div class="code-new-actions">
        <button class="button" type="submit">Start</button>
        <button class="button" type="button" @click=${() => onStart('immediate')}>Start Immediate</button>
      </div>
    </form>
  `;
}
