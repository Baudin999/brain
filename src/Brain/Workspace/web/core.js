// A workspace's window: one project folder, its file tree beside the file open in it, live as files change on disk,
// and all of it reachable from the keys.
//
// The core is five things, and everything else is a plugin on them. The workspace holds all state: the root, the file
// open, the focus, the layout and each plugin's own part. A command has a name, a title, a budget and a run against
// the workspace; a key, the bar, a click, a link in a file and a change on disk all end in executing one, and nothing
// changes the workspace another way. The keymap is data: a key, the focus it applies in, a command, and the arguments
// and the condition it takes from the workspace. A view draws from the workspace's state with lit-html and never
// changes it; a click in a view executes a command. A plugin registers commands, keys, views and renderers, which turn
// a file into a view, and what it knows of a path: its tone, such as changed, entries the folder lists not, such as a
// deleted file, and the marks of the file open. A plugin may also own a screen: while the workspace shows it, it fills
// the window and the tree and the reader are not drawn. The tree, the reader, the bar, the probe and git are plugins on
// this same API, all from this codebase.
//
// Every command executed is a record: its name, its arguments, its source, how long it took until the change was on
// screen, its outcome, and whether it went over its budget. A key paints within one frame; a file opens within 100 ms;
// the tree shows within 200 ms. The records go to the workspace's server, which logs them with its own, on this machine
// only.
//
// The launch, the calls, the event stream and the theme go through the shared window.js, as brain code's do.
'use strict';

const SESSION_KEY = 'workspace.session';
const FRAME_MS = 1000 / 60;
const RECORDS_SEND_MS = 10000;
// The most records the window keeps for its probe, its own and the server's.
const RECORDS_KEPT = 400;
// The foci that hold every key, as the plugins declare them: while the workspace is in one, only its own keys apply.
const holding = new Set();
// The sources a command is executed from.
const FROM = { key: 'key', bar: 'bar', click: 'click', link: 'link', file: 'file', start: 'start' };

// The workspace: all state there is. Each plugin keeps its own part under its name.
const workspace = {
  session: null,
  root: '',
  name: '',
  open: null,
  focus: 'tree',
  // The plugin whose screen fills the window, or null for the tree and the reader.
  screen: null,
  layout: { tree: true },
  history: [],
  problem: null,
  closed: null,
  // The records the probe shows, written by the core's telemetry only: each command's, and the server's while the
  // probe reads them.
  records: [],
};

const commands = new Map();
const keymap = [];
const views = [];
const renderers = [];
const refreshers = [];
const starters = [];
const openers = [];
const toners = [];
const entriers = [];
const markers = [];
// The window's records not yet sent to the server.
const unsent = [];

// A command refused: what it asked for is not there. Its outcome is refused, not failed.
class Refusal extends Error {}

// ---- Plugins ----

// plugin({ name, state, commands, keys, holds, views, renderers, refresh, start, opened, tone, entries, marks }) adds
// a plugin. state becomes the workspace's part under name. A command is { name, title, budget, run(workspace, args) };
// budget is in milliseconds, one frame unless it says, and Infinity for one that waits on the network. A key is { key,
// focus, command, args(workspace), when(workspace) }; focus is tree, reader, bar, a plugin's own or * for any. holds
// lists the plugin's own foci that hold every key, as a page over the whole window does: in one, only its keys apply.
// A view is { slot, name, draw(workspace), painted(host) }, its slot side, main, overlay, status or screen; a screen
// view is drawn while the workspace's screen is its name, instead of the side and the main slot. A renderer is { name,
// rank, test(file, workspace), draw(file, workspace) }; of those that take a file the highest rank draws it.
// refresh(workspace, changes) runs when files changed on disk or git moved, start(workspace) once the window has its
// session and opened(workspace) once a file opened. tone(workspace, path) says what a path is, such as changed, or null;
// entries(workspace, folder) lists entries a folder no longer holds on disk, such as a deleted file; marks(workspace,
// file) gives the open file's marked lines, { lines: [{ from, to, tone }], gaps: [line] }, lines counted from 0 and a gap
// before the line it names, or null.
function plugin(added) {
  if (added.state) workspace[added.name] = added.state;
  for (const command of added.commands || []) commands.set(command.name, command);
  keymap.push(...(added.keys || []));
  for (const focus of added.holds || []) holding.add(focus);
  views.push(...(added.views || []));
  renderers.push(...(added.renderers || []));
  if (added.refresh) refreshers.push(added.refresh);
  if (added.start) starters.push(added.start);
  if (added.opened) openers.push(added.opened);
  if (added.tone) toners.push(added.tone);
  if (added.entries) entriers.push(added.entries);
  if (added.marks) markers.push(added.marks);
}

// What the plugins say of a path: its tone, or null.
function toneOf(ws, path) {
  for (const toner of toners) {
    const tone = toner(ws, path);
    if (tone) return tone;
  }
  return null;
}

// The entries the plugins add to a folder, past those on disk.
function entriesOf(ws, folder) {
  return entriers.flatMap((entrier) => entrier(ws, folder));
}

// The marks the plugins give the file open, or null.
function marksOf(ws, file) {
  for (const marker of markers) {
    const marks = marker(ws, file);
    if (marks) return marks;
  }
  return null;
}

// Every command has a key: the names of those that have none.
function unkeyed() {
  return [...commands.keys()].filter((name) => !keymap.some((binding) => binding.command === name));
}

// ---- Commands ----

// Runs a command against the workspace, draws the page, and records it: until the frame that shows the change, or
// until it ended while the window is hidden and draws no frame.
async function execute(name, args = {}, source = FROM.key, at = performance.now()) {
  const command = commands.get(name);
  if (!command) {
    record({ name, args, source, ms: 0, outcome: 'refused', budget: null });
    return;
  }
  let outcome = 'ok';
  try {
    workspace.problem = null;
    await command.run(workspace, args);
  } catch (error) {
    outcome = 'failed';
    if (error instanceof Refusal) outcome = 'refused';
    workspace.problem = error.message;
  }
  draw();
  const shown = await frame();
  record({ name, args, source, ms: shown - at, outcome, budget: command.budget ?? FRAME_MS });
}

// The time the next frame is drawn, or now when the window is hidden and draws none. It is read as the frame runs: the
// time a frame carries is when it began, which may come before the key that changed it.
function frame() {
  if (document.hidden) return Promise.resolve(performance.now());
  return new Promise((resolve) => requestAnimationFrame(() => resolve(performance.now())));
}

function record({ name, args, source, ms, outcome, budget }) {
  const kept = {
    at: Date.now(),
    name,
    args: argsText(args),
    source,
    ms: Math.round(ms * 10) / 10,
    outcome,
    budget,
    over: budget !== null && ms > budget,
  };
  unsent.push(kept);
  keepRecord(kept);
}

// Keeps a record for the probe, the oldest going past the limit.
function keepRecord(kept) {
  workspace.records.push(kept);
  if (workspace.records.length > RECORDS_KEPT) workspace.records.shift();
}

function argsText(args) {
  const text = JSON.stringify(args);
  if (text === '{}') return '';
  return text;
}

// ---- Drawing ----

// Draws the page from the workspace: the tree's pane, the main pane, or the screen that fills the window instead, the
// status line and what lies over them.
function draw() {
  const host = document.getElementById('app');
  render(page(workspace), host);
  for (const view of views) view.painted?.(host);
}

function slot(name, ws) {
  return views.filter((view) => view.slot === name).map((view) => view.draw(ws));
}

function page(ws) {
  if (ws.closed) {
    return html`<div class="quiet"><h2>This window is closed</h2><p>${ws.closed}</p></div>`;
  }
  const screen = views.find((view) => view.slot === 'screen' && view.name === ws.screen);
  if (screen) {
    return html`
      <div class="ws-screen">
        ${screen.draw(ws)}
        <footer class="ws-status">${slot('status', ws)}</footer>
      </div>
      ${slot('overlay', ws)}
    `;
  }
  return html`
    <div class="ws" ?data-tree=${ws.layout.tree}>
      <nav class="ws-side" aria-label="Files" ?hidden=${!ws.layout.tree} ?data-focus=${ws.focus === 'tree'}>${slot('side', ws)}</nav>
      <main class="ws-main" ?data-focus=${ws.focus === 'reader'}>${slot('main', ws)}</main>
      <footer class="ws-status">${slot('status', ws)}</footer>
    </div>
    ${slot('overlay', ws)}
  `;
}

// The renderer that draws a file: of those that take it, the first of the highest rank, as the plugins registered them.
function rendererOf(file, ws) {
  let chosen = null;
  for (const renderer of renderers) {
    if (!renderer.test(file, ws)) continue;
    if (chosen && (renderer.rank ?? 0) <= (chosen.rank ?? 0)) continue;
    chosen = renderer;
  }
  return chosen;
}

// ---- The keys ----

// A key as the keymap names it: its modifiers, then the key, a letter as typed, so Shift+g is G.
function keyName(event) {
  const parts = [];
  if (event.ctrlKey) parts.push('Ctrl');
  if (event.altKey) parts.push('Alt');
  if (event.metaKey) parts.push('Meta');
  let key = event.key;
  if (key === ' ') key = 'Space';
  if (event.shiftKey && key.length > 1) parts.push('Shift');
  parts.push(key);
  return parts.join('+');
}

// The binding a key executes: the first for the focus whose condition holds, then the first for any focus. While the
// keys type into a box, or a page that covers the window holds them, only the focus's own bindings apply, so every
// other key types or does nothing.
function bindingOf(event) {
  const key = keyName(event);
  const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
  const holds = (binding) => binding.key === key && (!binding.when || binding.when(workspace));
  const focused = keymap.find((binding) => binding.focus === workspace.focus && holds(binding));
  if (focused || typing || holding.has(workspace.focus)) return focused;
  return keymap.find((binding) => binding.focus === '*' && holds(binding));
}

function onKey(event) {
  if (!workspace.session || workspace.closed) return;
  const binding = bindingOf(event);
  if (!binding) return;
  event.preventDefault();
  const args = binding.args ? binding.args(workspace) : {};
  execute(binding.command, args, FROM.key, event.timeStamp);
}

// ---- The server ----

// A call whose refusal throws: a refusal of what is not there, or of what git said no to, as a Refusal, anything else
// as a failure. With a body it posts it as JSON.
async function ask(path, sent) {
  let options = {};
  if (sent !== undefined) options = { method: 'POST', body: JSON.stringify(sent) };
  const { ok, status, body } = await windowCall(path, options);
  if (ok) return body;
  if (status === 404 || status === 409) throw new Refusal(body.error);
  throw new Error(body.error);
}

// The window's event stream: what changed on disk, a theme switch, and close once a newer window took over.
function listen() {
  return windowListen(heard, ended, () => workspace.closed !== null);
}

function heard(event, data) {
  if (event === 'changed') execute('workspace.refresh', JSON.parse(data), FROM.file);
  if (event === 'theme') windowSwapTheme(draw);
  if (event === 'close') shut('This workspace opened in a newer window.', true);
}

function ended(why) {
  if (why === 'session') {
    shut('This window’s session ended. Run brain workspace again.');
    return;
  }
  shut('The workspace’s server stopped. Run brain workspace again.', true);
}

// Shuts the window on a message; with close it closes itself too.
function shut(message, close) {
  if (workspace.closed) return;
  workspace.closed = message;
  workspace.session = null;
  windowForget(SESSION_KEY);
  if (close) window.close();
  draw();
}

// ---- The core's own commands ----

async function openFile(ws, { path }) {
  const file = await ask(`/api/file?path=${encodeURIComponent(path)}`);
  ws.screen = null;
  if (ws.open && ws.open.path !== path) ws.history.push(ws.open.path);
  ws.open = file;
  for (const opener of openers) await opener(ws);
}

async function back(ws) {
  const path = ws.history.pop();
  if (!path) throw new Refusal('nothing was open before');
  ws.screen = null;
  ws.open = await ask(`/api/file?path=${encodeURIComponent(path)}`);
  for (const opener of openers) await opener(ws);
}

// Files changed on disk, git moved, or a key asked: the open file reads again when it changed, and each plugin
// refreshes its part.
async function refresh(ws, changes) {
  const all = changes.all !== false;
  const paths = changes.paths || [];
  const git = changes.git === true;
  if (ws.open && (all || paths.includes(ws.open.path))) ws.open = await ask(`/api/file?path=${encodeURIComponent(ws.open.path)}`);
  for (const refresher of refreshers) await refresher(ws, { all, paths, git });
}

function nextFocus(ws) {
  if (ws.focus === 'tree' || !ws.layout.tree) {
    ws.focus = 'reader';
    return;
  }
  ws.focus = 'tree';
}

// Shows or hides the side column, the tree. Hidden, it holds the keys no more: they go to the reader. Only the bar, over
// both, keeps them.
function toggleTree(ws) {
  ws.layout.tree = !ws.layout.tree;
  if (ws.layout.tree) return;
  if (ws.focus === 'reader' || ws.focus === 'bar') return;
  ws.focus = 'reader';
}

function statusView(ws) {
  const open = ws.open ? ws.open.path : 'no file open';
  return html`
    <span class="ws-name">${ws.name}</span>
    <span class="ws-path">${open}</span>
    ${ws.problem ? html`<span class="ws-problem" role="status">${ws.problem}</span>` : nothing}
  `;
}

// The tree and the reader are on screen, not a plugin's screen.
const onWindow = (ws) => ws.screen === null;

plugin({
  name: 'core',
  commands: [
    { name: 'file.open', title: 'Open a file', budget: 100, run: openFile },
    { name: 'file.back', title: 'Open the file before', budget: 100, run: back },
    { name: 'workspace.refresh', title: 'Read the files again', budget: 200, run: refresh },
    { name: 'focus.next', title: 'Move to the other pane', run: nextFocus },
    { name: 'layout.tree', title: 'Show or hide the tree', run: toggleTree },
  ],
  keys: [
    { key: 'Backspace', focus: '*', command: 'file.back', when: onWindow },
    { key: 'F5', focus: '*', command: 'workspace.refresh', args: () => ({ all: true }) },
    { key: 'Tab', focus: 'tree', command: 'focus.next' },
    { key: 'Tab', focus: 'reader', command: 'focus.next' },
    { key: 'Ctrl+b', focus: '*', command: 'layout.tree', when: onWindow },
  ],
  views: [{ slot: 'status', draw: statusView }],
});

// ---- Start ----

async function start() {
  const session = await windowLaunch(SESSION_KEY);
  if (!session) {
    shut('This window has no key. Run brain workspace again.');
    return;
  }
  workspace.session = session.token;
  workspace.root = session.root;
  workspace.name = session.name;
  document.title = session.title;
  const missing = unkeyed();
  if (missing.length) console.error(`every command has a key, and these have none: ${missing.join(', ')}`);
  document.addEventListener('keydown', onKey);
  draw();
  listen();
  for (const starter of starters) await starter(workspace);
  await execute('workspace.refresh', { all: true }, FROM.start);
}

window.addEventListener('pagehide', () => {
  windowSendRecords(unsent, true);
  windowBye();
});
setInterval(() => windowSendRecords(unsent, false), RECORDS_SEND_MS);
document.addEventListener('DOMContentLoaded', start);
