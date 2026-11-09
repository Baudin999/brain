// The agents panel beside the conversation: every agent a session sent, drawn as the settings choose, as the shared
// agent flow, a diagram of blocks and lines from the open agent on, or as the list.
//
// The agent list: every agent a session sent, in the order it sent them, each as the shared agent row. A piece of work reads as one trail joined by a line: the checks, the builder, each validator with its
// checks, and between them the steps that passed between the agents, as the server outlines them. A click opens the
// agent's own view, a step's the view of the agent that wrote it, at that step. The list is drawn from what the server
// says and holds no state of its own.
import { html, nothing } from '/lit-html.js';
import { agentRowView } from '/components/agent-row.js';
import { trailOutlineView } from '/components/trail-step.js';
import { agentFlowView } from '/components/agent-flow.js';

// The entries of the list, in the order the first agent of each was sent. A builder heads one piece of work, which its
// validators join; a readiness check joins the entry of the agent the server says it checked, and stands alone when
// none was sent after it. Any other agent is an entry of its own.
// The key of the entry an agent is in: the piece of work of its builder, the entry of the agent a check checked, or its own.
function keyOf(sent, builders, byTool) {
  if (sent.kind === 'ready' && byTool.has(sent.attached)) return keyOf(byTool.get(sent.attached), builders, byTool);
  if (sent.work && builders.has(sent.work)) return `work|${sent.work}`;
  return sent.tool;
}

function grouped(agents) {
  const builders = new Set(agents.filter((sent) => sent.kind === 'builder').map((sent) => sent.agent));
  const byTool = new Map(agents.map((sent) => [sent.tool, sent]));
  const groups = new Map();
  for (const sent of agents) {
    const key = keyOf(sent, builders, byTool);
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(sent);
  }
  return [...groups.values()];
}

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
  return html`<ol class="code-agent-groups">${grouped(agents).map((group) => groupView(group, open, now, onOpen))}</ol>`;
}

// The agents a session sent, with the call id of the one open, or null; shows is 'flow' or 'list', root names the
// session's open agent the flow starts at, and onOpen(tool, step) opens an agent by its call id, at a step by its key.
export function agentListView(agents, open, { shows, root, onOpen }) {
  if (agents.length === 0) return nothing;
  const now = Date.now();
  let body = agentFlowView(agents, { root, open, now, onOpen });
  if (shows === 'list') body = listView(agents, open, now, onOpen);
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
