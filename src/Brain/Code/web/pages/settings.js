// The settings page of brain code's window: what the window draws of a conversation's second flow, the extra information
// the user inspects. One toggle for each kind, tool calls and brain queries; off hides that kind from the conversation
// entirely, on draws it as a line, or a run, that opens in the information panel. Both start on, and the server keeps
// the choice across restarts. A later kind, such as thinking, is one more entry of KINDS.
import { html } from '/lit-html.js';
import { fieldRow } from '/components/field-row.js';
import { toggle } from '/components/toggle.js';

const KINDS = [
  { id: 'tools', label: 'Tool calls', hint: 'Reads, edits, commands and the other tools the agent uses.' },
  { id: 'brain', label: 'Brain calls', hint: 'The agent reading the graph with brain lookup, nearest, node and walk.' },
];

function kindRow(kind, settings, onChange) {
  const on = settings[kind.id];
  const control = toggle({ on, label: kind.label, onToggle: () => onChange(kind.id, !on) });
  return fieldRow({ id: kind.id, label: kind.label, hint: kind.hint, current: false }, control);
}

// The page: settings is { tools, brain }, and onChange(id, on) saves one.
export function settingsPage(settings, { onChange }) {
  return html`
    <header class="pane-head"><h1 class="pane-title">Settings</h1></header>
    <section class="code-settings" aria-label="What the conversation shows">
      <h2 class="code-section-title">The second flow</h2>
      <p class="code-settings-lead">The conversation is what you and the agent say. The tool calls and brain calls are what you inspect, and they open in the panel on the right.</p>
      ${KINDS.map((kind) => kindRow(kind, settings, onChange))}
    </section>
  `;
}
