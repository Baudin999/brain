// The session page: one session's conversation, live, under a head that names its agent, its folder and its id, and
// the composer under it, with the agents it sent beside it, drawn as the settings choose, a flow or a list. Closing stops the session's claude and everything it
// runs; while it is answering, the button says so. The processes its agent started that still run wait behind a chip
// beside the close button, which folds them out over the conversation, each with its own stop, so the head keeps its
// height as they come and go. An agent the session sent opens its own view on the agent page,
// from the list or from its row in the conversation. Every permission request of the session, its own agent's and its
// subagents', waits under the conversation, where the session stands now. The questions the session asked the user
// stand in its conversation where they were asked, and the open ones are listed over the composer, which never scrolls
// away; a click on one shows it in the conversation.
import { html, nothing } from '/lit-html.js';
import { conversationView, toEndView } from '/components/conversation.js';
import { composerView } from '/components/composer.js';
import { asksView } from '/components/asks.js';
import { openQuestionsView } from '/components/open-questions.js';
import { sessionQuestionView } from '/components/session-question.js';
import { agentListView } from '/components/agent-list.js';
import { processFoldView } from '/components/process-fold.js';

export function closeLabel(session) {
  if (session.alive && (session.working || session.running.length > 0)) return 'Stop and close';
  return 'Close';
}

function problemView(session) {
  if (!session.problem) return nothing;
  return html`<p class="code-problem" role="status">${session.problem}</p>`;
}

function foldView(session, { folded, onFold, onEndProcess, ending }) {
  const shown = { work: session.running, open: folded(session), now: Date.now(), stopping: (job) => ending(session, job) };
  return processFoldView(shown, { onToggle: () => onFold(session), onStop: (job) => onEndProcess(session, job) });
}

// The session in view, its conversation, and the agents it sent; onAgent(tool) opens an agent by the id of its call.
export function sessionPage(session, conversation, agents, actions) {
  const {
    onAgent, onCall, selected, kinds, onSend, onInterrupt, stopping, confirm, onConfirm, onKeep, onClose, onAnswer, answering,
    onShowQuestion, away, onToEnd, onExplore, onChange, shows,
  } = actions;
  const questionView = (asked) => sessionQuestionView(session, asked, actions);
  return html`
    <header class="pane-head code-head">
      <div class="code-head-line">
        <h1 class="pane-title">${session.agent}</h1>
        <div class="code-head-actions">
          ${foldView(session, actions)}
          <button class="button no" @click=${() => onClose(session)}>${closeLabel(session)}</button>
        </div>
      </div>
      <p class="code-head-meta">${session.cwd} · ${session.id}</p>
    </header>
    <div class="code-body">
      <div class="code-talk">
        <div class="code-frame">
          <section id="conversation" class="code-scroll" aria-label="Conversation">
            ${conversationView(conversation, agents, { onAgent, onCall, selected, kinds, questions: session.questions, questionView })}
            ${conversation.found || session.problem ? nothing : html`<p class="code-none">Nothing said yet.</p>`}
            ${asksView(session, session.asks, { onAnswer, answering })}
            ${problemView(session)}
          </section>
          ${toEndView(away, onToEnd)}
        </div>
        ${openQuestionsView(session, { onShow: onShowQuestion })}
        ${composerView(session, { onSend, onInterrupt, stopping, confirm, onConfirm, onKeep, onExplore, onChange })}
      </div>
      ${agentListView(agents, null, { shows, root: session.agent, onOpen: onAgent })}
    </div>
  `;
}
