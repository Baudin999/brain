// The agent list: every agent a session sent, in the order it sent them, beside the conversation, each as the shared
// agent row. A builder and the validators sent for its work sit together as one piece of work, the validators under it.
// A click opens the agent's own view. The list is drawn from what the server says and holds no state of its own.
import { html, nothing } from '/lit-html.js';
import { agentRowView } from '/components/agent-row.js';

// The pieces of work and the lone agents, in the order the first agent of each was sent: a builder heads its work,
// and a validator joins the work of the builder its brief names, or stands alone when the list holds no such builder.
function grouped(agents) {
  const builders = new Set(agents.filter((sent) => sent.kind === 'builder').map((sent) => sent.agent));
  const groups = [];
  const works = new Map();
  for (const sent of agents) {
    if (!sent.work || !builders.has(sent.work)) {
      groups.push([sent]);
      continue;
    }
    if (!works.has(sent.work)) {
      const work = [];
      works.set(sent.work, work);
      groups.push(work);
    }
    works.get(sent.work).push(sent);
  }
  return groups;
}

function groupView(group, open, now, onOpen) {
  const rows = group.map((sent) => agentRowView(sent, { opened: sent.tool === open, now, onOpen }));
  if (group.length === 1) return html`<li class="code-sent-one">${rows}</li>`;
  return html`<li class="code-work" aria-label="One piece of work">${rows}</li>`;
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

// The agents a session sent, with the call id of the one open, or null; onOpen(tool) opens an agent by its call id.
export function agentListView(agents, open, { onOpen }) {
  if (agents.length === 0) return nothing;
  const now = Date.now();
  return html`
    <aside class="code-agents" aria-label="Agents the session sent">
      <header class="code-agents-head">
        <h2 class="code-agents-title">Agents</h2>
        <span class="code-agents-count">${countLine(agents)}</span>
      </header>
      <ol class="code-agent-groups">${grouped(agents).map((group) => groupView(group, open, now, onOpen))}</ol>
    </aside>
  `;
}
