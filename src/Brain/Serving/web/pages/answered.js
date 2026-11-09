// Answered: every write the user answered in the last two days, yes, no and no with words, in its own inbox, the newest
// first. Each opens exactly as it was asked, with the answer and when it was given. Undo on a yes takes back exactly what
// the write did, and undo on a no puts the write before the user again. The click is the user's yes: brain serve undoes it
// through the pending store's own code, never by writing a file itself. An undone yes commits the files it changed, and
// the toast says what it committed or why it could not. A yes whose nodes changed since shows what changed, and its undo
// refuses. The list follows the project focus, as node review does.
//
// The page renders with lit-html from answeredPage, its state: a handler changes it and renders again. A write's nodes
// are drawn by node review's own changeView and diff, so an answered write reads exactly as it did when it was asked.
// Keys: j and k step through the list, u undoes the answer in view.
import { html, nothing } from '/lit-html.js';
import { app } from '/seam.js';

const { state, call, toast, plainKey, registerInbox, showInbox, showReview, renderQueue, renderMain, templateNode, inFocus, graphChips, title, ago, changeView, diff, loadPending, SETTLE_MS } = app;

const ANSWERED = ':answered';

// list is every answered write brain serve keeps, the newest first. selected is the write in view by id, and selectedAt
// when it came into view, so an undo waits until it was read. busy holds while an undo is in flight.
const answeredPage = {
  list: [],
  selected: null,
  selectedAt: 0,
  busy: false,
};

const VERDICTS = { yes: 'You said yes', no: 'You said no', refused: 'You said yes, and nothing landed' };

async function loadAnswered() {
  const { ok, body } = await call('/api/answered');
  if (!ok) {
    toast(body.error);
    return;
  }
  answeredPage.list = body;
  redraw();
}

function redraw() {
  renderQueue();
  if (state.view === 'review' && state.inbox === ANSWERED) renderMain();
}

// The answered writes in the project in focus.
function visibleAnswered() {
  return answeredPage.list.filter(inFocus);
}

// The write in view: the one selected while it is in focus, else the newest.
function currentAnswered() {
  const list = visibleAnswered();
  return list.find((item) => item.id === answeredPage.selected) || list[0] || null;
}

function verdictOf(item) {
  if (item.verdict === 'no' && item.reason) return 'You said no, and what to change';
  return VERDICTS[item.verdict] || item.verdict;
}

function answeredSub(item) {
  const undone = item.undoneAt ? ', undone' : '';
  return `${item.verdict}${undone}, ${ago(item.answeredAt)}`;
}

function undoable(item) {
  return !item.undoneAt && (item.verdict === 'yes' || item.verdict === 'no');
}

function selectAnswered(id) {
  if (answeredPage.selected !== id) {
    answeredPage.selected = id;
    answeredPage.selectedAt = performance.now();
  }
  if (state.inbox !== ANSWERED) {
    showInbox(ANSWERED);
    return;
  }
  showReview();
}

function stepAnswered(delta) {
  const list = visibleAnswered();
  if (!list.length) return;
  const at = list.indexOf(currentAnswered());
  selectAnswered(list[Math.max(0, Math.min(list.length - 1, at + delta))].id);
}

async function undo() {
  const item = currentAnswered();
  if (!item || !undoable(item) || answeredPage.busy || state.view !== 'review' || state.inbox !== ANSWERED) return;
  if (performance.now() - answeredPage.selectedAt < SETTLE_MS) {
    toast('This answer just came into view. Read it, then undo.');
    return;
  }
  answeredPage.busy = true;
  redraw();
  try {
    const { ok, body } = await call('/api/undo', { method: 'POST', body: JSON.stringify({ id: item.id, digest: item.digest }) });
    if (!ok) toast(body.error);
    else if (body.undid === 'yes') toast(undoneYes(item, body));
    else toast('The write waits for your answer again in node review.');
  } finally {
    answeredPage.busy = false;
    await loadAnswered();
    await loadPending();
  }
}

// What an undone yes says: what it took back, and what it committed or why it could not.
function undoneYes(item, body) {
  const undid = `Undid ${item.changes.map((change) => change.ref).join(', ')}.`;
  if (body.notCommitted) return `${undid} It is not committed: ${body.notCommitted}`;
  if (body.commits.length) return `${undid} Committed as ${body.commits.map((commit) => commit.slice(0, 7)).join(', ')}.`;
  return `${undid} The last commit already holds it, so nothing was committed.`;
}

// ---- The queue column ----

function answeredList() {
  const list = visibleAnswered();
  if (!list.length) {
    const empty = answeredPage.list.length ? 'Nothing answered in this project.' : 'Nothing answered in the last two days.';
    return templateNode(html`<p class="queue-empty">${empty}</p>`);
  }
  const shown = currentAnswered();
  return templateNode(html`
    <ol>
      ${list.map((item) => html`
        <li class="queue-row">
          <button class="row queue-item" aria-current=${String(state.view === 'review' && item === shown)} @click=${() => selectAnswered(item.id)}>
            <span class="queue-title">${title(item)}</span>
            <span class="queue-sub">${answeredSub(item)}</span>
          </button>
          ${graphChips(item)}
        </li>`)}
    </ol>`);
}

// ---- The main pane ----

function answeredView() {
  const item = currentAnswered();
  if (!item) {
    return html`
      <div class="quiet">
        <h2>Nothing answered</h2>
        <p>Every write you answer shows here for two days, exactly as it was asked, so you can undo a mistaken answer.</p>
      </div>`;
  }
  return html`
    <div class="page">
      <p class="doc-meta">
        ${graphChips(item)}
        <span>${verdictOf(item)}</span>
        <span title=${new Date(item.answeredAt).toLocaleString()}>${ago(item.answeredAt)}</span>
        ${item.undoneAt ? html`<span title=${new Date(item.undoneAt).toLocaleString()}>undone ${ago(item.undoneAt)}</span>` : nothing}
      </p>
      ${item.reason ? html`<section class="doc-section"><h2>Your words</h2><p>${item.reason}</p></section>` : nothing}
      ${item.refusal ? html`<p class="stale">${item.refusal}</p>` : nothing}
      <ul class="says" aria-label="What the write does">${item.lines.map((line) => html`<li>${line}</li>`)}</ul>
      ${sinceView(item)}
      ${item.changes.map((change, index) => changeView(item, change, index))}
      ${undoBar(item)}
    </div>`;
}

// The nodes that read otherwise than the yes left them, each as a diff from then to now. An undo refuses while any does.
function sinceView(item) {
  if (!item.since.length) return nothing;
  return html`
    <section class="change">
      <p class="stale">A node changed since your yes, so an undo changes nothing. What changed since:</p>
      ${item.since.map((since) => html`
        <div class="doc-section">
          <div class="change-head"><span class="change-ref">${since.ref}</span></div>
          ${diff(since.then, since.now)}
        </div>`)}
    </section>`;
}

function undoBar(item) {
  if (!undoable(item)) return nothing;
  const label = item.verdict === 'yes' ? 'Undo, take it back out' : 'Undo, ask me again';
  return html`
    <div class="answer">
      <div class="answer-bar">
        <button class="button" ?disabled=${answeredPage.busy} @click=${undo}>${label}<kbd>U</kbd></button>
      </div>
    </div>`;
}

// ---- Keys and commands ----

function answeredKeys(event) {
  if (state.inbox !== ANSWERED || state.view !== 'review' || !plainKey(event)) return false;
  const actions = { j: () => stepAnswered(1), k: () => stepAnswered(-1), u: undo };
  const action = actions[event.key.toLowerCase()];
  if (!action) return false;
  event.preventDefault();
  action();
  return true;
}

function answeredCommands() {
  if (state.inbox !== ANSWERED) return [];
  const item = state.view === 'review' ? currentAnswered() : null;
  const several = visibleAnswered().length > 1;
  const list = [
    item && undoable(item) && { name: item.verdict === 'yes' ? 'Undo this yes' : 'Undo this no, and ask again', keys: ['U'], run: undo },
    several && { name: 'Next answered write', keys: ['J'], run: () => stepAnswered(1) },
    several && { name: 'Previous answered write', keys: ['K'], run: () => stepAnswered(-1) },
  ].filter(Boolean);
  return [...list, ...visibleAnswered().map((one) => ({ name: title(one), group: answeredSub(one), keys: [], run: () => selectAnswered(one.id) }))];
}

registerInbox({
  name: ANSWERED,
  label: 'Answered',
  ownSide: true,
  // An answered write is a record to look back on, not work that waits for the user.
  count: () => 0,
  start: loadAnswered,
  changed: loadAnswered,
  shown: () => {
    answeredPage.selectedAt = performance.now();
  },
  list: answeredList,
  view: answeredView,
  keys: answeredKeys,
  commands: answeredCommands,
});
