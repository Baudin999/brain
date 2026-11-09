// The agents panel beside the conversation: every agent a session sent, grouped into its pieces of work by the shared
// agent work, and drawn as the settings choose. As a flow, it is the shared agent flow, a diagram of blocks and lines
// from the open agent on. As a list, each agent is the shared agent row in the order it was sent, and a piece of work
// reads as one trail joined by a line: the checks, the builder, each validator with its checks, and between them the
// steps that passed between the agents, as the server outlines them. A click opens the agent's own view, a step's the
// view of the agent that wrote it, at that step. The panel is drawn from what the server says and holds no state of
// its own.
import { html, nothing } from '/lit-html.js';
import { agentRowView } from '/components/agent-row.js';
import { trailOutlineView } from '/components/trail-step.js';
import { agentFlowView } from '/components/agent-flow.js';
import { piecesOf } from '/components/agent-work.js';

// The time an item of an entry happened at; an item with none is last.
function atOf(item) {
  const at = Date.parse(item.sent ? item.sent.started : item.step.at);
  return Number.isNaN(at) ? Infinity : at;
}

// An entry as one trail: its agents and, for a builder's work, the steps of its outline, in the order they happened.
function trailOf(group) {
  const items = group.map((sent) => ({ sent }));
  for (const sent of group) {
    for (const step of sent.steps || []) items.push({ step });
  }
  items.sort((a, b) => atOf(a) - atOf(b));
  return items;
}

function itemView(item, open, now, onOpen) {
  if (item.step) return trailOutlineView(item.step, onOpen);
  return agentRowView(item.sent, { opened: item.sent.tool === open, now, onOpen });
}

function groupView(group, open, now, onOpen) {
  const items = trailOf(group);
  if (items.length === 1) return html`<li class="code-sent-one">${agentRowView(group[0], { opened: group[0].tool === open, now, onOpen })}</li>`;
  const drawn = items.map((item) => itemView(item, open, now, onOpen));
  return html`<li class="code-work" aria-label="One piece of work">${drawn}</li>`;
}

// How many agents are in each state, in words: running and finished always, stopped and failed when there are any.
function countLine(agents) {
  const counts = { running: 0, finished: 0, stopped: 0, failed: 0 };
  for (const sent of agents) {
    counts[sent.state] += 1;
  }
  const parts = [`${counts.running} running`, `${counts.finished} finished`];
  if (counts.stopped > 0) parts.push(`${counts.stopped} stopped`);
  if (counts.failed > 0) parts.push(`${counts.failed} failed`);
  return parts.join(' · ');
}

function listView(agents, open, now, onOpen) {
  return html`<ol class="code-agent-groups">${piecesOf(agents).map((group) => groupView(group, open, now, onOpen))}</ol>`;
}

// The agents a session sent, with the call id of the one open, or null; shows is 'flow' or 'list', root names the
// session's open agent the flow starts at, and onOpen(tool, step) opens an agent by its call id, at a step by its key.
export function agentListView(agents, open, { shows, root, onOpen }) {
  if (agents.length === 0) return nothing;
  const now = Date.now();
  let body;
  if (shows === 'list') {
    body = listView(agents, open, now, onOpen);
  } else {
    body = agentFlowView(agents, { root, open, now, onOpen });
  }
  return html`
    <aside class="code-agents" data-shows=${shows} aria-label="Agents the session sent">
      <header class="code-agents-head">
        <h2 class="code-agents-title">Agents</h2>
        <span class="code-agents-count">${countLine(agents)}</span>
      </header>
      ${body}
    </aside>
  `;
}
