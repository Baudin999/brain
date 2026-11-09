// The tree: the workspace's folder as a tree of its files, in the pane on the left. A folder lists its entries once it
// is open, folders first, without what git ignores; the keys move a cursor over the rows, open a folder or a file, and
// close a folder. A change on disk lists again each open folder it touched. Each row takes the tone the plugins give its
// path, as git colours a changed file and a folder that holds one, and a folder also lists the entries the plugins add,
// as git adds a file deleted from disk.
'use strict';

// A folder's entries: those on disk and those the plugins add, folders first, each group by name.
function treeEntries(ws, folder) {
  const listed = ws.tree.listings[folder] || [];
  const names = new Set(listed.map((entry) => entry.name));
  const added = entriesOf(ws, folder).filter((entry) => !names.has(entry.name));
  if (added.length === 0) return listed;
  const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });
  const all = [...listed, ...added];
  return [...all.filter((entry) => entry.dir).sort(byName), ...all.filter((entry) => !entry.dir).sort(byName)];
}

// The rows the tree shows: every entry of the root, and of each open folder under its folder, each with its depth.
function treeRows(ws) {
  const rows = [];
  const add = (folder, depth) => {
    for (const entry of treeEntries(ws, folder)) {
      const open = entry.dir && ws.tree.expanded.includes(entry.path);
      rows.push({ ...entry, depth, open });
      if (open) add(entry.path, depth + 1);
    }
  };
  add('', 0);
  return rows;
}

function treeCursor(ws) {
  return treeRows(ws).find((row) => row.path === ws.tree.cursor) || null;
}

function treeParent(path) {
  const at = path.lastIndexOf('/');
  if (at < 0) return '';
  return path.slice(0, at);
}

async function treeList(ws, folder) {
  const listing = await ask(`/api/tree?path=${encodeURIComponent(folder)}`);
  ws.tree.listings[folder] = listing.entries;
}

// Moves the cursor by delta rows, staying on the first or the last.
function treeMove(ws, delta) {
  const rows = treeRows(ws);
  if (rows.length === 0) return;
  const at = rows.findIndex((row) => row.path === ws.tree.cursor);
  const next = Math.max(0, Math.min(rows.length - 1, at + delta));
  ws.tree.cursor = rows[next].path;
  ws.focus = 'tree';
}

function treeEdge(ws, last) {
  const rows = treeRows(ws);
  if (rows.length === 0) return;
  ws.tree.cursor = rows[last ? rows.length - 1 : 0].path;
}

// Opens a folder, listing it first; a folder gone from disk lists only the entries the plugins add.
async function treeOpen(ws, path) {
  if (!ws.tree.listings[path]) {
    try {
      await treeList(ws, path);
    } catch (error) {
      if (!(error instanceof Refusal)) throw error;
      ws.tree.listings[path] = [];
    }
  }
  if (!ws.tree.expanded.includes(path)) ws.tree.expanded.push(path);
}

function treeClose(ws, path) {
  ws.tree.expanded = ws.tree.expanded.filter((open) => open !== path && !open.startsWith(`${path}/`));
}

// Opens or closes a folder: the one a click names, or the one under the cursor.
async function treeToggle(ws, { path }) {
  const folder = path ?? ws.tree.cursor;
  ws.tree.cursor = folder;
  ws.focus = 'tree';
  if (ws.tree.expanded.includes(folder)) {
    treeClose(ws, folder);
    return;
  }
  await treeOpen(ws, folder);
}

async function treeExpand(ws) {
  const row = treeCursor(ws);
  if (!row || !row.dir) return;
  if (row.open) {
    treeMove(ws, 1);
    return;
  }
  await treeOpen(ws, row.path);
}

function treeCollapse(ws) {
  const row = treeCursor(ws);
  if (!row) return;
  if (row.open) {
    treeClose(ws, row.path);
    return;
  }
  const parent = treeParent(row.path);
  if (parent) ws.tree.cursor = parent;
}

// Lists again each open folder a change touched, or every one; a folder gone closes unless the plugins still list
// entries in it, and the cursor stays on a row.
async function treeRefresh(ws, { all, paths }) {
  const folders = new Set(['', ...ws.tree.expanded]);
  if (!all) {
    const touched = new Set(paths.flatMap((path) => [treeParent(path), path]));
    for (const folder of folders) {
      if (!touched.has(folder)) folders.delete(folder);
    }
  }
  for (const folder of folders) {
    try {
      await treeList(ws, folder);
    } catch (error) {
      if (!(error instanceof Refusal)) throw error;
      if (entriesOf(ws, folder).length > 0) {
        ws.tree.listings[folder] = [];
        continue;
      }
      delete ws.tree.listings[folder];
      treeClose(ws, folder);
    }
  }
  if (!treeCursor(ws)) treeEdge(ws, false);
}

function treeClick(row) {
  if (row.dir) {
    execute('tree.toggle', { path: row.path }, FROM.click);
    return;
  }
  execute('file.open', { path: row.path }, FROM.click);
}

function treeRow(ws, row) {
  const indent = Array.from({ length: row.depth }, () => html`<span class="tree-indent"></span>`);
  let twisty = '';
  if (row.dir) twisty = row.open ? '▾' : '▸';
  return html`
    <li role="none">
      <button
        class="row option tree"
        type="button"
        role="treeitem"
        tabindex="-1"
        aria-selected=${String(row.path === ws.tree.cursor)}
        aria-current=${String(row.path === ws.open?.path)}
        aria-expanded=${row.dir ? String(row.open) : nothing}
        data-change=${toneOf(ws, row.path) ?? nothing}
        @click=${() => treeClick(row)}
      >${indent}<span class="tree-twisty">${twisty}</span><span class="tree-name">${row.name}</span></button>
    </li>
  `;
}

function treeView(ws) {
  return html`
    <header class="pane-head compact"><h2 class="pane-title">${ws.name}</h2></header>
    <ol class="ws-tree" role="tree" aria-label=${ws.name}>${treeRows(ws).map((row) => treeRow(ws, row))}</ol>
  `;
}

// The cursor's row stays in view.
function treePainted(host) {
  host.querySelector('.row.tree[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
}

const onFolder = (ws) => treeCursor(ws)?.dir === true;
const onFile = (ws) => treeCursor(ws)?.dir === false;

plugin({
  name: 'tree',
  state: { listings: {}, expanded: [], cursor: null },
  commands: [
    { name: 'tree.down', title: 'Tree: next row', run: (ws) => treeMove(ws, 1) },
    { name: 'tree.up', title: 'Tree: row before', run: (ws) => treeMove(ws, -1) },
    { name: 'tree.top', title: 'Tree: first row', run: (ws) => treeEdge(ws, false) },
    { name: 'tree.bottom', title: 'Tree: last row', run: (ws) => treeEdge(ws, true) },
    { name: 'tree.toggle', title: 'Tree: open or close the folder', budget: 200, run: treeToggle },
    { name: 'tree.expand', title: 'Tree: open the folder', budget: 200, run: treeExpand },
    { name: 'tree.collapse', title: 'Tree: close the folder, or go to it', run: treeCollapse },
  ],
  keys: [
    { key: 'j', focus: 'tree', command: 'tree.down' },
    { key: 'ArrowDown', focus: 'tree', command: 'tree.down' },
    { key: 'k', focus: 'tree', command: 'tree.up' },
    { key: 'ArrowUp', focus: 'tree', command: 'tree.up' },
    { key: 'g', focus: 'tree', command: 'tree.top' },
    { key: 'G', focus: 'tree', command: 'tree.bottom' },
    { key: 'l', focus: 'tree', command: 'tree.expand' },
    { key: 'ArrowRight', focus: 'tree', command: 'tree.expand' },
    { key: 'h', focus: 'tree', command: 'tree.collapse' },
    { key: 'ArrowLeft', focus: 'tree', command: 'tree.collapse' },
    { key: 'Enter', focus: 'tree', command: 'tree.toggle', when: onFolder },
    { key: 'Enter', focus: 'tree', command: 'file.open', when: onFile, args: (ws) => ({ path: ws.tree.cursor }) },
  ],
  views: [{ slot: 'side', draw: treeView, painted: treePainted }],
  refresh: treeRefresh,
});
