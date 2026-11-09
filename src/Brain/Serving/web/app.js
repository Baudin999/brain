// brain's window: the user's yes to every write, the graph to read while giving it, and other tools' questions in
// inboxes of their own, which questions.js draws.
//
// The launch code arrives once in the URL fragment and is traded for a session token that lives in this page's memory
// only. Every call carries it in a header. A reload loses it, so the page refuses reload keys; brain ui opens a fresh
// window.
//
// There is one window. brain serve says "close" on the window's event stream when a newer window opened or before it
// stops, and the page closes itself. A page that loses its server without that word closes itself too. Only a page
// that never had a key, or one an alarm shut, stays open to say why. When brain serve restarts onto a new build it says
// "moved" instead, with its successor's launch URL, and the page goes there in place: the same window, on the new build.
'use strict';

const SESSION = 'X-Brain-Session';
const POLL_MS = 1000;
const POLL_TIMEOUT_MS = 10000;
const EVENTS_RETRY_MS = 2000;
const MOVE_POLL_MS = 200;
const MOVE_WAIT_MS = 60000;
const SETTLE_MS = 700;
const UNPINNED_KEPT = 6;
const PINS_KEY = 'brain.pins';
const FOCUS_KEY = 'brain.focus';
const TAB_KEY = 'brain.tab';
const NODE_REVIEW = 'node review';
const HOME = ':home';

const state = {
  token: null,
  events: null,
  closed: null,
  items: [],
  version: null,
  // What the last poll of /api/state failed with, null while it reads; a failure is told once.
  failing: null,
  selected: null,
  selectedAt: 0,
  view: 'review',
  node: null,
  modes: {},
  reasonFor: null,
  reasonText: '',
  busy: false,
  side: true,
  query: '',
  hits: [],
  keywordsOnly: null,
  cursor: -1,
  // The entries nearest to the write in view, by node: { id, views }, views null while they load.
  near: null,
  // An entry read beside one node of the write in view: { id, about, node }.
  beside: null,
  stack: [],
  // The command palette, or with kind projects the project picker.
  palette: { open: false, query: '', cursor: 0, kind: 'commands' },
  // The image viewer while it is open, the instance imageViewer() made, else null.
  imageViewer: null,
  // The inbox shown, by the name it registered under. Sources and their questions come from questions.js.
  inbox: storedTab(),
  sources: [],
  // What the window can focus on besides all: personal, global and every project in projects.tsv, each with its hue.
  projects: [],
  asking: false,
  // The question the cursor is on, by source; opened says, by source, whether it is open or the inbox lists them all.
  picks: {},
  opened: {},
  pickedAt: 0,
  tags: {},
  verdict: null,
  // The user's words for a question, by source and question id, kept until its verdict is sent.
  drafts: {},
  focusWords: false,
  // The mark stepped to: { key, at }, the key naming the code block.
  markAt: null,
  // Each timeline's open step and the step pinned beside it, by key, and the timeline the step keys move.
  timelines: {},
  timeline: null,
  split: true,
  whole: false,
  unfolded: new Set(),
};

const $ = (id) => document.getElementById(id);

// h('div', {class: 'x', onclick: f}, child, 'text'): an element, its attributes and its children. Text is never HTML.
function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs || {})) {
    if (value === null || value === undefined || value === false) continue;
    if (key.startsWith('on')) el.addEventListener(key.slice(2), value);
    else if (key === 'class') el.className = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  el.append(...nodes(children));
  return el;
}

// fill(el, child, 'text'): replaces an element's children as h() builds them. Every child list goes through nodes(), which
// flattens lists nested at any depth, drops a null, undefined or false child and never prints one as text.
function fill(el, ...children) {
  el.replaceChildren(...nodes(children));
}

function nodes(children) {
  return children
    .flat(Infinity)
    .filter((child) => child !== null && child !== undefined && child !== false)
    .map((child) => (child instanceof Node ? child : document.createTextNode(String(child))));
}

// A lit-html template as a node for fill(), where a place is still filled by hand: it is drawn once into a fragment,
// which fill() empties into its place.
function templateNode(template) {
  const fragment = new DocumentFragment();
  render(template, fragment);
  return fragment;
}

function keys(...names) {
  return h('span', { class: 'palette-keys' }, names.map((name) => h('kbd', {}, name)));
}

const ICON_PIN = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M6 2.5h4M7 2.5v4L4.5 9h7L9 6.5v-4M8 9v4.5"/></svg>';
const ICON_PINNED = '<svg viewBox="0 0 16 16" fill="currentColor" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M6 2.5h4M7 2.5v4L4.5 9h7L9 6.5v-4"/><path d="M8 9v4.5" fill="none"/></svg>';
const ICON_CLOSE = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"><path d="M4 4l8 8M12 4l-8 8"/></svg>';
const ICON_DOWN = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M8 3v9M4 8.5l4 4 4-4"/></svg>';

function icon(svg) {
  const span = h('span', { 'aria-hidden': 'true' });
  span.innerHTML = svg;
  return span;
}

// ---- The inboxes, in the strip down the window's left edge ----
//
// registerInbox({ name, label, count(graph), start(), shown(), left(), drawn(), changed(), themed(), redraw(), list(), render(main), view(), keys(event),
// commands(), context(), layout(), noChat, noWaiting, ownSide, bottom, error() }) puts one in the strip, in the order registered, node review first; registering a
// name again replaces it in place. With bottom it sits at the strip's far end, below Ask again, as a view rather than work
// that waits. name is what state.inbox holds while it is shown. count(graph) is how much of its work waits in that graph, in every graph for null.
// list() draws the queue column under its head, render(main) the main pane. The rest is optional:
// start() runs once the window has its session, shown() runs each time the inbox is shown, changed() each time the
// pending writes change, as when one lands, left() each time another inbox is shown in its place, drawn() each time the
// main pane is drawn while it is shown, a node page in it included, themed() each time
// the theme's colors change, redraw() each time the global state in state.js changes while the inbox is shown,
// keys(event) sees every key first and is true when it took it, commands() adds to the palette, with ownSide the reading pane starts closed in this
// inbox and keeps its own state there, context() describes what the inbox has in view, as text for a chat summoned on it
// to start with, and is async or returns nothing, layout() is list when the main pane lists the inbox's work and the queue
// column folds away, and bench when the main pane holds an open question alone and the reading pane folds away too, noChat keeps a
// chat from being summoned on it, waitingChanged() each time the queue is drawn again, as what waits changed, noWaiting leaves out the head of what waits above its list, error() says why the inbox is failing, and with top it goes first in the strip.
// searchPath is the call the search box asks while the inbox is shown, /api/search when it names none, and onHits() runs
// whenever the hits change while it is shown. view() returns the main pane as a lit-html template, in place of
// render(main): it renders into a host of the inbox's own that the main pane holds, so what lit-html drew stays on the
// host while other views fill the main pane, and the next render only changes what changed.
const registered = [];

function registerInbox(inbox) {
  if (inbox.view) inbox.host = h('div', {});
  const at = registered.findIndex((one) => one.name === inbox.name);
  if (at >= 0) registered[at] = inbox;
  else if (inbox.top) registered.unshift(inbox);
  else registered.push(inbox);
  if (!state.token) return;
  inbox.start?.();
  renderQueue();
}

// Takes an inbox out of the strip, as a source gone from sources.tsv; node review shows when it was the one in view.
function dropInbox(name) {
  const at = registered.findIndex((one) => one.name === name);
  if (at >= 0) registered.splice(at, 1);
  if (state.inbox === name) state.inbox = NODE_REVIEW;
}

function shownInbox() {
  return registered.find((inbox) => inbox.name === state.inbox) || registered[0];
}

// The tab the window opens on: the last one the user opened, kept in localStorage like the focus, else Home. A window
// without storage opens on Home.
function storedTab() {
  try {
    return localStorage.getItem(TAB_KEY) || HOME;
  } catch {
    return HOME;
  }
}

function rememberTab(name) {
  try {
    localStorage.setItem(TAB_KEY, name);
  } catch {
    // A window without storage forgets its tab when it closes.
  }
}

function showInbox(name) {
  const was = shownInbox();
  state.inbox = name;
  rememberTab(name);
  state.view = 'review';
  state.node = null;
  state.verdict = null;
  state.pickedAt = performance.now();
  renderQueue();
  renderMain();
  renderChat();
  $('main').scrollTop = 0;
  if (was !== shownInbox()) was.left?.();
  shownInbox().shown?.();
  // Each inbox may search its own way, so a search typed elsewhere asks again.
  if (state.query.trim()) search();
}

function stepInbox(delta) {
  const at = registered.indexOf(shownInbox());
  showInbox(registered[(at + delta + registered.length) % registered.length].name);
}

// A key an inbox may take as its own: no modifier but Shift, nothing typed into a box, and no palette open.
function plainKey(event) {
  const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
  return !typing && !event.ctrlKey && !event.metaKey && !event.altKey && !state.palette.open;
}

// The strip: each inbox with how much of its work waits in the project in focus, its label turned a quarter left. The
// inboxes registered with bottom sit at its far end.
function renderStrip() {
  fill($('strip'),
    registered.filter((inbox) => !inbox.bottom).map(stripItem),
    h('span', { class: 'strip-gap' }),
    state.sources.length
      ? h(
          'button',
          { class: 'row vertical', disabled: state.asking, title: 'Run every source in sources.tsv again, F5', onclick: askAgain },
          h('span', { class: 'strip-label strip-ask' }, state.asking ? 'Asking' : 'Ask again'),
        )
      : null,
    registered.filter((inbox) => inbox.bottom).map(stripItem),
  );
}

function stripItem(inbox) {
  const error = inbox.error?.();
  const count = inbox.count(focus());
  return h(
    'button',
    {
      class: 'row vertical',
      'aria-current': String(inbox === shownInbox()),
      'data-failing': error ? 'true' : null,
      title: error || inbox.label,
      onclick: () => showInbox(inbox.name),
    },
    h('span', { class: 'strip-label' }, inbox.label),
    error ? h('span', { class: 'strip-failing' }, 'failing') : count ? h('span', { class: 'strip-count' }, String(count)) : null,
    agentMark(inbox.name),
  );
}

// How much waits in a graph across every inbox, every graph for null. Each inbox counts what waits in the graph it is asked.
function waiting(graph) {
  return registered.reduce((sum, inbox) => sum + inbox.count(graph), 0);
}

// ---- The focus: one project for the whole window ----
//
// focus() is the one reader of the focus: null for every project, else personal, global or a project's name in
// projects.tsv. It shadows window.focus, which nothing calls. Focused, every inbox, list and count shows only that
// graph's work, and nothing of another's. The focus is kept in localStorage, so it survives a reload; only focus() and
// setFocus() touch it.
let focused = storedFocus();

function storedFocus() {
  try {
    return localStorage.getItem(FOCUS_KEY);
  } catch {
    return null;
  }
}

function focus() {
  return focused;
}

// A write is the work of each graph its changes land in, a question of its repo's project.
function inFocus(item) {
  return belongsTo(item, focus());
}

// True when the item is the work of the graph, or of any graph when it is null.
function belongsTo(item, graph) {
  return graph === null || item.graphs.some((one) => one.name === graph);
}

// What waits for a graph, or for every graph when null: its node reviews, and its disputes, the questions in the
// inboxes of the sources. Each inbox counts its own.
function pendingOf(graph) {
  const reviews = registered.find((inbox) => inbox.name === NODE_REVIEW).count(graph);
  let disputes = 0;
  for (const source of state.sources) {
    const inbox = registered.find((one) => one.name === source.name);
    disputes += inbox.count(graph);
  }
  return { reviews, disputes };
}

// The graphs with something waiting, each with its chip and its counts, in the order of the focus picker.
function pendingProjects() {
  const waiting = [];
  for (const graph of state.projects) {
    const { reviews, disputes } = pendingOf(graph.name);
    if (reviews + disputes > 0) {
      waiting.push({ name: graph.name, chip: graphChip(graph), reviews, disputes });
    }
  }
  return waiting;
}

function setFocus(graph) {
  focused = graph;
  try {
    if (graph === null) localStorage.removeItem(FOCUS_KEY);
    else localStorage.setItem(FOCUS_KEY, graph);
  } catch {
    // A window without storage keeps its focus until it closes.
  }
  state.pickedAt = performance.now();
  keepSelection(0);
  renderQueue();
  renderMain();
}

// The graphs to focus on. A focus on a project gone from projects.tsv would hide every item for good, so it ends.
async function loadProjects() {
  const { ok, body } = await call('/api/projects');
  if (!ok) {
    toast(body.error);
    return;
  }
  state.projects = body;
  if (focus() !== null && !body.some((graph) => graph.name === focus())) setFocus(null);
  else renderQueue();
  renderPalette();
}

// A graph as a chip: a project in its own color, the theme giving saturation and lightness; personal and global plain.
function graphChip(graph) {
  const project = graph.hue !== null && graph.hue !== undefined;
  const el = h('span', { class: 'chip', 'data-origin': project ? 'project' : 'none' }, graph.name);
  if (project) el.style.setProperty('--hue', String(graph.hue));
  return el;
}

// An item's chips: one per graph it is the work of, or a plain no project.
function graphChips(item) {
  return h('span', { class: 'chips' }, item.graphs.length ? item.graphs.map(graphChip) : graphChip({ name: 'no project', hue: null }));
}

function focusChip(graph) {
  return graph === null ? graphChip({ name: 'all', hue: null }) : graphChip(state.projects.find((one) => one.name === graph) || { name: graph, hue: null });
}

// The selector at the top of the queue: the focus, all included. It opens the picker, as Alt+P does.
function focusSelector() {
  return h(
    'button',
    { class: 'select subtle focus-select', title: 'Focus on one project', onclick: () => openPalette('projects') },
    h('span', { class: 'focus-label' }, 'Focus'),
    focusChip(focus()),
    keys('Alt', 'P'),
  );
}

// The picker's rows: all, then every graph, each with its graph's path and how much waits for it, so a project with
// nothing waiting is there. All has no path.
function focusChoices() {
  const graphs = [{ graph: null, name: 'all', path: null }, ...state.projects.map((project) => ({ graph: project.name, name: project.name, path: project.path }))];
  return graphs.map(({ graph, name, path }) => ({
    name,
    path,
    chip: focusChip(graph),
    current: graph === focus(),
    ...pendingOf(graph),
    run: () => setFocus(graph),
  }));
}

// True when the query's letters appear in the text in order, so cklb finds CkLang-Bootstrap.
function fuzzy(text, query) {
  const wanted = query.toLowerCase().replace(/\s+/g, '');
  let at = 0;
  for (const letter of text.toLowerCase()) if (letter === wanted[at]) at++;
  return at === wanted.length;
}

// ---- Talking to brain serve ----

// What call throws when the request never reached brain serve: fetch fails with a TypeError only then.
class ServerGone extends Error {}

async function call(path, options = {}) {
  let response;
  try {
    response = await fetch(path, {
      ...options,
      headers: { ...(options.headers || {}), [SESSION]: state.token, 'Content-Type': 'application/json' },
    });
  } catch (error) {
    if (error instanceof TypeError) throw new ServerGone(error.message);
    throw error;
  }
  const body = await response.json().catch(() => ({ error: 'brain serve answered with something that is not JSON' }));
  if (response.status === 401) {
    shut(body.error, !body.alarm);
    throw new Error(body.error);
  }
  return { ok: response.ok, status: response.status, body };
}

async function launch() {
  const match = /^#launch=([0-9a-f]{64})$/.exec(location.hash);
  history.replaceState(null, '', '/');
  if (!match) {
    shut('This window has no key. Open brain again with brain ui.');
    return;
  }
  const response = await fetch('/api/launch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code: match[1] }),
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    shut(body.error || 'brain serve refused this window.', !body.alarm);
    return;
  }
  state.token = body.token;
  loadPins();
  renderAll();
  poll();
  followEvents();
  loadProjects();
  loadQuestions();
  loadAgents();
  for (const inbox of registered) inbox.start?.();
}

// One poll: the state's version, and when it moved, the pending writes and every inbox. The version is kept only once
// they all read and drew, so a load or a draw that fails is tried again on the next poll, never lost. The two requests
// of a poll, the state and the pending writes, end after POLL_TIMEOUT_MS without an answer, so neither can stop the
// polling. Only a request that never reached brain serve shuts the window.
async function poll() {
  if (state.closed) return;
  try {
    const { ok, body } = await call('/api/state', { signal: AbortSignal.timeout(POLL_TIMEOUT_MS) });
    // A failure is told once, not on every poll, until the state reads again.
    if (!ok && body.error !== state.failing) toast(body.error);
    state.failing = ok ? null : body.error;
    if (ok && body.version !== state.version) {
      if (await loadPending()) {
        for (const inbox of registered) inbox.changed?.();
        state.version = body.version;
      }
    }
  } catch (error) {
    if (state.closed) return;
    if (error instanceof ServerGone) {
      shut('brain serve stopped. Open brain again with brain ui.', true);
      return;
    }
    // Anything else, a draw that threw or a call that timed out, is told once and tried again on the next poll.
    if (error.message !== state.failing) toast(error.message);
    state.failing = error.message;
  }
  if (!state.closed) setTimeout(poll, POLL_MS);
}

// brain serve says "theme" when the stream opens and on every Omarchy theme switch: the page swaps /theme.css in place,
// so nothing reloads and nothing scrolls. It says "close" when this window must go, and "moved" when it goes on in the
// successor of a restart.
async function followEvents() {
  while (!state.closed) {
    state.events = new AbortController();
    try {
      const response = await fetch('/api/events', { headers: { [SESSION]: state.token }, signal: state.events.signal });
      if (!response.ok) return;
      const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
      let buffer = '';
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += value;
        for (let end = buffer.indexOf('\n\n'); end >= 0; end = buffer.indexOf('\n\n')) {
          const lines = buffer.slice(0, end).split('\n');
          if (lines.includes('event: theme')) swapTheme();
          if (lines.includes('event: close')) shut('A newer brain window took over, or brain serve stopped.', true);
          if (lines.includes('event: moved')) moved(lines);
          buffer = buffer.slice(end + 2);
        }
      }
    } catch {
      // The stream broke or the window shut; poll says which.
    }
    await new Promise((resolve) => setTimeout(resolve, EVENTS_RETRY_MS));
  }
}

// The moved event names the successor's launch URL on its data line; one without it moves nowhere.
function moved(lines) {
  const data = lines.find((line) => line.startsWith('data: '));
  if (!data) return;
  const url = data.slice('data: '.length);
  move(url);
}

// brain serve restarts onto a new build and hands this window on: once its successor serves the page, the window goes
// to the successor's launch URL, whose page trades the launch code. Until then the old server answers nothing and the
// port may stand free, and the window stays as it is: it neither closes nor says brain serve stopped. A successor that
// never serves leaves the window to shut as on a server gone.
async function move(url) {
  if (state.closed) return;
  state.closed = 'brain moves onto its new build.';
  state.token = null;
  state.events?.abort();
  const until = Date.now() + MOVE_WAIT_MS;
  while (Date.now() < until) {
    if (await serves()) {
      // The launch URL differs from this page's only in its fragment, so the replace alone stays in this page: the
      // reload loads the successor's.
      location.replace(url);
      location.reload();
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, MOVE_POLL_MS));
  }
  // The window is no longer moving: it shuts.
  state.closed = null;
  shut('brain serve did not come back on its new build. Open brain again with brain ui.', true);
}

// True once a brain serve answers the page: the one that restarts refuses everything, and none answers while the port
// stands free.
async function serves() {
  try {
    const response = await fetch('/', { cache: 'no-store' });
    return response.ok;
  } catch {
    return false;
  }
}

// The new sheet goes last, and every older one goes once it loaded, so a quick second switch still ends on the newest.
function swapTheme() {
  const next = h('link', { rel: 'stylesheet', href: `/theme.css?at=${Date.now()}`, 'data-theme': true });
  next.addEventListener('load', () => {
    const sheets = [...document.querySelectorAll('link[data-theme]')];
    for (const old of sheets.slice(0, sheets.indexOf(next))) old.remove();
    for (const inbox of registered) inbox.themed?.();
  });
  next.addEventListener('error', () => next.remove());
  document.head.append(next);
}

async function loadPending() {
  const { ok, body } = await call('/api/pending', { signal: AbortSignal.timeout(POLL_TIMEOUT_MS) });
  if (!ok) {
    toast(body.error);
    return false;
  }
  const before = visible().findIndex((item) => item.id === state.selected);
  state.items = body;
  keepSelection(before);
  if (state.reasonFor && !state.items.some((item) => item.id === state.reasonFor)) state.reasonFor = null;
  renderQueue();
  if (state.view === 'review' && state.inbox === NODE_REVIEW) renderMain();
  renderPalette();
  return true;
}

// Shuts the window on a message. With close it closes itself too; the message stays on screen when the browser refuses.
function shut(message, close) {
  if (state.closed) return;
  state.closed = message || 'This window is closed. Open brain again with brain ui.';
  state.token = null;
  state.events?.abort();
  if (close) window.close();
  fill($('strip'));
  fill($('queue'));
  $('side').hidden = true;
  fill($('main'),
    h('div', { class: 'quiet' }, h('h2', {}, 'This window is closed'), h('p', {}, state.closed)),
  );
}

// ---- A node, read ----

function doc(node, { compact = false, onTitle = null, ref = true } = {}) {
  const meta = h(
    'p',
    { class: 'doc-meta' },
    ref ? h('span', {}, node.ref) : null,
    node.kind ? h('span', {}, node.kind) : null,
    node.subjects.map((subject) => h('span', { class: 'subject' }, subject)),
    !compact && node.namedBy ? h('span', {}, `named by ${node.namedBy}`) : null,
    !compact && node.born ? h('span', {}, `born ${node.born}`) : null,
  );
  return h(
    'article',
    { class: 'doc' },
    meta,
    h(compact ? 'h3' : 'h1', { class: 'doc-title' }, onTitle ? h('button', { class: 'link heading plain', onclick: onTitle }, node.title || node.slug) : node.title || node.slug),
    node.body ? markdownView(node.body) : null,
    node.why ? h('section', { class: 'doc-section' }, h('h2', {}, 'Why'), markdownView(node.why)) : null,
    node.edges.length
      ? h('section', { class: 'doc-section' }, h('h2', {}, 'Edges'), h('ul', { class: 'edges' }, node.edges.map(edge)))
      : null,
  );
}

function edge(e) {
  const target = e.ref
    ? h(
        'span',
        { class: 'edge-target' },
        h(
          'a',
          {
            class: 'link accent plain',
            href: '#',
            title: 'Enter opens it here, Shift+Enter in the reading pane',
            onclick: (event) => {
              event.preventDefault();
              event.shiftKey ? openSide(e.ref) : openMain(e.ref);
            },
            onkeydown: (event) => {
              if (event.key !== 'Enter') return;
              event.preventDefault();
              event.stopPropagation();
              event.shiftKey ? openSide(e.ref) : openMain(e.ref);
            },
          },
          e.title || e.ref,
        ),
        h('span', { class: 'edge-ref' }, e.ref),
      )
    : h('span', { class: 'edge-target edge-dead' }, e.target);
  return h('li', { class: 'edge' }, h('span', { class: 'edge-rel' }, e.rel), target);
}

// ---- A diff over whole lines, every line shown: an edit hides nothing it keeps. ----

function textLines(text) {
  return text == null || text === '' ? [] : text.replace(/\n$/, '').split('\n');
}

// How many items two sequences start and end with in common, the tail never reaching into the head.
function commonEnds(a, b) {
  let head = 0;
  while (head < a.length && head < b.length && a[head] === b[head]) {
    head++;
  }
  let tail = 0;
  while (tail < a.length - head && tail < b.length - head && a[a.length - 1 - tail] === b[b.length - 1 - tail]) {
    tail++;
  }
  return { head, tail };
}

// The lines both texts start and end with stay out of the table, so a small edit to a long file costs little.
function diffLines(before, after) {
  const a = textLines(before);
  const b = textLines(after);
  const { head, tail } = commonEnds(a, b);
  const same = (line) => ['same', line];
  return [...a.slice(0, head).map(same), ...lcsLines(a.slice(head, a.length - tail), b.slice(head, b.length - tail)), ...a.slice(a.length - tail).map(same)];
}

// The diff's lines with their numbers: each row has its kind, its text and its line number before and after, null on the
// side it is not in.
function numberedRows(before, after) {
  const rows = [];
  let was = 0;
  let now = 0;
  for (const [kind, text] of diffLines(before, after)) {
    const row = { kind, text, before: null, after: null };
    if (kind !== 'add') {
      was++;
      row.before = was;
    }
    if (kind !== 'del') {
      now++;
      row.after = now;
    }
    rows.push(row);
  }
  return rows;
}

// Where two codes differ, for each side: the index of a line in its code, 0-based, to the span of the line that changed,
// or null when the whole line is new or gone.
function changeMarks(before, after) {
  const marks = { before: new Map(), after: new Map() };
  let removed = [];
  let added = [];
  for (const row of numberedRows(before, after)) {
    if (row.kind === 'del') {
      removed.push(row);
      continue;
    }
    if (row.kind === 'add') {
      added.push(row);
      continue;
    }
    markRun(removed, added, marks);
    removed = [];
    added = [];
  }
  markRun(removed, added, marks);
  return marks;
}

// One run of changes: its removed lines and added lines are paired in order, and a pair's span is what lies between the
// text its two lines start and end with. A line left without a partner is marked whole.
function markRun(removed, added, marks) {
  const longest = Math.max(removed.length, added.length);
  for (let i = 0; i < longest; i++) {
    const old = removed[i];
    const fresh = added[i];
    if (old && fresh) {
      const { head, tail } = commonEnds(old.text, fresh.text);
      marks.before.set(old.before - 1, [head, old.text.length - tail]);
      marks.after.set(fresh.after - 1, [head, fresh.text.length - tail]);
      continue;
    }
    if (old) {
      marks.before.set(old.before - 1, null);
      continue;
    }
    marks.after.set(fresh.after - 1, null);
  }
}

// A diff as text for an agent, one line each: + added, - removed, a space for the rest.
function diffText(before, after) {
  return diffLines(before, after).map(([kind, line]) => `${{ add: '+', del: '-', same: ' ' }[kind]} ${line}`);
}

function lcsLines(a, b) {
  const n = a.length;
  const m = b.length;
  const lcs = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
  }
  const out = [];
  let i = 0;
  let j = 0;
  while (i < n || j < m) {
    if (i < n && j < m && a[i] === b[j]) {
      out.push(['same', a[i]]);
      i++;
      j++;
    } else if (i < n && (j === m || lcs[i + 1][j] >= lcs[i][j + 1])) out.push(['del', a[i++]]);
    else out.push(['add', b[j++]]);
  }
  return out;
}

// A node's text is markdown, so its diff wraps at the wrap measure.
function diff(before, after) {
  const marks = { same: ' ', add: '+', del: '−' };
  return h(
    'div',
    { class: 'diff', role: 'figure', 'aria-label': 'Changes to the file' },
    diffLines(before, after).map(([kind, text]) =>
      h('div', { class: `diff-line ${kind}` }, h('span', { class: 'diff-mark' }, marks[kind]), h('span', { class: 'diff-text' }, text || ' ')),
    ),
  );
}

// ---- The queue ----

function title(item) {
  const shown = item.changes.find((change) => change.state !== 'edges') || item.changes[0];
  return shown ? shown.node.title || shown.ref : item.id;
}

function ago(iso) {
  const seconds = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 60) return 'just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
  return new Date(iso).toLocaleDateString();
}

// ---- Node review: the writes waiting for the user's yes ----

// Keeps the selection on a write shown, whichever inbox is in view. When the selected write left view, the one now at
// index at, or the last, takes it and settles before it takes an answer.
function keepSelection(at) {
  const list = visible();
  if (list.some((item) => item.id === state.selected)) return;
  state.selected = list.length ? list[Math.max(0, Math.min(at, list.length - 1))].id : null;
  state.selectedAt = performance.now();
}

// The writes the queue shows: those of the project in focus.
function visible() {
  return state.items.filter(inFocus);
}

// The queue column: the focus, how much waits for it across every inbox, and the list of the inbox shown.
function renderQueue() {
  renderStrip();
  shownInbox().waitingChanged?.();
  fill($('queue'),
    focusSelector(),
    shownInbox().noWaiting ? null : h('div', { class: 'pane-head compact flush queue-head' }, h('h1', { class: 'pane-title' }, 'Waiting for you'), h('span', { class: 'queue-count' }, String(waiting(focus())))),
    shownInbox().list(),
    h('div', { class: 'queue-foot' }, 'Commands ', keys('Ctrl', 'K'), registered.length > 1 ? h('span', { class: 'queue-foot-keys' }, 'Inboxes ', keys('H'), keys('L')) : null),
  );
}

function writeList() {
  const items = visible();
  if (!items.length) return h('p', { class: 'queue-empty' }, 'Nothing waits.');
  return h(
    'ol',
    {},
    items.map((item) =>
      h(
        'li',
        { class: 'queue-row' },
        h(
          'button',
          {
            class: 'row queue-item',
            'aria-current': state.view === 'review' && item.id === state.selected ? 'true' : 'false',
            onclick: () => select(item.id),
          },
          h('span', { class: 'queue-title' }, title(item)),
          h('span', { class: 'queue-sub' }, `${item.lines[0] || item.id}, ${ago(item.askedAt)}`),
        ),
        graphChips(item),
      ),
    ),
  );
}

// ---- The main pane ----

// The write in view; none while another inbox is shown.
function current() {
  if (state.inbox !== NODE_REVIEW) return null;
  return visible().find((item) => item.id === state.selected) || null;
}

// The write in view for a chat: what it does, each change in full and, for an edit, its diff. The chat changes it only by
// staging a new write; the user answers the waiting one in the window.
function reviewContext() {
  const item = current();
  if (!item) return null;
  const changes = item.changes.map((change) => {
    const text = change.state === 'forgotten' ? change.before : change.after;
    const lines = change.state === 'rewritten' || change.state === 'edges' ? diffText(change.before, change.after) : null;
    return [
      `## ${STATES[change.state] || change.state}: ${change.ref}`,
      change.stale ? 'A node in this write changed since it was staged, so a yes lands nothing.' : '',
      lines ? `The diff, + added and - removed:\n\n${lines.join('\n')}` : '',
      `The full text${change.state === 'forgotten' ? ' being forgotten' : ''}:\n\n${text ?? ''}`,
    ]
      .filter(Boolean)
      .join('\n\n');
  });
  return [
    "The user has this write open in node review, waiting for their yes. It lands only when they answer in the window, and you never answer it for them. To change it, stage a new write through the brain CLI as you always do; the user answers that one too.",
    `What the write does:\n${item.lines.map((line) => `- ${line}`).join('\n')}`,
    ...changes,
  ].join('\n\n');
}

function renderMain() {
  const main = $('main');
  if (state.closed) return;
  renderHits();
  showSide();
  const inbox = shownInbox();
  if (state.view === 'node') {
    fill(main, nodePage());
  } else if (inbox.view) {
    render(inbox.view(), inbox.host);
    if (inbox.host.parentNode !== main) fill(main, inbox.host);
  } else {
    inbox.render(main);
  }
  inbox.drawn?.();
}

function renderReviewMain(main) {
  const item = current();
  if (!item && focus() !== null) {
    fill(main,
      h('div', { class: 'quiet' }, h('h2', {}, `No write for ${focus()} waits for you`), h('p', { class: 'keys' }, 'Focus on another project ', keys('Alt', 'P'))),
    );
    return;
  }
  if (!item) {
    fill(main,
      h(
        'div',
        { class: 'quiet' },
        h('h2', {}, 'Nothing waits for you'),
        h('p', {}, 'When Claude asks to write to the graph, the write appears here in full, and it lands only on your yes.'),
        h('p', { class: 'keys' }, 'Search the graph ', h('kbd', {}, '/'), '   Commands ', keys('Ctrl', 'K')),
      ),
    );
    return;
  }
  if (state.near?.id !== item.id) loadNear(item);
  const scroll = main.scrollTop;
  const same = main.dataset.item === item.id;
  main.dataset.item = item.id;
  fill(main,
    h(
      'div',
      { class: state.beside?.id === item.id ? 'page page-wide' : 'page page-code' },
      h('ul', { class: 'says', 'aria-label': 'What the write does' }, item.lines.map((line) => h('li', {}, line))),
      item.changes.some((change) => change.stale)
        ? h('p', { class: 'stale' }, 'A node in this write changed since Claude staged it, so a yes lands nothing. Answer no and ask Claude to stage it again.')
        : null,
      item.changes.map((change, index) => changeView(item, change, index)),
      answerBar(item),
    ),
  );
  if (same) main.scrollTop = scroll;
  else main.scrollTop = 0;
  if (state.reasonFor === item.id) {
    const box = $('reason-text');
    box.value = state.reasonText;
    box.focus();
  }
}

const STATES = { new: 'New node', rewritten: 'Rewritten', forgotten: 'Forgotten', edges: 'Edges change' };

// The raw file is a view of every change: an edit as a diff, a new node as a diff from nothing, a forgotten one as a diff
// to nothing. So the user can always read byte for byte what lands, frontmatter and link targets included.
function defaultMode(change) {
  return change.state === 'rewritten' || change.state === 'edges' ? 'diff' : 'read';
}

function changeView(item, change, index) {
  const key = `${item.id}:${index}`;
  const mode = state.modes[key] || defaultMode(change);
  const raw = change.state === 'new' || change.state === 'forgotten' ? 'File' : 'Changes';
  const switcher = h(
    'span',
    { class: 'segmented', role: 'group', 'aria-label': 'Show' },
    h('button', { 'aria-pressed': String(mode === 'diff'), onclick: () => setMode(key, 'diff') }, raw),
    h('button', { 'aria-pressed': String(mode === 'read'), onclick: () => setMode(key, 'read') }, 'Reads as'),
  );
  let body;
  if (mode === 'diff') body = [h('h1', { class: 'doc-title diff-title' }, change.node.title || change.node.slug), diff(change.before, change.after)];
  else if (change.state === 'forgotten') body = h('div', { class: 'forgotten-doc' }, doc(change.node, { ref: false }));
  else body = doc(change.node, { ref: false });
  if (state.beside?.id === item.id && state.beside.about === change.ref) body = beside(body, state.beside.node);
  return h(
    'section',
    { class: 'change' },
    h(
      'div',
      { class: 'change-head' },
      h('span', { class: `change-state ${change.state}` }, STATES[change.state] || change.state),
      h('span', { class: 'change-ref' }, change.ref),
      switcher,
    ),
    body,
  );
}

function setMode(key, mode) {
  state.modes[key] = mode;
  renderMain();
}

function toggleModes() {
  const item = current();
  if (state.view !== 'review' || !item) return;
  item.changes.forEach((change, index) => {
    const key = `${item.id}:${index}`;
    state.modes[key] = (state.modes[key] || defaultMode(change)) === 'diff' ? 'read' : 'diff';
  });
  renderMain();
}

function answerBar(item) {
  const forgets = item.changes.every((change) => change.state === 'forgotten' || change.state === 'edges');
  const reason =
    state.reasonFor === item.id
      ? h(
          'div',
          { class: 'reason' },
          h('label', { for: 'reason-text' }, 'What should Claude change? Your words go back to it as you write them.'),
          h('textarea', {
            id: 'reason-text',
            class: 'field bare writing',
            oninput: (event) => {
              state.reasonText = event.target.value;
            },
          }),
          h('div', { class: 'reason-foot' }, h('span', {}, 'Send ', keys('Ctrl', 'Enter')), h('span', {}, 'Cancel ', keys('Esc'))),
        )
      : null;
  return h(
    'div',
    { class: 'answer' },
    reason,
    h(
      'div',
      { class: 'answer-bar' },
      h('button', { class: 'button yes signed', disabled: state.busy, onclick: () => answer('yes') }, forgets ? 'Yes, forget' : 'Yes, land it', h('kbd', {}, 'Y')),
      h('button', { class: 'button no signed', disabled: state.busy, onclick: () => answer('no') }, 'No', h('kbd', {}, 'N')),
      h('button', { class: 'button', disabled: state.busy, onclick: () => startReason() }, 'No, change it', h('kbd', {}, 'R')),
      h('span', { class: 'spacer' }),
      visible().length > 1 ? h('span', { class: 'answer-note' }, `${visible().indexOf(item) + 1} of ${visible().length}`) : null,
    ),
  );
}

function select(id) {
  state.view = 'review';
  state.inbox = NODE_REVIEW;
  if (state.selected !== id) {
    state.selected = id;
    state.selectedAt = performance.now();
    state.reasonFor = null;
    state.beside = null;
  }
  renderQueue();
  renderMain();
}

function step(delta) {
  const list = visible();
  if (!list.length) return;
  const at = list.findIndex((item) => item.id === state.selected);
  const next = Math.max(0, Math.min(list.length - 1, (at < 0 ? 0 : at) + delta));
  select(list[next].id);
}

function startReason() {
  const item = current();
  if (!item || state.view !== 'review') return;
  state.reasonFor = item.id;
  state.reasonText = '';
  renderMain();
}

async function answer(verdict, reason = null) {
  const item = current();
  if (!item || state.busy || state.view !== 'review') return;
  if (performance.now() - state.selectedAt < SETTLE_MS) {
    toast('This write just came into view. Read it, then answer.');
    return;
  }
  state.busy = true;
  renderMain();
  try {
    const { ok, status, body } = await call('/api/answer', {
      method: 'POST',
      body: JSON.stringify({ id: item.id, digest: item.digest, answer: verdict, reason }),
    });
    if (status === 425) toast(body.error);
    else if (!ok) toast(body.error);
    else if (body.verdict === 'yes') toast(`Landed ${item.changes.map((change) => change.ref).join(', ')}`);
    else if (body.verdict === 'refused') toast(body.refusal);
    else toast(body.reason ? 'Sent your words to Claude. Its rewrite appears here as a new write.' : 'Said no. Nothing landed.');
    if (ok) state.reasonFor = null;
  } finally {
    state.busy = false;
    state.version = null;
    await loadPending();
    renderMain();
  }
}

function nodePage() {
  const back = h('button', { class: 'link subtle back', onclick: showReview }, keys('Esc'), state.inbox !== NODE_REVIEW ? `Back to ${shownInbox().label}` : visible().length ? `Back to ${visible().length} waiting` : 'Back to the review');
  if (!state.node) return h('div', { class: 'page' }, back);
  if (state.node.error) return h('div', { class: 'page' }, back, h('p', {}, state.node.error));
  return h('div', { class: 'page' }, back, doc(state.node));
}

async function fetchNode(ref) {
  const { ok, body } = await call(`/api/node?ref=${encodeURIComponent(ref)}`);
  return ok ? body : { error: body.error, ref };
}

async function openMain(ref) {
  state.view = 'node';
  state.node = await fetchNode(ref);
  renderQueue();
  renderMain();
  $('main').scrollTop = 0;
  $('main').focus();
}

function showReview() {
  state.view = 'review';
  state.node = null;
  renderQueue();
  renderMain();
  $('main').focus();
}

// ---- The reading pane ----

function loadPins() {
  try {
    const pins = JSON.parse(localStorage.getItem(PINS_KEY) || '[]');
    Promise.all(pins.map(fetchNode)).then((nodes) => {
      state.stack = nodes.filter((node) => !node.error).map((node) => ({ node, pinned: true }));
      renderStack();
    });
  } catch {
    state.stack = [];
  }
}

function savePins() {
  try {
    localStorage.setItem(PINS_KEY, JSON.stringify(state.stack.filter((entry) => entry.pinned).map((entry) => entry.node.ref)));
  } catch {
    // A window without storage keeps its pins until it closes.
  }
}

async function openSide(ref) {
  state[sideKey()] = true;
  showSide();
  const node = await fetchNode(ref);
  if (node.error) {
    toast(node.error);
    return;
  }
  const kept = state.stack.filter((entry) => entry.node.ref !== ref);
  const pinned = state.stack.some((entry) => entry.node.ref === ref && entry.pinned);
  const entries = [{ node, pinned }, ...kept];
  let unpinned = 0;
  state.stack = entries.filter((entry) => entry.pinned || ++unpinned <= UNPINNED_KEPT);
  renderStack();
  $('stack').scrollTop = 0;
}

function renderStack() {
  const pinned = state.stack.filter((entry) => entry.pinned);
  const loose = state.stack.filter((entry) => !entry.pinned);
  const cards = [...pinned, ...loose].map((entry) =>
    h(
      'div',
      { class: entry.pinned ? 'card pinned' : 'card' },
      h(
        'div',
        { class: 'card-head' },
        h('span', { class: 'card-ref' }, entry.node.ref),
        h('button', { class: 'icon', 'aria-pressed': String(entry.pinned), title: entry.pinned ? 'Unpin' : 'Pin', onclick: () => pin(entry.node.ref) }, icon(entry.pinned ? ICON_PINNED : ICON_PIN)),
        h('button', { class: 'icon', title: 'Close', onclick: () => unstack(entry.node.ref) }, icon(ICON_CLOSE)),
      ),
      doc(entry.node, { compact: true, onTitle: () => openMain(entry.node.ref) }),
    ),
  );
  fill($('stack'),
    ...(cards.length
      ? cards
      : [h('p', { class: 'stack-empty' }, 'Search the graph to read beside the review. Enter opens a node in the main pane, Shift+Enter keeps it here.')]),
  );
}

function pin(ref) {
  state.stack = state.stack.map((entry) => (entry.node.ref === ref ? { ...entry, pinned: !entry.pinned } : entry));
  savePins();
  renderStack();
}

function unstack(ref) {
  state.stack = state.stack.filter((entry) => entry.node.ref !== ref);
  savePins();
  renderStack();
}

function closeUnpinned() {
  state.stack = state.stack.filter((entry) => entry.pinned);
  renderStack();
}

let searchTimer = 0;
let searchSeq = 0;

function onSearchInput(event) {
  state.query = event.target.value;
  clearTimeout(searchTimer);
  searchTimer = setTimeout(search, 160);
}

async function search() {
  const seq = ++searchSeq;
  const query = state.query.trim();
  if (!query) {
    state.hits = [];
    state.cursor = -1;
    renderHits();
    return;
  }
  const { ok, body } = await call(`${shownInbox().searchPath || '/api/search'}?q=${encodeURIComponent(query)}`);
  if (seq !== searchSeq) return;
  if (!ok) {
    toast(body.error);
    return;
  }
  state.hits = body.hits;
  state.keywordsOnly = body.keywordsOnly;
  state.cursor = state.hits.length ? 0 : -1;
  renderHits();
}

// Draws the hit list, then keeps the selected row in view inside it.
function renderHits() {
  const hits = $('hits');
  if (state.query.trim()) renderSearchHits(hits);
  else renderNear(hits);
  hits.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  if (state.view === 'review') shownInbox().onHits?.();
}

function renderSearchHits(hits) {
  const rows = state.hits.map((hit, index) =>
    h(
      'li',
      {
        class: 'row option hit',
        role: 'option',
        'aria-selected': String(index === state.cursor),
        onmousedown: (event) => {
          event.preventDefault();
          event.shiftKey ? openSide(hit.ref) : openMain(hit.ref);
        },
      },
      h('span', { class: 'hit-title' }, hit.title || hit.ref),
      hit.snippet ? h('span', { class: 'hit-snippet' }, ...marked(hit.snippet)) : null,
      h('span', { class: 'hit-ref' }, `${hit.ref}${hit.kind === 'idea' ? ', an idea' : ''}, ${hit.tier}`),
    ),
  );
  fill(hits,
    ...(rows.length ? rows : [h('li', { class: 'hits-note' }, 'No node is sure or likely.')]),
    state.keywordsOnly && rows.length ? h('li', { class: 'hits-note' }, `Keyword hits only: ${state.keywordsOnly}.`) : null,
    rows.length ? h('li', { class: 'hits-foot' }, keys('Enter'), ' open   ', keys('Shift', 'Enter'), ' keep here') : null,
  );
}

// ---- The entries nearest to the write in view ----

// Asks once per write for the entries nearest to each node it writes, the same query the command line printed under it.
// The reading pane shows them while no search is typed.
async function loadNear(item) {
  state.near = { id: item.id, views: null };
  state.cursor = -1;
  renderHits();
  const { ok, body } = await call(`/api/nearest?id=${encodeURIComponent(item.id)}`);
  if (state.near?.id !== item.id) return;
  state.near = { id: item.id, views: ok ? body : [], error: ok ? null : body.error };
  renderHits();
}

// The near entries of the write in view, in order, each knowing which node of the write it is near.
function nearHits() {
  const item = current();
  if (!item || state.near?.id !== item.id || !state.near.views) return [];
  return state.near.views.flatMap((view) => view.hits.map((hit) => ({ ...hit, about: view.ref })));
}

// The rows Up, Down and Enter move through: the search's hits while one is typed, else the entries near the write.
function listed() {
  return state.query.trim() ? state.hits : nearHits();
}

function renderNear(hits) {
  const item = current();
  if (!item || state.near?.id !== item.id) {
    fill(hits);
    return;
  }
  if (!state.near.views) {
    fill(hits, h('li', { class: 'hits-note' }, 'Finding the entries near this write.'));
    return;
  }
  if (state.near.error) {
    fill(hits, h('li', { class: 'hits-note' }, state.near.error));
    return;
  }
  if (!state.near.views.length) {
    fill(hits);
    return;
  }
  let index = 0;
  fill(hits,
    h('li', { class: 'near-mark' }, 'Near this write'),
    state.near.views.map((view) => [
      h('li', { class: 'near-head' }, view.ref),
      view.hits.length ? view.hits.map((hit) => nearRow(view, hit, index++)) : h('li', { class: 'hits-note' }, 'No node is near.'),
      view.keywordsOnly && view.hits.length ? h('li', { class: 'hits-note' }, `Keyword hits only: ${view.keywordsOnly}.`) : null,
    ]),
    h('li', { class: 'hits-foot' }, 'Click an entry to read it beside the write. Type to search instead.'),
  );
}

function nearRow(view, hit, index) {
  return h(
    'li',
    {
      class: 'row option hit',
      role: 'option',
      'aria-selected': String(index === state.cursor),
      onmousedown: (event) => {
        event.preventDefault();
        openBeside(view.ref, hit.ref);
      },
    },
    h('span', { class: 'hit-title' }, hit.title || hit.ref),
    hit.snippet ? h('span', { class: 'hit-snippet' }, hit.snippet) : null,
    h('span', { class: 'hit-ref' }, scopeChip(hit), hit.kind === 'idea' ? `${hit.ref}, an idea` : hit.ref),
  );
}

// An entry's scope as a chip: a project in its own color, as the queue shows a write's project; personal and global plain.
function scopeChip(hit) {
  const project = hit.hue !== null && hit.hue !== undefined;
  const el = h('span', { class: 'chip near-scope', 'data-origin': project ? 'project' : 'none' }, hit.scope);
  if (project) el.style.setProperty('--hue', String(hit.hue));
  return el;
}

// Opens a near entry beside the node of the write it is near, as two documents. The write stays in view and answerable.
async function openBeside(about, ref) {
  const item = current();
  if (!item) return;
  const node = await fetchNode(ref);
  if (node.error) {
    toast(node.error);
    return;
  }
  if (current()?.id !== item.id) return;
  state.view = 'review';
  state.beside = { id: item.id, about, node };
  renderQueue();
  renderMain();
}

function closeBeside() {
  state.beside = null;
  renderMain();
}

function beside(body, node) {
  return h(
    'div',
    { class: 'beside' },
    h('div', { class: 'beside-write' }, body),
    h(
      'div',
      { class: 'beside-entry' },
      h(
        'div',
        { class: 'card-head' },
        h('span', { class: 'card-ref' }, `Near this write: ${node.ref}`),
        h('button', { class: 'icon', title: 'Close', onclick: closeBeside }, icon(ICON_CLOSE)),
      ),
      doc(node, { ref: false }),
    ),
  );
}

// A snippet's matched words sit between \u0002 and \u0003; each becomes a <mark>, and the rest stays text.
function marked(snippet) {
  return snippet.split('\u0002').flatMap((part, index) => {
    if (index === 0) return [part];
    const [hit, ...rest] = part.split('\u0003');
    return [h('mark', {}, hit), rest.join('')];
  });
}

function focusSearch() {
  state[sideKey()] = true;
  showSide();
  const input = $('search');
  input.focus();
  input.select();
}

function toggleSide() {
  state[sideKey()] = !state[sideKey()];
  showSide();
}

// The reading pane's setting where the user is. An inbox that keeps its own starts with the pane closed.
function sideKey() {
  return shownInbox().ownSide && state.view === 'review' ? `side:${state.inbox}` : 'side';
}

// The reading pane as the user set it where they are, and the queue column, unless the inbox in view lays out the main
// pane wider.
function showSide() {
  const layout = state.view === 'review' ? shownInbox().layout?.() : null;
  $('app').classList.toggle('no-queue', layout === 'list' || layout === 'bench');
  $('app').classList.toggle('no-side', layout === 'bench' || !state[sideKey()]);
}

function onSearchKey(event) {
  const rows = listed();
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    if (!rows.length) return;
    state.cursor = (state.cursor + (event.key === 'ArrowDown' ? 1 : -1) + rows.length) % rows.length;
    renderHits();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    const hit = rows[state.cursor];
    if (!hit) return;
    if (hit.about) openBeside(hit.about, hit.ref);
    else if (event.shiftKey) openSide(hit.ref);
    else openMain(hit.ref);
  } else if (event.key === 'Escape') {
    event.preventDefault();
    event.target.value = '';
    state.query = '';
    state.hits = [];
    renderHits();
    event.target.blur();
    $('main').focus();
  }
}

// ---- The command palette ----

function commands() {
  const others = registered.filter((inbox) => inbox !== shownInbox());
  const list = [
    { name: 'Node review', keys: ['Ctrl', 'P'], run: () => showInbox(NODE_REVIEW) },
    { name: 'Focus on a project', keys: ['Alt', 'P'], run: () => openPalette('projects') },
    focus() !== null && { name: 'Focus on every project', keys: [], run: () => setFocus(null) },
    { name: 'Search the graph', keys: ['/'], run: focusSearch },
    { name: state[sideKey()] ? 'Hide the reading pane' : 'Show the reading pane', keys: ['Ctrl', '\\'], run: toggleSide },
    state.stack.some((entry) => !entry.pinned) && { name: 'Close unpinned notes', keys: [], run: closeUnpinned },
    state.view === 'node' && { name: 'Back to the review', keys: ['Esc'], run: showReview },
    state.view === 'review' && state.beside && { name: 'Close the entry beside the write', keys: ['Esc'], run: closeBeside },
    others.length > 0 && { name: 'Next inbox', keys: ['L'], run: () => stepInbox(1) },
    others.length > 0 && { name: 'Previous inbox', keys: ['H'], run: () => stepInbox(-1) },
    ...others.map((inbox) => ({ name: `Show ${inbox.label}`, group: inbox.error?.() ? 'failing' : `${inbox.count(focus())} waiting`, keys: [], run: () => showInbox(inbox.name) })),
    state.sources.length > 0 && { name: 'Ask every source again', keys: ['F5'], run: askAgain },
  ].filter(Boolean);
  return [...list, ...chatCommands(), ...registered.flatMap((inbox) => inbox.commands?.() || [])];
}

// Node review's commands, while it is shown: the answers, the steps, and every write in focus to jump to.
function reviewCommands() {
  if (state.inbox !== NODE_REVIEW) return [];
  const reviewing = state.view === 'review' && current();
  const list = [
    reviewing && { name: 'Yes, land this write', keys: ['Y'], run: () => answer('yes') },
    reviewing && { name: 'No', keys: ['N'], run: () => answer('no') },
    reviewing && { name: 'No, and say what to change', keys: ['R'], run: startReason },
    reviewing && { name: 'Switch between the file and how it reads', keys: ['D'], run: toggleModes },
    visible().length > 1 && { name: 'Next write', keys: ['J'], run: () => step(1) },
    visible().length > 1 && { name: 'Previous write', keys: ['K'], run: () => step(-1) },
  ].filter(Boolean);
  return [...list, ...visible().map((write) => ({ name: title(write), group: write.lines[0] || 'Waiting', keys: [], run: () => select(write.id) }))];
}

// Node review's keys, while it is shown.
function onReviewKey(event) {
  if (state.inbox !== NODE_REVIEW || !plainKey(event)) return false;
  const actions = {
    j: () => state.view === 'review' && step(1),
    k: () => state.view === 'review' && step(-1),
    y: () => answer('yes'),
    n: () => answer('no'),
    r: startReason,
    d: toggleModes,
  };
  const action = actions[event.key.toLowerCase()];
  if (!action) return false;
  event.preventDefault();
  action();
  return true;
}

function matches(command, query) {
  const text = `${command.name} ${command.group || ''}`.toLowerCase();
  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => text.includes(word));
}

function paletteItems() {
  return state.palette.kind === 'projects'
    ? focusChoices().filter((choice) => fuzzy(choice.name, state.palette.query))
    : commands().filter((command) => matches(command, state.palette.query));
}

// The palette: the commands, or with kind projects the project picker, which opens on the project in focus.
function openPalette(kind = 'commands') {
  const picking = kind === 'projects';
  const at = picking ? [null, ...state.projects.map((graph) => graph.name)].indexOf(focus()) : 0;
  state.palette = { open: true, query: '', cursor: Math.max(0, at), kind };
  $('palette').hidden = false;
  const input = $('palette-input');
  input.value = '';
  input.placeholder = picking ? 'Focus on a project' : 'Type a command';
  input.setAttribute('aria-label', picking ? 'Project' : 'Command');
  renderPalette();
  bringCursorIntoView();
  input.focus();
}

function closePalette() {
  state.palette.open = false;
  $('palette').hidden = true;
  $('main').focus();
}

function renderPalette() {
  if (!state.palette.open) return;
  const items = paletteItems();
  state.palette.cursor = Math.min(state.palette.cursor, Math.max(0, items.length - 1));
  // A redraw leaves the list where it is: emptying it resets its scroll, so it is put back.
  const list = $('palette-list');
  const scrolled = list.scrollTop;
  if (state.palette.kind === 'projects') renderProjectRows(items);
  else renderCommandRows(items);
  list.scrollTop = scrolled;
}

function renderCommandRows(items) {
  fill($('palette-list'),
    ...(items.length
      ? items.map((command, index) =>
          h(
            'li',
            {
              class: 'row option palette-item',
              role: 'option',
              'aria-selected': String(index === state.palette.cursor),
              onmousedown: (event) => {
                event.preventDefault();
                runCommand(command);
              },
            },
            h('span', { class: 'palette-name' }, command.name),
            command.group ? h('span', { class: 'palette-group' }, command.group) : null,
            command.keys.length ? keys(...command.keys) : null,
          ),
        )
      : [h('li', { class: 'palette-none' }, 'No command matches.')]),
  );
}

// The project picker's list: a row per graph, which scrolls when it is longer than the box.
function renderProjectRows(choices) {
  fill($('palette-list'),
    choices.length
      ? choices.map((choice, index) => templateNode(projectRow({ ...choice, selected: index === state.palette.cursor, onChoose: () => runCommand(choice) })))
      : h('li', { class: 'palette-none' }, 'No project matches.'),
  );
}

// Only the keys that move the cursor bring its row into view; a redraw leaves the list where it is.
function bringCursorIntoView() {
  $('palette-list').querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
}

function runCommand(command) {
  closePalette();
  command.run();
}

function onPaletteKey(event) {
  const items = paletteItems();
  if (event.key === 'Escape') {
    event.preventDefault();
    closePalette();
  } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    if (!items.length) return;
    state.palette.cursor = (state.palette.cursor + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    renderPalette();
    bringCursorIntoView();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    if (items[state.palette.cursor]) runCommand(items[state.palette.cursor]);
  }
}

// ---- The image viewer ----

// The viewer is drawn over the window from state.imageViewer, and while it is open its keys are taken first.
function renderImageViewer() {
  const host = $('image-viewer');
  const viewer = state.imageViewer;
  if (!viewer) {
    render(nothing, host);
    return;
  }
  const changed = (command, args) => {
    imageViewerDo(viewer, command, args);
    renderImageViewer();
  };
  render(imageViewerView(viewer, {
    onZoom: (factor, x, y) => changed('zoom', { factor, x, y }),
    onPan: (dx, dy) => changed('pan', { dx, dy }),
  }), host);
  imageViewerPaint(viewer, host);
}

function openImageViewer(node) {
  state.imageViewer = imageViewer(node);
  renderImageViewer();
}

// Esc and Alt+R, with the zoom and pan keys, belong to the viewer while it is open: no other handler hears them, the
// inbox keys, the chat's and the fields' included, so it takes them before the page does.
function onImageViewerKey(event) {
  if (!state.imageViewer) return;
  const result = imageViewerKey(state.imageViewer, event);
  if (!result) return;
  event.preventDefault();
  event.stopImmediatePropagation();
  if (result === 'close') state.imageViewer = null;
  renderImageViewer();
}

window.addEventListener('keydown', onImageViewerKey, true);
document.addEventListener('open-image', (event) => openImageViewer(event.detail.node));

// ---- Keys ----

function onKey(event) {
  const ctrl = event.ctrlKey || event.metaKey;
  const key = event.key.toLowerCase();
  // A reload would close this window, since its key lives only in this page. The reload keys ask the sources again.
  if (event.key === 'F5' || (ctrl && key === 'r')) {
    event.preventDefault();
    if (!state.closed) askAgain();
    return;
  }
  if (state.closed) return;
  if (chatKey(event) || registered.some((inbox) => inbox.keys?.(event))) return;
  if (ctrl && key === 'k') {
    event.preventDefault();
    state.palette.open && state.palette.kind === 'commands' ? closePalette() : openPalette();
    return;
  }
  // Alt+P by the key's place, since Alt can change the letter a layout types.
  if (event.altKey && event.code === 'KeyP') {
    event.preventDefault();
    state.palette.open && state.palette.kind === 'projects' ? closePalette() : openPalette('projects');
    return;
  }
  // Alt+` by the key's place: it opens the settings page from anywhere, and closes it again.
  if (event.altKey && event.code === 'Backquote') {
    event.preventDefault();
    toggleSettings();
    return;
  }
  if (state.palette.open) return;
  if (ctrl && key === 'p') {
    event.preventDefault();
    showInbox(NODE_REVIEW);
    $('main').focus();
    return;
  }
  if (ctrl && event.key === '\\') {
    event.preventDefault();
    toggleSide();
    return;
  }
  const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
  if (event.target.id === 'reason-text') {
    if (event.key === 'Escape') {
      event.preventDefault();
      state.reasonFor = null;
      renderMain();
      $('main').focus();
    } else if (ctrl && event.key === 'Enter') {
      event.preventDefault();
      const words = state.reasonText.trim();
      if (!words) toast('Say what to change, or answer N for a plain no.');
      else answer('no', words);
    }
    return;
  }
  if (event.target.id === 'verdict-text') {
    onVerdictKey(event, ctrl);
    return;
  }
  if (typing || ctrl || event.altKey) return;
  switch (key) {
    case '/':
      event.preventDefault();
      focusSearch();
      break;
    case 'escape':
      if (state.view === 'node') showReview();
      else if (state.inbox === EVIDENCE && evidence.detail) closeEvidenceDetail();
      else if (state.beside) closeBeside();
      break;
    case 'h':
      if (registered.length > 1) stepInbox(-1);
      break;
    case 'l':
      if (registered.length > 1) stepInbox(1);
      break;
    default:
  }
}

let toastTimer = 0;

function toast(message) {
  const el = $('toast');
  el.textContent = message;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 3200);
}

function renderAll() {
  renderQueue();
  renderMain();
  renderHits();
  renderStack();
}

document.addEventListener('keydown', onKey);
$('search').addEventListener('input', onSearchInput);
$('search').addEventListener('keydown', onSearchKey);
$('palette-input').addEventListener('input', (event) => {
  state.palette.query = event.target.value;
  state.palette.cursor = 0;
  renderPalette();
});
$('palette-input').addEventListener('keydown', onPaletteKey);
$('palette').addEventListener('mousedown', (event) => {
  if (event.target === $('palette')) closePalette();
});
registerInbox({
  name: NODE_REVIEW,
  label: 'Node review',
  count: (graph) => state.items.filter((item) => belongsTo(item, graph)).length,
  list: writeList,
  context: reviewContext,
  render: renderReviewMain,
  keys: onReviewKey,
  commands: reviewCommands,
});
// The window launches once every script ran, the classic ones after this file and the modules alike. Launched here, the
// session could come back while a later script still loads, and launch draws every inbox with what that script defines.
document.addEventListener('DOMContentLoaded', launch);
