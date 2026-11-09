// Navarone: one tab for the code-review tool, hosting the navarone source of sources.tsv. It shows three flows.
//
// Escalations are the questions Navarone's disputes command lists, where the builder and the validator disagree and
// only the user decides. They open as the bench questions.js draws, one question on one screen, and the tab's count in
// the strip is how many wait, so the strip still means what waits for the user. Signals are every problem Navarone
// raised, read from its event log through brain serve, in the project in focus and folded by method: a row is a method
// in the code's own words with how many problems it has, and a search box above narrows the rows by method name as the
// user types. A method opens to all its problems, each leading with where its story stands, its check small and last,
// and a problem opens its story top to bottom, the code fetched only then. Nothing but the user opens, selects or
// scrolls to a method. The lens is a modal over the tab, opened on any problem or escalation that names a method: a
// view per button down its left side, the code in the middle with every call a mark that makes that method the
// subject, a trail back, and the window's chat to talk it over. Esc closes it where the user was.
//
// brain reads Navarone's commands and writes none of its files. The page renders with lit-html from its state, the
// navarone object below: a handler changes it and renders again. Everything is in agent work, not on master, and the
// tab says so.
import { html, nothing, render } from '/lit-html.js';
import { app, offer } from '/seam.js';

const { state, $, call, toast, plainKey, renderMain, renderQueue, showInbox, inFocus, graphChips, ago, hostSource, templateNode, currentQuestion, visibleQuestions, inboxRow, blocksView, indexed, plainCode, changeMarks, isOpen, summonChat, openContextMenu, contextMenuKey, registered } = app;

const NAVARONE = 'navarone';
const ESCALATIONS = 'escalations';
const SIGNALS = 'signals';
const NO_BRANCH = 'no branch';
const SEARCH_LABEL = 'Search the methods';
// A story's code has no file name, and Navarone reads C# only.
const NAVARONE_LANGUAGE = 'csharp';
const NOT_ON_MASTER = 'In agent work, not on master: Navarone logs in a worktree before any commit, and nothing yet links a fix to master.';

// The lens views Navarone builds today, one button each, in the order they sit down the lens's left side.
const VIEWS = [
  { label: 'Class', view: 'class:short', key: '1' },
  { label: 'Callers', view: 'callers', key: '2' },
  { label: 'Calls', view: 'expand:1', key: '3' },
];

// What each step of a problem's story is called where it heads its part.
const STEP_HEADS = {
  raised: 'Navarone raised it',
  fixed: 'The builder fixed it',
  resolved: 'The builder kept the code, with a reason',
  'refused-fix': 'The validator refused the fix',
  'refused-reason': 'The validator refused the reason',
  accepted: 'The validator accepted the answer',
  escalated: 'Escalated to you',
  settled: 'You answered',
};

// flow is the flow in view. problems is every problem brain serve last listed, null until it has, and error why it
// could not. search is what the user typed to find a method and method the symbol of the method open. cursor is the row
// the keys are on: a method's symbol in the methods, a problem's key inside an open method. story is the problem whose
// story is open, by key. codes holds the code under each hash a story asked for: its text, or an error. lens is the
// open lens, null while it is shut: the problem it opened on, its subject and view, the trail of subjects before it,
// what Navarone printed, an error, whether it runs, the run that answers last and what had the focus before it opened.
const navarone = {
  flow: ESCALATIONS,
  problems: null,
  error: null,
  cursor: null,
  search: '',
  method: null,
  story: null,
  codes: new Map(),
  lens: null,
};

const host = document.createElement('div');

function keyOf(problem) {
  return `${problem.worktree}\u0000${problem.id}`;
}

async function loadProblems() {
  const { ok, body } = await call('/api/navarone/problems');
  navarone.error = ok ? null : body.error;
  if (ok) navarone.problems = body;
  redraw();
}

function redraw() {
  if (state.inbox !== NAVARONE) return;
  renderQueue();
  if (state.view === 'review') renderMain();
  renderLens();
}

function source() {
  return state.sources.find((one) => one.name === NAVARONE) || null;
}

// True while an escalation is open as the bench, which then holds the main pane.
function escalationOpen() {
  const navaroneSource = source();
  return navarone.flow === ESCALATIONS && Boolean(navaroneSource) && isOpen(navaroneSource);
}

function signals() {
  return (navarone.problems || []).filter(inFocus);
}

function escalations() {
  const navaroneSource = source();
  if (!navaroneSource || navaroneSource.error) return [];
  return visibleQuestions(navaroneSource);
}

function problemByKey(key) {
  return (navarone.problems || []).find((problem) => keyOf(problem) === key) || null;
}

// The problem an escalation is about: Navarone names a question by the problem's id and a dash, in the problem's worktree.
function problemOfQuestion(q) {
  if (!q) return null;
  return (navarone.problems || []).find((problem) => problem.worktree === q.repo && q.id.startsWith(`${problem.id}-`)) || null;
}

// The problem the keys and the lens act on: the story open, the escalation open or under the cursor, or the signal
// row, which on a method row is the first of its problems.
function problemInView() {
  if (navarone.story) return problemByKey(navarone.story);
  if (navarone.flow === ESCALATIONS) {
    const navaroneSource = source();
    return navaroneSource ? problemOfQuestion(currentQuestion(navaroneSource)) : null;
  }
  return currentSignal();
}

// ---- The flows ----

function setFlow(flow) {
  navarone.flow = flow;
  navarone.story = null;
  state.opened[NAVARONE] = false;
  redraw();
  $('main').focus();
}

function flowSwitch() {
  const flows = [
    { flow: ESCALATIONS, label: 'Escalations', count: escalations().length, key: '1' },
    { flow: SIGNALS, label: 'Signals', count: signals().length, key: '2' },
  ];
  return html`
    <div class="segmented navarone-flows" role="group" aria-label="Navarone's flows">
      ${flows.map((one) => html`
        <button aria-pressed=${String(navarone.flow === one.flow)} @click=${() => setFlow(one.flow)}>
          ${one.label} <span class="navarone-flow-count">${one.count}</span><kbd>${one.key}</kbd>
        </button>`)}
    </div>`;
}

function pageView() {
  const story = problemByKey(navarone.story);
  if (story) return storyView(story);
  const say = navarone.flow === ESCALATIONS
    ? 'Where the builder and the validator disagree. Only you decide these.'
    : 'Every problem Navarone raised in agent work, and where its story stands.';
  return html`
    <div class="inbox-page navarone-page">
      <header class="pane-head flush inbox-head">
        <h1 class="pane-title">Navarone</h1>
        <p class="navarone-note">${NOT_ON_MASTER}</p>
      </header>
      ${flowSwitch()}
      <p class="navarone-say">${say}</p>
      <p class="keys">Move <kbd>j</kbd><kbd>k</kbd>  Open <kbd>Enter</kbd>  Lens <kbd>Alt</kbd><kbd>L</kbd>  Talk <kbd>Alt</kbd><kbd>A</kbd></p>
      ${navarone.flow === ESCALATIONS ? escalationsView() : signalsView()}
    </div>`;
}

function escalationsView() {
  const navaroneSource = source();
  if (navaroneSource?.error) {
    return html`<p class="quiet-error">Navarone's disputes command is failing: ${navaroneSource.error}</p>`;
  }
  const list = escalations();
  if (!list.length) {
    return html`<p class="navarone-none">Nothing waits for you. When the builder and the validator disagree on a problem, it waits here for your judgement.</p>`;
  }
  const picked = currentQuestion(navaroneSource);
  return html`<ol class="inbox-rows">${list.map((q) => html`<li>${inboxRow(navaroneSource, q, q === picked)}</li>`)}</ol>`;
}

// The signals folded by method, in the order their methods first appear.
function methodsOf(list) {
  const methods = new Map();
  for (const problem of list) {
    if (!methods.has(problem.symbol)) methods.set(problem.symbol, { symbol: problem.symbol, name: problem.name, problems: [] });
    methods.get(problem.symbol).problems.push(problem);
  }
  return [...methods.values()];
}

// The methods whose name the search matches.
function foundMethods() {
  const needle = navarone.search.trim().toLowerCase();
  return methodsOf(signals()).filter((method) => method.name.toLowerCase().includes(needle));
}

// The method open with all its problems, none when it has gone or the Signals flow is not the one in view.
function openMethod() {
  if (navarone.flow !== SIGNALS) return null;
  if (!navarone.method) return null;
  return methodsOf(signals()).find((method) => method.symbol === navarone.method) || null;
}

// The rows the keys move over: the open method's problems, or the methods the search finds.
function rows() {
  const open = openMethod();
  if (open) {
    return open.problems.map((problem) => ({ key: keyOf(problem), problem }));
  }
  return foundMethods().map((method) => ({ key: method.symbol, method }));
}

function currentRow(list = rows()) {
  return list.find((row) => row.key === navarone.cursor) || list[0] || null;
}

// The problem the keys act on from the signals: the one under the cursor in an open method, else the first of the method under it.
function currentSignal() {
  const row = currentRow();
  if (!row) return null;
  return row.problem || row.method.problems[0];
}

function countOf(problems) {
  return `${problems.length} ${problems.length === 1 ? 'problem' : 'problems'}`;
}

function onSearch(event) {
  navarone.search = event.target.value;
  navarone.cursor = null;
  redraw();
}

// The keys of the search box, as the graph search has them: Esc leaves the box and keeps the text, the arrows move over
// the rows and Enter opens the row under the cursor. Every letter stays text.
function onSearchKey(event) {
  if (event.key === 'Escape') {
    event.preventDefault();
    event.target.blur();
    $('main').focus();
  } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    stepSignal(event.key === 'ArrowDown' ? 1 : -1);
  } else if (event.key === 'Enter') {
    event.preventDefault();
    openCurrentSignal();
  }
}

function searchBox() {
  return html`<input class="field search" type="search" placeholder=${SEARCH_LABEL} aria-label=${SEARCH_LABEL} spellcheck="false" autocomplete="off" .value=${navarone.search} @input=${onSearch} @keydown=${onSearchKey}>`;
}

function signalsView() {
  if (navarone.error) return html`<p class="quiet-error">Navarone's event log could not be read: ${navarone.error}</p>`;
  if (!navarone.problems) return html`<p class="navarone-none">Reading Navarone's event log.</p>`;
  const open = openMethod();
  if (open) return methodView(open);
  if (!signals().length) {
    const none = navarone.problems.length ? 'Navarone raised no problem in this project.' : 'Navarone has raised no problem yet.';
    return html`<p class="navarone-none">${none}</p>`;
  }
  const list = foundMethods();
  return html`${searchBox()}${methodList(list, currentRow())}`;
}

// One template around the search box, so the input lives on while the matches come and go under it.
function methodList(list, picked) {
  if (!list.length) {
    return html`<p class="navarone-none">No method matches the search.</p>`;
  }
  return html`<ol class="inbox-rows">${list.map((method) => html`<li>${methodRow(method, method.symbol === picked?.key)}</li>`)}</ol>`;
}

function methodRow(method, current) {
  const branches = [...new Set(method.problems.map((problem) => problem.branch ?? NO_BRANCH))].join(', ');
  return html`
    <button class="row inbox-row" aria-current=${String(current)} @click=${() => showMethod(method.symbol)}>
      <span class="inbox-title">${method.name}</span>
      <span class="inbox-small">${branches}</span>
      <span class="inbox-id">${countOf(method.problems)}</span>
    </button>`;
}

// One method with all its problems together, for the user to fix in one change.
function methodView(method) {
  const picked = currentRow();
  return html`
    <section class="inbox-group">
      <button class="link subtle back" @click=${closeMethod}><kbd>Esc</kbd> Back to the methods</button>
      <h2 class="inbox-group-head">
        <span class="inbox-group-name">${method.name}</span>
        <span class="inbox-group-count">${countOf(method.problems)}</span>
      </h2>
      <ol class="inbox-rows">${method.problems.map((problem) => html`<li>${signalRow(problem, keyOf(problem) === picked?.key)}</li>`)}</ol>
    </section>`;
}

function signalRow(problem, current) {
  return html`
    <button class="row inbox-row" aria-current=${String(current)} @click=${() => openStory(problem)}>
      <span class="inbox-title">${problem.state}</span>
      <span class="inbox-small">${graphChips(problem)}<span>${problem.branch ?? NO_BRANCH}</span></span>
      <span class="inbox-id">${problem.check}</span>
    </button>`;
}

function showMethod(symbol) {
  navarone.method = symbol;
  navarone.cursor = null;
  redraw();
  $('main').scrollTop = 0;
  $('main').focus();
}

function closeMethod() {
  navarone.cursor = navarone.method;
  navarone.method = null;
  redraw();
  $('main').focus();
}

function openCurrentSignal() {
  const row = currentRow();
  if (!row) return;
  if (row.problem) openStory(row.problem);
  else showMethod(row.method.symbol);
}

function stepSignal(delta) {
  const list = rows();
  if (!list.length) return;
  const at = list.indexOf(currentRow(list));
  navarone.cursor = list[Math.max(0, Math.min(list.length - 1, at + delta))].key;
  redraw();
  document.querySelector('.navarone-page .inbox-row[aria-current="true"]')?.scrollIntoView({ block: 'nearest' });
}

// ---- A problem's story, top to bottom ----

function openStory(problem) {
  navarone.flow = SIGNALS;
  navarone.method = problem.symbol;
  navarone.cursor = keyOf(problem);
  navarone.story = keyOf(problem);
  for (const step of problem.story) {
    fetchCode(step.before);
    fetchCode(step.after);
  }
  redraw();
  $('main').scrollTop = 0;
  $('main').focus();
}

function closeStory() {
  navarone.story = null;
  redraw();
  $('main').focus();
}

// The code under a hash, asked for once: the story draws it when it comes.
async function fetchCode(hash) {
  if (!hash || navarone.codes.has(hash)) return;
  navarone.codes.set(hash, null);
  const { ok, body } = await call(`/api/navarone/code?hash=${encodeURIComponent(hash)}`);
  navarone.codes.set(hash, ok ? { code: body.code } : { error: body.error });
  redraw();
}

// Opens a problem's story from another tab, as Evidence does for each of its Navarone cases.
function openNavaroneStory(key) {
  if (!registered.some((inbox) => inbox.name === NAVARONE)) {
    toast('sources.tsv has no navarone line, so there is no Navarone tab.');
    return;
  }
  showInbox(NAVARONE);
  const problem = (navarone.problems || []).find((one) => one.worktree === key.worktree && one.id === key.id);
  if (problem) openStory(problem);
  else toast('Navarone lists no such problem now.');
}

function storyView(problem) {
  return html`
    <div class="page navarone-story">
      <button class="link subtle back" @click=${closeStory}><kbd>Esc</kbd> Back to the method</button>
      <header class="navarone-story-head">
        <h1 class="doc-title">${problem.name}</h1>
        <p class="doc-meta">
          <span class="subject">${problem.state}</span>
          <span>on <code>${problem.branch ?? NO_BRANCH}</code></span>
          ${graphChips(problem)}
          <span>${problem.check}</span>
        </p>
        <p class="navarone-note">${NOT_ON_MASTER}</p>
        ${lensButton(problem)}
      </header>
      <ol class="navarone-steps">${problem.story.map((step) => stepView(problem, step))}</ol>
      <details class="navarone-details">
        <summary class="link subtle">Details</summary>
        <p class="doc-meta"><span>id ${problem.id}</span><span>${problem.symbol}</span><span>${problem.worktree}</span></p>
      </details>
    </div>`;
}

function lensButton(problem) {
  if (!problem?.method) return nothing;
  return html`<button class="button" @click=${() => openLens(problem)}>Open the lens <kbd>Alt</kbd><kbd>L</kbd></button>`;
}

function stepView(problem, step) {
  return html`
    <li class="navarone-step">
      <h2 class="block-caption">${STEP_HEADS[step.type] || step.type}<span class="block-path" title=${new Date(step.at).toLocaleString()}>${ago(step.at)}</span></h2>
      ${stepBody(problem, step)}
    </li>`;
}

function stepBody(problem, step) {
  switch (step.type) {
    case 'raised':
      return html`<p class="navarone-words">Navarone marked ${problem.name} as a place to look.</p>`;
    case 'fixed':
      return html`
        ${step.secondRound ? html`<p class="navarone-words">A second fix, after the validator refused the first answer.</p>` : nothing}
        ${beforeAfter(step.before, step.after)}`;
    case 'resolved':
      return words(step.reason);
    case 'refused-fix':
      return words(step.finding);
    case 'refused-reason':
      return html`${words(step.finding)}${codePart('The code the reason defends', step.before)}`;
    case 'accepted':
      return html`<p class="navarone-words">The validator keeps the builder's answer.</p>`;
    case 'escalated':
      return words(step.text);
    case 'settled':
      return settledView(step);
    default:
      return nothing;
  }
}

// The user's answer to an escalation, and his words when he gave some.
function settledView(step) {
  const answer = step.user === 'keep' ? 'Keep the code as it is.' : 'Change the code.';
  if (!step.words) return html`<p class="navarone-words">${answer}</p>`;
  return html`<p class="navarone-words">${answer}</p>${words(step.words)}`;
}

// An agent's words in full, as it wrote them.
function words(text) {
  return html`<p class="navarone-words">${text || 'Nothing was recorded.'}</p>`;
}

// The code before and after a fix, side by side, the lines that differ marked on both; a fix that left no subject has no after.
function beforeAfter(before, after) {
  const marks = changesBetween(before, after);
  return html`
    <div class="compare">
      ${codePart('Before', before, marks?.before, 'before')}
      ${after ? codePart('After', after, marks?.after, 'after') : html`<section class="block"><h3 class="block-caption">After</h3><p class="navarone-none">The fix removed the code.</p></section>`}
    </div>`;
}

// Where the two stored codes differ, or null while either is not read yet or there is no after to compare.
function changesBetween(before, after) {
  const was = navarone.codes.get(before);
  const now = navarone.codes.get(after);
  if (!was?.code || !now?.code) {
    return null;
  }
  return changeMarks(was.code, now.code);
}

function codePart(label, hash, changes, side) {
  return html`<section class="block"><h3 class="block-caption">${label}</h3>${storedCode(hash, changes, side)}</section>`;
}

function storedCode(hash, changes, side) {
  if (!hash) return html`<p class="navarone-none">The code is not stored.</p>`;
  const stored = navarone.codes.get(hash);
  if (!stored) return html`<p class="navarone-none">Reading the code.</p>`;
  if (stored.error) return html`<p class="quiet-error">${stored.error}</p>`;
  return plainCode({ code: stored.code, language: NAVARONE_LANGUAGE, changes, side });
}

// ---- The lens ----

const lensHost = document.createElement('div');
lensHost.className = 'navarone-lens-host';

function openLens(problem) {
  if (!problem?.method) {
    toast('No method here for the lens to show.');
    return;
  }
  navarone.lens = { problem, subject: problem.method, view: VIEWS[0].view, trail: [], result: null, error: null, loading: false, seq: 0, before: document.activeElement };
  runLens();
  renderLens();
  lensHost.querySelector('.navarone-lens')?.focus();
}

function closeLens() {
  const before = navarone.lens?.before;
  navarone.lens = null;
  renderLens();
  if (before?.isConnected) before.focus();
  else $('main').focus();
}

function lensQuery(lens) {
  return `method ${lens.subject} ${lens.view}`;
}

async function runLens() {
  const lens = navarone.lens;
  lens.loading = true;
  const seq = ++lens.seq;
  const { problem } = lens;
  const query = new URLSearchParams({ worktree: problem.worktree, id: problem.id, q: lensQuery(lens) });
  const { ok, body } = await call(`/api/navarone/lens?${query}`);
  if (navarone.lens !== lens || lens.seq !== seq) return;
  lens.loading = false;
  lens.result = ok ? body : null;
  lens.error = ok ? null : body.error;
  renderLens();
  lensHost.querySelector('.navarone-lens-code')?.scrollTo(0, 0);
}

function showView(view) {
  const lens = navarone.lens;
  if (!lens || lens.view === view) return;
  lens.view = view;
  runLens();
  renderLens();
}

// A method a mark names becomes the subject, in the view shown; the one before goes on the trail.
function showSubject(subject) {
  const lens = navarone.lens;
  lens.trail.push(lens.subject);
  lens.subject = subject;
  runLens();
  renderLens();
}

// Back to a subject on the trail, the last one when none is named.
function lensBack(at) {
  const lens = navarone.lens;
  if (!lens?.trail.length) {
    toast('No method before this one.');
    return;
  }
  const to = at ?? lens.trail.length - 1;
  lens.subject = lens.trail[to];
  lens.trail = lens.trail.slice(0, to);
  runLens();
  renderLens();
}

// A mark that names one method shows it at once; one that names several opens a menu to pick from. Code shown above
// scrolls to where it is shown.
function followMark(mark, el) {
  if (mark.section) {
    lensHost.querySelector(`[data-section="${CSS.escape(mark.section)}"]`)?.scrollIntoView({ block: 'start' });
    return;
  }
  const links = mark.queries.map((one) => ({ label: one.label, run: () => showSubject(one.label) }));
  if (links.length === 1) links[0].run();
  else openContextMenu(links, el);
}

// The lens is drawn over the tab's own page only: a node page opened from the search hides it until the page is back.
function renderLens() {
  const shown = state.inbox === NAVARONE && state.view === 'review';
  const lens = shown ? navarone.lens : null;
  if (!lens) {
    lensHost.remove();
    return;
  }
  if (!lensHost.isConnected) $('main').after(lensHost);
  render(lensView(lens), lensHost);
}

function lensView(lens) {
  return html`
    <div class="navarone-lens" role="dialog" aria-modal="true" aria-label=${`The lens on ${lens.subject}`} tabindex="-1">
      <header class="pane-head compact navarone-lens-head">
        <h1 class="pane-title">Lens</h1>
        <nav class="navarone-trail" aria-label="The methods before this one">
          ${lens.trail.map((subject, at) => html`<button class="link subtle" @click=${() => lensBack(at)}>${subject}</button><span aria-hidden="true">›</span>`)}
          <code>${lens.subject}</code>
        </nav>
        <span class="navarone-lens-acts">
          <button class="button" ?disabled=${!lens.trail.length} @click=${() => lensBack()}>Back <kbd>u</kbd></button>
          <button class="button" @click=${summonChat}>Talk <kbd>Alt</kbd><kbd>A</kbd></button>
          <button class="button" @click=${closeLens}>Close <kbd>Esc</kbd></button>
        </span>
      </header>
      <div class="navarone-lens-body">
        <nav class="navarone-lens-views" aria-label="Lens views">
          ${VIEWS.map((one) => html`<button class="button" aria-pressed=${String(lens.view === one.view)} @click=${() => showView(one.view)}>${one.label}<kbd>${one.key}</kbd></button>`)}
        </nav>
        <div class="navarone-lens-code">
          ${lens.loading ? html`<p class="navarone-none">Running the lens.</p>` : nothing}
          ${lens.error ? html`<p class="quiet-error">${lens.error}</p>` : nothing}
          ${lens.result?.commit ? html`<p class="doc-meta">at commit <code>${lens.result.commit}</code></p>` : nothing}
          ${(lens.result?.sections || []).map(lensSection)}
        </div>
      </div>
    </div>`;
}

// A section of the lens: its blocks drawn as every context's are, a mark's click going to followMark, then its children.
function lensSection(view) {
  const section = view.section;
  const part = { id: `lens:${section.id}`, drift: view.drift, sums: view.sums, agreements: view.agreements, onMark: followMark };
  return html`
    <section class="navarone-lens-part" data-section=${section.id}>
      <h2 class="block-caption">${section.title}</h2>
      ${blocksView(part, indexed(section.blocks), '')}
      ${view.children.length ? html`<div class="navarone-lens-children">${view.children.map(lensSection)}</div>` : nothing}
    </section>`;
}

// ---- What a chat summoned on the tab starts with ----

function stepText(step) {
  const parts = [`- ${STEP_HEADS[step.type] || step.type}, ${step.at}`];
  for (const [label, text] of [['reason', step.reason], ['finding', step.finding], ['text', step.text], ['words', step.words]]) {
    if (text) parts.push(`  ${label}: ${text}`);
  }
  for (const [label, hash] of [['code before', step.before], ['code after', step.after]]) {
    const code = hash ? navarone.codes.get(hash)?.code : null;
    if (code) parts.push(`  ${label}:\n\n\`\`\`\n${code}\n\`\`\``);
  }
  return parts.join('\n');
}

function storyText(problem) {
  return [
    `Navarone problem ${problem.id} in ${problem.worktree}, on branch ${problem.branch ?? NO_BRANCH}: ${problem.name}, check ${problem.check}, where it stands: ${problem.state}.`,
    'Its story, top to bottom:',
    ...problem.story.map(stepText),
  ].join('\n');
}

function lensText(lens) {
  const code = [];
  const walk = (views) => {
    for (const view of views) {
      code.push(`## ${view.section.title}`);
      for (const block of view.section.blocks) {
        if (block.type === 'code') code.push(`${block.path}:${block.startLine}\n\`\`\`\n${block.code}\n\`\`\``);
        else if (block.text) code.push(block.text);
      }
      walk(view.children);
    }
  };
  walk(lens.result?.sections || []);
  return [`The user has Navarone's lens open on ${lens.subject}, the query \`${lensQuery(lens)}\`. The code the lens shows:`, ...code].join('\n\n');
}

// The method open with its problems, as the method view shows them: the name, then each problem's check and where it stands.
function methodText(method) {
  const lines = method.problems.map((problem) => `- ${problem.check}, ${problem.state}, on branch ${problem.branch ?? NO_BRANCH}`);
  return [`The user has the method ${method.name} open in Navarone's signals, with its ${countOf(method.problems)}:`, ...lines].join('\n');
}

function navaroneContext(inbox) {
  const intro = 'The user has Navarone open in brain\'s window: a code-review tool whose signals mark places to look, which a builder answers and a validator judges. It is agent work, not yet on master. You may explain every side; the user judges.';
  const lens = navarone.lens;
  if (lens) return [intro, storyText(lens.problem), lensText(lens)].join('\n\n');
  const story = problemByKey(navarone.story);
  if (story) return [intro, storyText(story)].join('\n\n');
  if (escalationOpen()) return inbox.context();
  const method = openMethod();
  if (method) return [intro, methodText(method)].join('\n\n');
  return null;
}

// ---- Keys and commands ----

// The lens's keys while it is open. Every plain key is its own, so nothing under the modal moves.
function lensKey(event) {
  if (contextMenuKey(event)) return true;
  if (!plainKey(event)) return false;
  const view = VIEWS.find((one) => one.key === event.key);
  const actions = {
    Escape: closeLens,
    u: () => lensBack(),
    Backspace: () => lensBack(),
  };
  const action = view ? () => showView(view.view) : actions[event.key];
  if (action) {
    event.preventDefault();
    action();
  }
  return true;
}

// The page's own keys, before the question view's: Esc and the moves of the story and the signals, the flows on 1 and 2.
function pageKey(event) {
  if (navarone.story) {
    if (event.key !== 'Escape') return false;
    event.preventDefault();
    closeStory();
    return true;
  }
  if (escalationOpen()) return false;
  const flows = { 1: ESCALATIONS, 2: SIGNALS };
  if (flows[event.key]) {
    event.preventDefault();
    setFlow(flows[event.key]);
    return true;
  }
  if (navarone.flow !== SIGNALS) return false;
  if (event.key === 'Escape' && !openMethod()) return false;
  const actions = {
    Escape: closeMethod,
    j: () => stepSignal(1),
    k: () => stepSignal(-1),
    Enter: openCurrentSignal,
  };
  const action = actions[event.key];
  if (!action) return false;
  event.preventDefault();
  action();
  return true;
}

function navaroneKeys(inbox, event) {
  if (state.inbox !== NAVARONE || state.view !== 'review') return false;
  if (navarone.lens) return lensKey(event);
  if (event.altKey && !event.ctrlKey && !event.metaKey && event.code === 'KeyL') {
    event.preventDefault();
    openLens(problemInView());
    return true;
  }
  if (!plainKey(event)) return inbox.keys(event);
  const taken = pageKey(event);
  if (taken) return true;
  return inbox.keys(event);
}

function navaroneCommands(inbox) {
  if (state.inbox !== NAVARONE) return [];
  const problem = problemInView();
  const list = [
    { name: 'Show the escalations', keys: ['1'], run: () => setFlow(ESCALATIONS) },
    { name: 'Show the signals', keys: ['2'], run: () => setFlow(SIGNALS) },
  ];
  if (problem?.method) list.push({ name: `Open the lens on ${problem.method}`, keys: ['Alt', 'L'], run: () => openLens(problem) });
  if (navarone.story) list.push({ name: 'Back to the method', keys: ['Esc'], run: closeStory });
  else if (openMethod()) list.push({ name: 'Back to the methods', keys: ['Esc'], run: closeMethod });
  if (navarone.flow === ESCALATIONS) list.push(...inbox.commands());
  return list;
}

// ---- The tab ----

// The navarone source's inbox, wrapped as the Navarone tab: its escalations stay the source's questions, drawn and
// answered as questions.js draws and answers them, and the tab adds the signals and the lens.
function navaroneTab(inbox) {
  return {
    ...inbox,
    label: 'Navarone',
    start: loadProblems,
    shown: loadProblems,
    drawn: renderLens,
    left: () => {
      navarone.lens = null;
      renderLens();
    },
    list: () => (escalationOpen() ? inbox.list() : null),
    layout: () => (escalationOpen() ? inbox.layout() : 'list'),
    render: (main) => {
      if (escalationOpen()) {
        inbox.render(main);
        return;
      }
      delete main.dataset.item;
      render(pageView(), host);
      if (host.parentNode !== main) main.replaceChildren(host);
    },
    keys: (event) => navaroneKeys(inbox, event),
    commands: () => navaroneCommands(inbox),
    context: () => navaroneContext(inbox),
  };
}

// The lens button an open escalation's head adds, when the escalation's problem names a method.
function escalationActions(q) {
  const problem = problemOfQuestion(q);
  if (!problem?.method) return null;
  return templateNode(html`<p class="navarone-actions">${lensButton(problem)}</p>`);
}

hostSource(NAVARONE, navaroneTab, escalationActions);
offer({ openNavaroneStory });
