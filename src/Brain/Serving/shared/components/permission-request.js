// The permission request: a tool call of an agent that waits for the user's yes, drawn where it happened in brain
// code's window and in its states in the Stylebook. It names the tool and, when a subagent asked, which one, and shows
// what the call will do in a readable form: a Bash command as code, a file edit as the path and its change, a file
// written as the path and what it holds, and any other tool as its input. Allow lets the call run; Deny refuses it,
// with the words typed beside it, if any, as the reason the agent reads. While the answer is on its way the request is
// answering and takes no second one.
//
// The request is the server's shape of it: { id, tool, input, agent, agentId }, agent and agentId only for a
// subagent's. Component state: the words typed for each request, by its id, kept across redraws.
import { html, nothing } from '/lit-html.js';

const typed = new Map();

// What a value is as text: a string as it is, anything else as JSON.
function asText(value) {
  if (typeof value === 'string') return value;
  return JSON.stringify(value, null, 2);
}

// Lines of text as one diff run in a git state, added or deleted, or nothing for no text.
function run(state, text) {
  if (!text) return nothing;
  let lines = text;
  if (!lines.endsWith('\n')) lines += '\n';
  return html`<span class="git-run git-change" data-change=${state}>${lines}</span>`;
}

function pathView(path) {
  return html`<p class="permission-request-path">${path}</p>`;
}

function noteView(note) {
  if (!note) return nothing;
  return html`<p class="permission-request-note">${note}</p>`;
}

function changeView(edit) {
  return html`<pre class="permission-request-code">${run('deleted', edit.old_string)}${run('added', edit.new_string)}</pre>`;
}

function bashView(input) {
  return html`${noteView(input.description)}<pre class="permission-request-code">${input.command}</pre>`;
}

function editView(input) {
  let note = '';
  if (input.replace_all) note = 'every occurrence';
  return html`${pathView(input.file_path)}${noteView(note)}${changeView(input)}`;
}

function multiEditView(input) {
  const edits = input.edits || [];
  return html`${pathView(input.file_path)}${edits.map((edit) => changeView(edit))}`;
}

function writeView(input) {
  return html`${pathView(input.file_path)}<pre class="permission-request-code">${run('added', input.content)}</pre>`;
}

function inputView(input) {
  return html`<pre class="permission-request-code">${asText(input)}</pre>`;
}

// What the call will do, in the form its tool reads best in.
function whatView(tool, input) {
  if (!input || typeof input !== 'object') return inputView(input);
  if (tool === 'Bash' && typeof input.command === 'string') return bashView(input);
  if (tool === 'Edit' && typeof input.file_path === 'string') return editView(input);
  if (tool === 'MultiEdit' && typeof input.file_path === 'string') return multiEditView(input);
  if (tool === 'Write' && typeof input.file_path === 'string') return writeView(input);
  return inputView(input);
}

function whoView(ask) {
  if (!ask.agent) return nothing;
  return html`<span class="permission-request-who" title=${ask.agentId || ''}>asked by the ${ask.agent} subagent</span>`;
}

function denied(ask, onDeny) {
  const words = (typed.get(ask.id) || '').trim();
  typed.delete(ask.id);
  onDeny(words);
}

function allowed(ask, onAllow) {
  typed.delete(ask.id);
  onAllow();
}

// A request that waits; answering marks the one whose answer is on its way. onAllow() and onDeny(words) answer it.
export function permissionRequestView(ask, { answering = false, onAllow, onDeny }) {
  let state = 'waiting';
  if (answering) state = 'answering';
  return html`
    <article class="permission-request" data-state=${state} aria-label="Permission request">
      <header class="permission-request-head">
        <span>Allow <span class="permission-request-tool">${ask.tool}</span>?</span>
        ${whoView(ask)}
      </header>
      ${whatView(ask.tool, ask.input)}
      <div class="permission-request-answer">
        <input class="field permission-request-words" placeholder="Why not, for the agent, if you deny" ?disabled=${answering}
          .value=${typed.get(ask.id) || ''} @input=${(event) => typed.set(ask.id, event.target.value)}>
        <button class="button yes" ?disabled=${answering} @click=${() => allowed(ask, onAllow)}>Allow</button>
        <button class="button no" ?disabled=${answering} @click=${() => denied(ask, onDeny)}>Deny</button>
      </div>
    </article>
  `;
}
