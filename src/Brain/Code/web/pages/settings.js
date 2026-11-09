// The settings page of brain code's window: what the window draws of a conversation's second flow, the extra information
// the user inspects. One toggle for each kind, tool calls and brain queries; off hides that kind from the conversation
// entirely, on draws it as a line, or a run, that opens in the information panel. Both start on. Under them, how the
// agents panel beside a conversation draws the agents a session sent: as a flow, the default, or as a list. The server
// keeps every choice across restarts. A later kind, such as thinking, is one more entry of KINDS.
import { html } from '/lit-html.js';
import { fieldRow } from '/components/field-row.js';
import { toggle } from '/components/toggle.js';

const KINDS = [
  { id: 'tools', label: 'Tool calls', hint: 'Reads, edits, commands and the other tools the agent uses.' },
  { id: 'brain', label: 'Brain calls', hint: 'The agent reading the graph with brain lookup, nearest, node and walk.' },
];

// How the agents panel draws, each with its words.
const SHOWS = [
  { id: 'flow', label: 'Flow' },
  { id: 'list', label: 'List' },
];

const SHOWS_HINT = 'The flow draws each piece of work as blocks and lines from the open agent on; the list puts the agents in a column.';

function kindRow(kind, settings, onChange) {
  const on = settings[kind.id];
  const control = toggle({ on, label: kind.label, onToggle: () => onChange(kind.id, !on) });
  return fieldRow({ id: kind.id, label: kind.label, hint: kind.hint, current: false }, control);
}

function showsRow(settings, onShows) {
  const choices = SHOWS.map((shows) => html`<button aria-pressed=${String(settings.agents === shows.id)} @click=${() => onShows(shows.id)}>${shows.label}</button>`);
  const control = html`<div class="segmented" role="group" aria-label="Agents panel">${choices}</div>`;
  return fieldRow({ id: 'agents', label: 'Agents panel', hint: SHOWS_HINT, current: false }, control);
}

// The page: settings is { tools, brain, agents }, onChange(id, on) saves a kind, and onShows(shows) how the agents
// panel draws, 'flow' or 'list'.
export function settingsPage(settings, { onChange, onShows }) {
  return html`
    <header class="pane-head"><h1 class="pane-title">Settings</h1></header>
    <section class="code-settings" aria-label="What the window shows">
      <h2 class="code-section-title">The second flow</h2>
      <p class="code-settings-lead">The conversation is what you and the agent say. The tool calls and brain calls are what you inspect, and they open in the panel on the right.</p>
      ${KINDS.map((kind) => kindRow(kind, settings, onChange))}
      <h2 class="code-section-title code-settings-next">The agents</h2>
      <p class="code-settings-lead">The agents a session sent show in the panel beside its conversation.</p>
      ${showsRow(settings, onShows)}
    </section>
  `;
}
