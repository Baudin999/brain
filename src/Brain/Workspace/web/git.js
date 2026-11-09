// Git: what is uncommitted against HEAD in the workspace's root, staged and unstaged together, and what git does not
// track yet. Outside the git view the tree colours each changed file by its state, added, changed, deleted or untracked,
// a folder that holds changes in theirs, and lists a deleted file where it was; the file open has its changed lines
// marked: a run of code in its state's wash with a bar in the margin, a block of markdown whole, and a mark where lines
// were removed.
//
// Git is also a screen of its own that fills the window, and Ctrl+g or Esc gives the tree and the reader back as they
// were. Its files are two lists side by side, unstaged on the left and staged on the right, and staging a file is moving
// it across with a button or a key; the diff of the file selected reads in the middle, the file in place with its removed
// lines red and its added lines green and the lines around them as they are; the commit's message and button sit under
// the lists, a textarea where Enter makes a new line and Ctrl+Enter commits; and the remote area on the right says the
// branch and its upstream, the commits a push would send and the ones behind it, and pushes the current branch to its
// upstream, never forced. What git said no to shows beside the button that asked. All of it follows the disk and the
// index through the watch, never by polling.
'use strict';

const GIT_SIDES = ['unstaged', 'staged'];
// How far a page of the diff scrolls, as a share of its height.
const GIT_PAGE = 0.9;

// The git screen is shown.
const gitShown = (ws) => ws.screen === 'git';

function gitParent(path) {
  const at = path.lastIndexOf('/');
  if (at < 0) return '';
  return path.slice(0, at);
}

// The files of one list: those with changes not staged, or those with changes staged. A file with both is in both.
function gitFiles(ws, side) {
  if (side === 'staged') return ws.git.files.filter((file) => file.staged);
  return ws.git.files.filter((file) => file.unstaged);
}

// Keeps each list's cursor on a file it still lists, the first when it is gone, and the keys on a list that has files.
function gitKeepCursors(ws) {
  for (const side of GIT_SIDES) {
    const files = gitFiles(ws, side);
    if (files.some((file) => file.path === ws.git.cursor[side])) continue;
    ws.git.cursor[side] = files[0]?.path ?? null;
  }
  if (ws.git.cursor[ws.git.side] !== null) return;
  const other = GIT_SIDES.find((side) => ws.git.cursor[side] !== null);
  if (other) ws.git.side = other;
}

// The file selected: the cursor of the list the keys are in, or null when both lists are empty.
function gitSelected(ws) {
  const path = ws.git.cursor[ws.git.side];
  if (path === null) return null;
  return { side: ws.git.side, path };
}

// Reads the status: each changed file by its path, each folder that holds changes with their state, or changed when
// they differ, and the cursors on files still listed.
async function gitStatus(ws) {
  const status = await ask('/api/git/status');
  const files = [...status.files].sort((a, b) => a.path.localeCompare(b.path));
  const byPath = new Map(files.map((file) => [file.path, file]));
  const folders = new Map();
  for (const file of files) {
    for (let folder = gitParent(file.path); folder !== ''; folder = gitParent(folder)) {
      const held = folders.get(folder);
      if (held && held !== file.state) folders.set(folder, 'changed');
      if (!held) folders.set(folder, file.state);
    }
  }
  Object.assign(ws.git, { repo: status.repo, branch: status.branch, upstream: status.upstream, ahead: status.ahead, behind: status.behind, cut: status.cut, files, byPath, folders });
  gitKeepCursors(ws);
}

// The open file's marks from its hunks: the lines a hunk adds, or changes where it also removed some, and a gap where a
// hunk only removed.
function gitHunkMarks(hunks) {
  const lines = [];
  const gaps = [];
  for (const hunk of hunks) {
    if (hunk.newCount === 0) {
      gaps.push(hunk.newStart);
      continue;
    }
    const tone = hunk.oldCount === 0 ? 'added' : 'changed';
    lines.push({ from: hunk.newStart - 1, to: hunk.newStart - 1 + hunk.newCount, tone });
  }
  return { lines, gaps };
}

// The marks of the file open, made afresh from the status and, for a file git tracks, its diff, which is read again only
// when fresh says the file or git moved. A new file is marked whole in its state.
async function gitMarks(ws, fresh) {
  const file = ws.open;
  const entry = file && ws.git.byPath.get(file.path);
  if (!entry) {
    ws.git.marks = null;
    return;
  }
  const kept = ws.git.marks;
  if (entry.state === 'added' || entry.state === 'untracked') {
    if (kept?.path === file.path && kept.state === entry.state) return;
    ws.git.marks = { path: file.path, state: entry.state, value: { lines: [{ from: 0, to: Infinity, tone: entry.state }], gaps: [] } };
    return;
  }
  if (!fresh && kept?.path === file.path && kept.state === entry.state) return;
  const diff = await ask(`/api/git/diff?path=${encodeURIComponent(file.path)}`);
  ws.git.marks = { path: file.path, state: entry.state, value: gitHunkMarks(diff.hunks) };
}

// A file opened afresh has its marks follow.
async function gitOpened(ws) {
  if (!ws.git.repo) return;
  await gitMarks(ws, true);
}

// The patch of the file selected, read again when fresh says the disk or git moved, or the selection did. A patch that
// arrives after the selection moved on is dropped.
async function gitPatch(ws, fresh) {
  const chosen = gitSelected(ws);
  if (!chosen) {
    ws.git.patch = null;
    return;
  }
  const staged = chosen.side === 'staged';
  const kept = ws.git.patch;
  if (!fresh && kept?.path === chosen.path && kept.staged === staged) return;
  const patch = await ask(`/api/git/patch?path=${encodeURIComponent(chosen.path)}&staged=${staged ? 1 : 0}`);
  const now = gitSelected(ws);
  if (now?.path !== chosen.path || now.side !== chosen.side) return;
  ws.git.patch = { ...patch, staged };
}

// The commits a push would send and those behind, when the branch has an upstream.
async function gitRemote(ws) {
  ws.git.remote = { ahead: [], behind: [] };
  if (!ws.git.upstream) return;
  try {
    ws.git.remote = await ask('/api/git/remote');
  } catch (error) {
    // An upstream git cannot resolve, as one deleted on the remote, has no commits to list.
    if (!(error instanceof Refusal)) throw error;
  }
}

// What the git screen shows, read again.
async function gitReload(ws) {
  await gitStatus(ws);
  await gitPatch(ws, true);
  await gitRemote(ws);
}

// Files changed on disk or git moved: the status reads again, the open file's diff when it or git moved, and the git
// screen's patch and remote while it is shown.
async function gitRefresh(ws, { all, paths, git }) {
  await gitStatus(ws);
  if (!ws.git.repo) return;
  await gitMarks(ws, all || git || paths.includes(ws.open?.path));
  if (!gitShown(ws)) return;
  const chosen = gitSelected(ws);
  await gitPatch(ws, all || git || paths.includes(chosen?.path));
  if (all || git) await gitRemote(ws);
}

function gitTone(ws, path) {
  const file = ws.git.byPath.get(path);
  if (file) return file.state;
  return ws.git.folders.get(path) ?? null;
}

// The files deleted from disk in folder, and the folders under it that only deleted files still hold.
function gitEntries(ws, folder) {
  const prefix = folder === '' ? '' : `${folder}/`;
  const entries = new Map();
  for (const file of ws.git.files) {
    if (file.state !== 'deleted' || !file.path.startsWith(prefix)) continue;
    const rest = file.path.slice(prefix.length);
    const at = rest.indexOf('/');
    const name = at < 0 ? rest : rest.slice(0, at);
    entries.set(name, { name, path: prefix + name, dir: at >= 0 });
  }
  return [...entries.values()];
}

function gitFileMarks(ws, file) {
  if (ws.git.marks?.path !== file.path) return null;
  return ws.git.marks.value;
}

// ---- The screen ----

// Shows the git screen over the tree and the reader, or gives them back as they were.
async function gitScreen(ws) {
  if (gitShown(ws)) {
    gitLeave(ws);
    return;
  }
  if (!ws.git.repo) throw new Refusal('the workspace is in no git repo');
  await gitEnter(ws);
}

async function gitEnter(ws) {
  ws.git.before = ws.focus === 'bar' ? 'tree' : ws.focus;
  ws.git.scrolled = {
    path: ws.open?.path ?? null,
    reader: document.getElementById('reader')?.scrollTop ?? 0,
    tree: document.querySelector('.ws-tree')?.scrollTop ?? 0,
  };
  ws.screen = 'git';
  ws.focus = 'git';
  await gitReload(ws);
}

function gitLeave(ws) {
  ws.screen = null;
  ws.focus = ws.git.before;
  ws.git.restore = ws.git.scrolled;
}

// ---- The lists ----

async function gitMove(ws, delta) {
  const files = gitFiles(ws, ws.git.side);
  if (files.length === 0) return;
  const at = files.findIndex((file) => file.path === ws.git.cursor[ws.git.side]);
  const next = Math.max(0, Math.min(files.length - 1, at + delta));
  ws.git.cursor[ws.git.side] = files[next].path;
  await gitPatch(ws, false);
}

// Puts the keys in the unstaged list or the staged one, on the file a click names or the one its cursor was on.
async function gitSide(ws, { side, path }) {
  if (path) ws.git.cursor[side] = path;
  if (ws.git.cursor[side] === null) throw new Refusal(`no file is ${side}`);
  ws.git.side = side;
  ws.focus = 'git';
  await gitPatch(ws, false);
}

// Moves the file selected across, from unstaged to staged or back; outside the git screen, stages the file open when it
// has changes not staged, or unstages it when all its changes are staged. The cursor stays on the place the file left.
async function gitStage(ws, args) {
  const here = gitShown(ws) && !args.path;
  let path = args.path ?? ws.open?.path ?? null;
  if (here) path = gitSelected(ws)?.path ?? null;
  const file = ws.git.byPath.get(path);
  if (!file) throw new Refusal(`${path ?? 'no file'} has no changes to stage`);
  let action = 'unstage';
  if (file.unstaged) action = 'stage';
  if (!here) {
    await ask(`/api/git/${action}`, { path });
    await gitStatus(ws);
    return;
  }
  const from = ws.git.side;
  if (from === 'staged') action = 'unstage';
  const at = gitFiles(ws, from).findIndex((one) => one.path === path);
  await ask(`/api/git/${action}`, { path });
  await gitStatus(ws);
  const to = GIT_SIDES.find((side) => side !== from);
  const left = gitFiles(ws, from);
  ws.git.cursor[to] = path;
  if (left.length > 0) {
    ws.git.cursor[from] = left[Math.min(at, left.length - 1)].path;
  } else {
    ws.git.side = to;
  }
  await gitPatch(ws, true);
}

// Closes the screen and reads the file selected.
async function gitOpenFile(ws) {
  const chosen = gitSelected(ws);
  if (!chosen) throw new Refusal('no file is listed');
  await openFile(ws, { path: chosen.path });
  ws.focus = 'reader';
}

// Reads the diff of the file open: the screen opens on it, in the list that holds its changes.
async function gitDiff(ws) {
  if (!ws.open) throw new Refusal('no file is open');
  const file = ws.git.byPath.get(ws.open.path);
  if (!file) throw new Refusal(`${ws.open.path} has no changes against HEAD`);
  const side = file.unstaged ? 'unstaged' : 'staged';
  ws.git.side = side;
  ws.git.cursor[side] = file.path;
  if (gitShown(ws)) {
    ws.focus = 'git';
    await gitPatch(ws, false);
    return;
  }
  await gitEnter(ws);
}

function gitScroll(ws, direction) {
  const diff = document.getElementById('git-diff');
  diff?.scrollBy({ top: direction * diff.clientHeight * GIT_PAGE });
}

// ---- The commit and the push ----

// Takes the message as typed, or puts the keys in its field.
async function gitMessage(ws, { message }) {
  if (message !== undefined) {
    ws.git.message = message;
    return;
  }
  if (!gitShown(ws)) await gitScreen(ws);
  ws.focus = 'message';
}

function gitList(ws) {
  ws.focus = 'git';
}

// Runs a commit or a push, and says beside its button what it did or why git said no.
async function gitSay(ws, area, act) {
  try {
    const done = await act();
    ws.git.said = { area, ok: true, text: done.said };
  } catch (error) {
    ws.git.said = { area, ok: false, text: error.message };
    throw error;
  }
  await gitReload(ws);
}

async function gitCommit(ws) {
  if (ws.git.message.trim() === '') {
    await gitMessage(ws, {});
    throw new Refusal('a commit needs a message');
  }
  if (!gitShown(ws)) await gitScreen(ws);
  await gitSay(ws, 'commit', () => ask('/api/git/commit', { message: ws.git.message }));
  ws.git.message = '';
  ws.focus = 'git';
}

async function gitPush(ws) {
  if (!gitShown(ws)) await gitScreen(ws);
  await gitSay(ws, 'push', () => ask('/api/git/push', {}));
}

// ---- The diff ----

const gitRunsOf = new WeakMap();

// The patch as runs the reader draws: lines of one kind in a row as one run, an added run in the added tone and a removed
// one in the deleted tone. An added or an unchanged line has its number in the file; a removed one has none.
function gitRuns(patch) {
  const kept = gitRunsOf.get(patch);
  if (kept) return kept;
  const runs = [];
  let number = 0;
  for (const line of patch.lines) {
    let tone = null;
    let shown = '';
    if (line.kind === 'added') tone = 'added';
    if (line.kind === 'removed') tone = 'deleted';
    if (line.kind !== 'removed') {
      number += 1;
      shown = number;
    }
    const last = runs[runs.length - 1];
    if (last && last.tone === tone) {
      last.lines.push(line.text);
      last.numbers.push(shown);
      continue;
    }
    runs.push({ tone, lines: [line.text], numbers: [shown] });
  }
  gitRunsOf.set(patch, runs);
  return runs;
}

// Why a patch has no lines. It names no size: the patch is not the file.
function gitProblem(problem) {
  if (problem === 'binary') return 'A binary file. The diff shows text only.';
  if (problem === 'too large') return 'A file of more than 2 MB. The diff shows less than that.';
  return 'This file is gone from disk.';
}

function gitDiffBody(ws) {
  const patch = ws.git.patch;
  if (!gitSelected(ws)) return html`<p class="ws-empty">Nothing is uncommitted.</p>`;
  if (!patch) return nothing;
  if (patch.problem) return html`<p class="ws-empty">${gitProblem(patch.problem)}</p>`;
  if (patch.lines.length === 0) return html`<p class="ws-empty">No line of this file changed.</p>`;
  return readerCode(gitRuns(patch), readerIsMarkdown(patch.path));
}

function gitDiffArea(ws) {
  const chosen = gitSelected(ws);
  let title = 'Diff';
  let where = '';
  if (chosen) {
    title = chosen.path;
    where = chosen.side;
  }
  return html`
    <section class="ws-git-diff" aria-label="Diff">
      <header class="pane-head compact"><h2 class="pane-title">${title}</h2><span class="ws-git-meta">${where}</span></header>
      <div class="ws-reader" id="git-diff">${gitDiffBody(ws)}</div>
    </section>
  `;
}

// ---- The lists, the commit and the remote ----

function gitRow(ws, side, file) {
  const selected = ws.git.side === side && file.path === ws.git.cursor[side];
  return html`
    <li role="none">
      <button
        class="row option git-file"
        type="button"
        role="option"
        tabindex="-1"
        data-change=${file.state}
        aria-selected=${String(selected)}
        @click=${() => execute('git.side', { side, path: file.path }, FROM.click)}
      ><span class="git-file-state">${file.state[0].toUpperCase()}</span><span class="git-file-path">${file.path}</span></button>
    </li>
  `;
}

function gitListView(ws, side, title, none) {
  const files = gitFiles(ws, side);
  let cut = nothing;
  if (side === 'unstaged' && ws.git.cut) {
    cut = html`<p class="ws-git-none" role="status">The list stops at its first ${ws.git.files.length} files; the tree colours only these.</p>`;
  }
  return html`
    <section class="ws-git-list" data-side=${side} aria-label=${title} ?data-focus=${!ws.away && ws.git.side === side && ws.focus === 'git'}>
      <header class="pane-head compact"><h2 class="pane-title">${title}</h2><span class="ws-git-meta">${files.length}</span></header>
      <ol class="ws-git-files" role="listbox" aria-label=${`${title} files`}>${files.map((file) => gitRow(ws, side, file))}</ol>
      ${files.length === 0 ? html`<p class="ws-git-none">${none}</p>` : nothing}
      ${cut}
    </section>
  `;
}

// The button between the lists: it moves the file selected to the list the arrow points at.
function gitToggleView(ws) {
  let arrow = '→';
  let label = 'Stage the file';
  if (ws.git.side === 'staged') {
    arrow = '←';
    label = 'Unstage the file';
  }
  return html`
    <div class="ws-git-toggle">
      <button class="button" type="button" tabindex="-1" aria-label=${label} @click=${() => execute('git.stage', {}, FROM.click)}>${arrow}</button>
    </div>
  `;
}

// A click into the message gives it the keys, as c does.
function onGitMessageFocus() {
  if (workspace.focus === 'message') return;
  execute('git.message', {}, FROM.click);
}

function gitSaid(ws, area) {
  const said = ws.git.said;
  if (said?.area !== area) return nothing;
  return html`<p class="ws-git-said" role="status" ?data-failed=${!said.ok}>${said.text}</p>`;
}

function gitCommitView(ws) {
  return html`
    <div class="ws-git-commit">
      <div class="ws-git-commit-bar">
        <textarea
          id="git-message"
          class="field mono"
          placeholder="The commit's message"
          spellcheck="false"
          aria-label="The commit's message"
          .value=${ws.git.message}
          @focus=${onGitMessageFocus}
          @input=${(event) => execute('git.message', { message: event.target.value }, FROM.key, event.timeStamp)}
        ></textarea>
        <button class="button" type="button" tabindex="-1" @click=${() => execute('git.commit', {}, FROM.click)}>Commit</button>
      </div>
      ${gitSaid(ws, 'commit')}
    </div>
  `;
}

function gitCommitRow(commit) {
  return html`<li class="git-commit"><span class="git-commit-hash">${commit.hash}</span><span class="git-commit-subject">${commit.subject}</span></li>`;
}

function gitCommits(title, commits, none) {
  let body = html`<p class="ws-git-none">${none}</p>`;
  if (commits.length > 0) body = html`<ul class="ws-git-commits">${commits.map(gitCommitRow)}</ul>`;
  return html`<h3 class="ws-git-heading">${title} <span class="ws-git-meta">${commits.length}</span></h3>${body}`;
}

function gitBranch(ws) {
  if (!ws.git.branch) return '';
  if (!ws.git.upstream) return `${ws.git.branch} has no upstream; set one with git push -u once`;
  return `${ws.git.branch} → ${ws.git.upstream}`;
}

function gitRemoteView(ws) {
  const { ahead, behind } = ws.git.remote;
  return html`
    <section class="ws-git-remote" aria-label="Remote">
      <header class="pane-head compact"><h2 class="pane-title">Remote</h2></header>
      <div class="ws-git-remote-body">
        <p class="ws-git-branch">${gitBranch(ws)}</p>
        ${gitCommits('To push', ahead, 'Nothing to push.')}
        ${behind.length > 0 ? gitCommits('Behind upstream', behind, '') : nothing}
        <div class="ws-git-push">
          <button class="button" type="button" tabindex="-1" @click=${() => execute('git.push', {}, FROM.click)}>Push</button>
        </div>
        ${gitSaid(ws, 'push')}
      </div>
    </section>
  `;
}

function gitScreenView(ws) {
  return html`
    <div class="ws-git-view">
      ${gitListView(ws, 'unstaged', 'Unstaged', 'Nothing to stage.')}
      ${gitToggleView(ws)}
      ${gitListView(ws, 'staged', 'Staged', 'Nothing is staged.')}
      ${gitCommitView(ws)}
      ${gitDiffArea(ws)}
      ${gitRemoteView(ws)}
    </div>
  `;
}

// The keys type into the message while it has them and leave it once they go; the cursor's row stays in view; a diff
// newly shown starts at its first change; and the tree and the reader come back where they were scrolled.
function gitPainted(host) {
  const input = host.querySelector('#git-message');
  if (input && workspace.focus === 'message' && document.activeElement !== input) input.focus();
  if (input && workspace.focus !== 'message' && document.activeElement === input) input.blur();
  host.querySelector('.row.git-file[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  gitPaintedDiff(host);
  gitPaintedBack(host);
}

// What the diff area showed last, so a diff newly shown starts at its first change, and a refresh leaves its place.
let gitDiffShown = null;

function gitPaintedDiff(host) {
  const diff = host.querySelector('#git-diff');
  const patch = workspace.git.patch;
  if (!diff || !patch) {
    gitDiffShown = null;
    return;
  }
  const key = `${patch.path} ${patch.staged}`;
  if (key === gitDiffShown) return;
  gitDiffShown = key;
  const first = diff.querySelector('.ws-text [data-change]');
  diff.scrollTop = 0;
  if (first) diff.scrollTop = Math.max(0, first.offsetTop - diff.clientHeight / 3);
}

function gitPaintedBack(host) {
  const back = workspace.git.restore;
  if (!back || workspace.screen !== null) return;
  workspace.git.restore = null;
  const reader = host.querySelector('#reader');
  if (reader && back.path === workspace.open?.path) reader.scrollTop = back.reader;
  const tree = host.querySelector('.ws-tree');
  if (tree) tree.scrollTop = back.tree;
}

plugin({
  name: 'git',
  state: {
    repo: false,
    branch: null,
    upstream: null,
    ahead: 0,
    behind: 0,
    cut: false,
    files: [],
    byPath: new Map(),
    folders: new Map(),
    marks: null,
    // The list the keys are in, the file each list has the cursor on, and the patch of the file selected.
    side: 'unstaged',
    cursor: { unstaged: null, staged: null },
    patch: null,
    remote: { ahead: [], behind: [] },
    message: '',
    said: null,
    // The focus the screen took over from, and how the tree and the reader were scrolled, to give back.
    before: 'tree',
    scrolled: null,
    restore: null,
  },
  commands: [
    { name: 'git.screen', title: 'Git: show or leave the view', budget: 200, run: gitScreen },
    { name: 'git.down', title: 'Git: next file', budget: 100, run: (ws) => gitMove(ws, 1) },
    { name: 'git.up', title: 'Git: file before', budget: 100, run: (ws) => gitMove(ws, -1) },
    { name: 'git.side', title: 'Git: select a file, or move to the unstaged or the staged list', budget: 100, run: gitSide },
    { name: 'git.stage', title: 'Git: stage or unstage the file', budget: 200, run: gitStage },
    { name: 'git.open', title: 'Git: read the file selected', budget: 100, run: gitOpenFile },
    { name: 'git.scroll.down', title: 'Git: a page of the diff down', run: (ws) => gitScroll(ws, 1) },
    { name: 'git.scroll.up', title: 'Git: a page of the diff up', run: (ws) => gitScroll(ws, -1) },
    { name: 'git.message', title: 'Git: write the commit message', budget: 200, run: gitMessage },
    { name: 'git.list', title: 'Git: back to the changed files', run: gitList },
    { name: 'git.commit', title: 'Git: commit what is staged', budget: Infinity, run: gitCommit },
    { name: 'git.push', title: 'Git: push the branch to its upstream', budget: Infinity, run: gitPush },
    { name: 'git.diff', title: 'Git: the diff of the file open', budget: 200, run: gitDiff },
  ],
  keys: [
    { key: 'Ctrl+g', focus: '*', command: 'git.screen' },
    { key: 'Ctrl+g', focus: 'message', command: 'git.screen' },
    { key: 'Escape', focus: 'git', command: 'git.screen' },
    { key: 'j', focus: 'git', command: 'git.down' },
    { key: 'ArrowDown', focus: 'git', command: 'git.down' },
    { key: 'k', focus: 'git', command: 'git.up' },
    { key: 'ArrowUp', focus: 'git', command: 'git.up' },
    { key: 'h', focus: 'git', command: 'git.side', args: () => ({ side: 'unstaged' }) },
    { key: 'ArrowLeft', focus: 'git', command: 'git.side', args: () => ({ side: 'unstaged' }) },
    { key: 'l', focus: 'git', command: 'git.side', args: () => ({ side: 'staged' }) },
    { key: 'ArrowRight', focus: 'git', command: 'git.side', args: () => ({ side: 'staged' }) },
    { key: 's', focus: 'git', command: 'git.stage' },
    { key: 'Space', focus: 'git', command: 'git.stage' },
    { key: 'Enter', focus: 'git', command: 'git.stage' },
    { key: 'Alt+s', focus: '*', command: 'git.stage' },
    { key: 'o', focus: 'git', command: 'git.open' },
    { key: 'd', focus: 'git', command: 'git.scroll.down' },
    { key: 'u', focus: 'git', command: 'git.scroll.up' },
    { key: 'Tab', focus: 'git', command: 'git.message' },
    { key: 'c', focus: 'git', command: 'git.message' },
    { key: 'Ctrl+u', focus: 'message', command: 'git.message', args: () => ({ message: '' }) },
    { key: 'Escape', focus: 'message', command: 'git.list' },
    { key: 'Tab', focus: 'message', command: 'git.list' },
    { key: 'Ctrl+Enter', focus: 'message', command: 'git.commit' },
    { key: 'C', focus: 'git', command: 'git.commit' },
    { key: 'P', focus: 'git', command: 'git.push' },
    { key: 'Alt+d', focus: '*', command: 'git.diff' },
  ],
  views: [{ slot: 'screen', name: 'git', draw: gitScreenView, painted: gitPainted }],
  refresh: gitRefresh,
  opened: gitOpened,
  tone: gitTone,
  entries: gitEntries,
  marks: gitFileMarks,
});
