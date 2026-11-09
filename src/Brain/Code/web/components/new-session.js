// The new session form: an agent, offered from Claude Code's agents folder and brain unless typed, and a folder, the
// one the session in view runs in unless typed, and a button that starts the session. Like brain code in a terminal,
// any agent file's name works. A second button starts the immediate agent, for small changes, in the same folder.
//
// Component state: what was typed in the two fields, kept across redraws; a folder not typed follows the view.
import { html } from '/lit-html.js';

const typed = { agent: 'brain', cwd: null };

function submitted(event, folder, onStart) {
  event.preventDefault();
  onStart(typed.agent.trim(), folder.trim());
}

export function newSessionView(agents, folder, { onStart }) {
  let shown = folder;
  if (typed.cwd !== null) shown = typed.cwd;
  return html`
    <form class="code-new" @submit=${(event) => submitted(event, shown, onStart)}>
      <h2 class="code-new-title">New session</h2>
      <label class="code-new-label" for="new-agent">Agent</label>
      <input id="new-agent" class="field mono" list="new-agents" .value=${typed.agent} @input=${(event) => { typed.agent = event.target.value; }}>
      <datalist id="new-agents">${agents.map((agent) => html`<option value=${agent}></option>`)}</datalist>
      <label class="code-new-label" for="new-cwd">Folder</label>
      <input id="new-cwd" class="field mono" .value=${shown} @input=${(event) => { typed.cwd = event.target.value; }}>
      <div class="code-new-actions">
        <button class="button" type="submit">Start</button>
        <button class="button" type="button" @click=${() => onStart('immediate', shown.trim())}>Start Immediate</button>
      </div>
    </form>
  `;
}
