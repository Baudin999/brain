// The bar: one box, opened with /, that finds both files and commands, like the command bar of macOS. What it finds is
// ranked by how closely the typed letters follow each other in a file's path or a command's title; Enter runs what the
// cursor is on. A command with a word in its title that starts with the query ranks above every file, and among those
// the commands whose key works where the bar was opened come first. Everything else ranks by its letters alone: the
// other commands a little above a file that matches as closely. A file it finds opens with file.open, the same command
// the tree's Enter runs, and each command shows its key, since every command has one.
'use strict';

const BAR_SHOWN = 50;
// What a command adds to its score when a word of its title starts with the query, and when its key also works where
// the bar was opened. The first is more than any file's score, so a command by word ranks above every file, and the
// second more than the letters' score, so one whose key works there ranks above the rest of those.
const BAR_WORD = 1000;
const BAR_RUNNABLE = 500;

// The bar's own commands, which the bar does not list among what it finds.
const BAR_OWN = ['bar.open', 'bar.close', 'bar.query', 'bar.down', 'bar.up', 'bar.run', 'file.open'];

// How closely the letters of query follow each other in text: more for letters in a row and at the start of a part of
// the path; -1 when text does not hold them all in order.
function barScore(text, query) {
  const lower = text.toLowerCase();
  let score = 0;
  let from = 0;
  let last = -2;
  for (const letter of query) {
    const found = lower.indexOf(letter, from);
    if (found < 0) return -1;
    score += 1;
    if (found === last + 1) score += 3;
    if (found === 0 || '/._- '.includes(lower[found - 1])) score += 2;
    last = found;
    from = found + 1;
  }
  return score - text.length / 100;
}

// A path scores as a whole, or by its file's name alone with a bonus, whichever is more: the letters typed are most
// often the file's own.
function barPathScore(path, query) {
  const whole = barScore(path, query);
  const name = barScore(path.slice(path.lastIndexOf('/') + 1), query);
  if (name < 0) return whole;
  return Math.max(whole, name + 2);
}

function barKey(name) {
  return keymap.find((binding) => binding.command === name)?.key ?? '';
}

// A command can run from where the bar was opened when one of its keys applies in that focus, or in any, and holds.
function barRunnable(ws, name) {
  const applies = (binding) => binding.focus === '*' || binding.focus === ws.bar.before;
  return keymap.some((binding) => binding.command === name && applies(binding) && (!binding.when || binding.when(ws)));
}

function barCommands(ws) {
  return [...commands.values()]
    .filter((command) => !BAR_OWN.includes(command.name))
    .map((command) => ({
      label: command.title,
      detail: command.name,
      key: barKey(command.name),
      command: command.name,
      args: {},
      runnable: barRunnable(ws, command.name),
    }));
}

// A word of the title starts with the query: a word is a run of letters.
function barWordStarts(title, query) {
  const words = title.toLowerCase().match(/[a-z]+/g) || [];
  return words.some((word) => word.startsWith(query));
}

function barFile(path) {
  const at = path.lastIndexOf('/');
  return { label: path.slice(at + 1), detail: path.slice(0, Math.max(0, at)), key: '', command: 'file.open', args: { path } };
}

// What the bar finds for its query: with none, every command and then the files opened before, newest first.
function barItems(ws) {
  const query = ws.bar.query.toLowerCase().replace(/\s+/g, '');
  if (query === '') {
    const recent = [...new Set([...ws.history].reverse())].map(barFile);
    return [...barCommands(ws), ...recent].slice(0, BAR_SHOWN);
  }
  const scored = [];
  for (const item of barCommands(ws)) {
    const score = barScore(item.label, query);
    if (score < 0) continue;
    let bonus = 1;
    if (barWordStarts(item.label, query)) {
      bonus = BAR_WORD;
      if (item.runnable) {
        bonus += BAR_RUNNABLE;
      }
    }
    scored.push({ item, score: score + bonus });
  }
  for (const path of ws.bar.files || []) {
    const score = barPathScore(path, query);
    if (score >= 0) scored.push({ item: barFile(path), score });
  }
  scored.sort((a, b) => b.score - a.score);
  return scored.slice(0, BAR_SHOWN).map((found) => found.item);
}

async function barLoad(ws) {
  const list = await ask('/api/files');
  ws.bar.files = list.paths;
}

async function barOpen(ws) {
  ws.bar.open = true;
  ws.bar.query = '';
  ws.bar.cursor = 0;
  if (ws.focus !== 'bar') ws.bar.before = ws.focus;
  ws.focus = 'bar';
  if (ws.bar.files === null) await barLoad(ws);
}

function barClose(ws) {
  ws.bar.open = false;
  ws.focus = ws.bar.before;
}

function barQuery(ws, { query }) {
  ws.bar.query = query;
  ws.bar.cursor = 0;
}

function barMove(ws, delta) {
  const count = barItems(ws).length;
  if (count === 0) return;
  ws.bar.cursor = (ws.bar.cursor + delta + count) % count;
}

// Runs what the cursor is on, or what a click names, as a command from the bar.
async function barRun(ws, { at }) {
  const item = barItems(ws)[at ?? ws.bar.cursor];
  if (!item) throw new Refusal('the bar finds nothing for that');
  barClose(ws);
  await execute(item.command, item.args, FROM.bar);
  // A file the bar found is there to be read.
  if (item.command === 'file.open') ws.focus = 'reader';
}

// A file made on disk joins what the bar finds; one changed is there already.
async function barRefresh(ws, { all, paths }) {
  if (ws.bar.files === null) return;
  const known = new Set(ws.bar.files);
  if (all || paths.some((path) => !known.has(path))) await barLoad(ws);
}

function barItem(ws, item, at) {
  return html`
    <li class="row option ws-bar-item" role="option" aria-selected=${String(at === ws.bar.cursor)} @click=${() => execute('bar.run', { at }, FROM.click)}>
      <span class="ws-bar-label">${item.label}</span>
      <span class="ws-bar-detail">${item.detail}</span>
      ${item.key ? html`<kbd>${item.key}</kbd>` : nothing}
    </li>
  `;
}

function barView(ws) {
  if (!ws.bar.open) return nothing;
  const items = barItems(ws);
  return html`
    <div class="palette">
      <div class="palette-box" role="dialog" aria-label="Find a file or a command">
        <input
          id="bar-input"
          class="field bare command"
          type="text"
          placeholder="Find a file or a command"
          autocomplete="off"
          spellcheck="false"
          aria-label="Find a file or a command"
          .value=${ws.bar.query}
          @input=${(event) => execute('bar.query', { query: event.target.value }, FROM.key, event.timeStamp)}
        >
        <ol class="palette-list" role="listbox">
          ${items.map((item, at) => barItem(ws, item, at))}
          ${items.length === 0 ? html`<li class="palette-none">Nothing matches.</li>` : nothing}
        </ol>
      </div>
    </div>
  `;
}

// The keys type into the bar while it is open, and its cursor stays in view.
function barPainted(host) {
  const input = host.querySelector('#bar-input');
  if (!input) return;
  if (document.activeElement !== input) input.focus();
  host.querySelector('.ws-bar-item[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
}

plugin({
  name: 'bar',
  state: { open: false, query: '', cursor: 0, files: null, before: 'tree' },
  commands: [
    { name: 'bar.open', title: 'Find a file or a command', budget: 200, run: barOpen },
    { name: 'bar.close', title: 'Close the bar', run: barClose },
    { name: 'bar.query', title: 'Find what is typed', run: barQuery },
    { name: 'bar.down', title: 'Bar: next', run: (ws) => barMove(ws, 1) },
    { name: 'bar.up', title: 'Bar: before', run: (ws) => barMove(ws, -1) },
    { name: 'bar.run', title: 'Bar: run what the cursor is on', budget: 100, run: barRun },
  ],
  keys: [
    { key: '/', focus: '*', command: 'bar.open' },
    { key: 'Escape', focus: 'bar', command: 'bar.close' },
    { key: 'Ctrl+u', focus: 'bar', command: 'bar.query', args: () => ({ query: '' }) },
    { key: 'ArrowDown', focus: 'bar', command: 'bar.down' },
    { key: 'Ctrl+n', focus: 'bar', command: 'bar.down' },
    { key: 'ArrowUp', focus: 'bar', command: 'bar.up' },
    { key: 'Ctrl+p', focus: 'bar', command: 'bar.up' },
    { key: 'Enter', focus: 'bar', command: 'bar.run' },
  ],
  views: [{ slot: 'overlay', draw: barView, painted: barPainted }],
  refresh: barRefresh,
});
