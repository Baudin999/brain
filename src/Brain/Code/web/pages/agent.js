// The agent page: an agent a session sent, read only and live while it runs, in a view made for its kind. What leads
// is what happened, in words. A readiness check shows its verdict and its questions, as the server split its report; a
// builder what it is doing now and the trail of its work, from its report on; a validator the same trail, its rounds of
// findings and its landing report; any other agent its report. The trail carries the Navarone signals the work
// touched. The brief an agent was sent sits behind its own fold, and ids, hashes and the worktree behind the one
// details fold. The agent's whole conversation is the other side of the switch in the head. The permission requests
// the agent asked wait at the end of either side, to be answered there. Back returns to the session.
import { html, nothing } from '/lit-html.js';
import { conversationView, toEndView } from '/components/conversation.js';
import { asksView } from '/components/asks.js';
import { kindLabel, ranFor } from '/components/agent-row.js';
import { verdictView } from '/components/verdict.js';
import { proseOf, trailView, trailLead, trailDetails } from '/components/trail.js';
import { signalsView, signalsDetails } from '/components/signals.js';

// What happened, in one sentence: the agent's state, how long it ran, and what it is doing now.
function leadOf(view) {
  const sent = view.sent;
  const ran = ranFor(sent, Date.now());
  if (sent.state === 'failed') return `It failed after ${ran}.`;
  if (sent.state === 'stopped') return `It was stopped after ${ran}.`;
  if (sent.state === 'finished') return `It finished after ${ran}.`;
  if (sent.now) return `It is working, for ${ran} so far. Now: ${sent.now}`;
  return `It is working, for ${ran} so far.`;
}

function briefView(view) {
  if (!view.brief) return nothing;
  return html`
    <details class="code-brief">
      <summary class="fold">The brief it was sent</summary>
      ${proseOf(`${view.sent.tool}|brief`, view.brief)}
    </details>
  `;
}

function reportView(view, title) {
  if (!view.report) return html`<p class="code-none">No report yet.</p>`;
  return html`
    <section class="code-report">
      <h2 class="code-section-title">${title}</h2>
      ${proseOf(`${view.sent.tool}|report`, view.report)}
    </section>
  `;
}

function readyView(view) {
  if (!view.sent.verdict) return reportView(view, 'Its answer');
  let questions = null;
  if (view.questions) questions = proseOf(`${view.sent.tool}|questions`, view.questions);
  return verdictView(view.sent.verdict, questions);
}

function workView(view) {
  const trail = view.trail;
  if (!trail) return reportView(view, 'Its report');
  return html`
    <p class="code-trail-lead">${trailLead(trail)}</p>
    ${trailView(trail)}
    ${signalsView(trail.signals)}
  `;
}

function kindView(view) {
  const kind = view.sent.kind;
  if (kind === 'ready') return readyView(view);
  if (kind === 'builder' || kind === 'validator') return workView(view);
  return reportView(view, 'Its report');
}

function detailsView(view) {
  const sent = view.sent;
  let trail = nothing;
  let signals = nothing;
  if (view.trail) {
    trail = trailDetails(view.trail);
    signals = signalsDetails(view.trail.signals);
  }
  return html`
    <details class="code-details">
      <summary class="fold">Details</summary>
      <div class="code-details-body">
        <p>Agent ${sent.agent}, a ${sent.type}, sent by the call ${sent.tool}</p>
        ${sent.worktree ? html`<p>Worktree ${sent.worktree.path}</p>` : nothing}
        ${sent.started ? html`<p>Started ${sent.started}</p>` : nothing}
        ${sent.ended ? html`<p>Last wrote ${sent.ended}</p>` : nothing}
        ${trail}
        ${signals}
      </div>
    </details>
  `;
}

function happenedView(view, asks) {
  if (!view) return html`<section class="code-scroll"><p class="code-none">Reading the agent.</p>${asks}</section>`;
  return html`
    <section id="agent-view" class="code-scroll" aria-label="What the agent did">
      <div class="code-agent-body">
        ${kindView(view)}
        ${briefView(view)}
        ${detailsView(view)}
      </div>
      ${asks}
    </section>
  `;
}

function conversationSection(conversation, agents, asks, { onAgent, onCall, selected, kinds, away, onToEnd }) {
  return html`
    <div class="code-frame">
      <section id="conversation" class="code-scroll" aria-label="The agent's conversation">
        ${conversationView(conversation, agents, { onAgent, onCall, selected, kinds })}
        ${conversation.found ? nothing : html`<p class="code-none">The agent has written nothing yet.</p>`}
        ${asks}
      </section>
      ${toEndView(away, onToEnd)}
    </div>
  `;
}

function titleOf(agent, view) {
  if (!view) return agent.name || 'Agent';
  return `${kindLabel(view.sent)}: ${view.sent.description}`;
}

// The page of the agent open, { id, name, shows }, in its session, with its view from the server, or null before it
// came, its conversation and the session's agents; shows is 'happened' or 'conversation'.
export function agentPage(session, agent, view, conversation, agents, { onBack, onAgent, onCall, selected, kinds, onShow, onAnswer, answering, away, onToEnd }) {
  const asks = asksView(session, session.asks.filter((ask) => ask.call === agent.id), { onAnswer, answering });
  let body = happenedView(view, asks);
  if (agent.shows === 'conversation') body = conversationSection(conversation, agents, asks, { onAgent, onCall, selected, kinds, away, onToEnd });
  let lead = nothing;
  if (view) lead = html`<p class="code-agent-lead">${leadOf(view)}</p>`;
  return html`
    <header class="pane-head code-head">
      <div class="code-head-line">
        <h1 class="pane-title">${titleOf(agent, view)}</h1>
        <button class="button" @click=${onBack}>Back to the session</button>
      </div>
      ${lead}
      <div class="segmented" role="group" aria-label="Show">
        <button aria-pressed=${String(agent.shows === 'happened')} @click=${() => onShow('happened')}>What happened</button>
        <button aria-pressed=${String(agent.shows === 'conversation')} @click=${() => onShow('conversation')}>Conversation</button>
      </div>
    </header>
    ${body}
  `;
}
