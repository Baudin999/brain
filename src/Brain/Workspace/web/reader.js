// The reader: the file open, in the main pane, drawn by the renderer that takes it. Markdown reads as brain draws it,
// its mermaid diagrams drawn and its links to other files followed as commands. Code and any other text read plain, in
// the monospace font, with no colouring by keyword, which leaves colour free for the marks the plugins give the file:
// a marked run of code lines takes its tone's wash behind its text and a bar in the margin, a marked block of markdown
// takes it whole, and a mark shows where lines were removed. The keys scroll the file and step through its links.
'use strict';

// How far a step of the keys scrolls, in pixels: about three lines.
const READER_STEP = 64;
const READER_PAGE = 0.9;

// What the reader drew last, so a file opened afresh starts at its top: the view's own memory, not the workspace's.
let readerShown = null;
// Each file's markdown element, and each file's runs of lines, made once per read of it and its marks.
const readerHtml = new WeakMap();
const readerLines = new WeakMap();

// A value made from a file and its marks, kept in cache until either changes.
function readerKept(cache, file, marks, make) {
  const kept = cache.get(file);
  if (kept && kept.marks === marks) return kept.value;
  const value = make();
  cache.set(file, { marks, value });
  return value;
}

// The lines of a text: the newline that ends the last line starts no line of its own.
function readerSplit(text) {
  const lines = text.split('\n');
  if (lines.length > 1 && lines[lines.length - 1] === '') lines.pop();
  return lines;
}

// Each line's tone from marks, null for a line unmarked, and the lines a gap sits before.
function readerTones(count, marks) {
  const tones = new Array(count).fill(null);
  for (const range of marks?.lines || []) {
    const to = Math.min(range.to, count);
    for (let at = range.from; at < to; at++) tones[at] = range.tone;
  }
  return { tones, gaps: new Set(marks?.gaps || []) };
}

// The tone of the lines from to to as one block: their own when they share one, changed when they differ or lines were
// removed inside them, null when none is marked.
function readerBlockTone(tones, gaps, from, to) {
  let tone = tones[from] ?? null;
  let marked = false;
  for (let at = from; at < to; at++) {
    if (tones[at]) marked = true;
    if (tones[at] !== tone) tone = 'changed';
    if (at > from && gaps.has(at)) tone = 'changed';
  }
  if (!marked && tone !== 'changed') return null;
  return tone;
}

// How the markdown marks a block of its lines: in its tone whole, and with the gap mark when lines were removed in the
// blank lines before it.
function readerBlockMark(marks, count) {
  const { tones, gaps } = readerTones(count, marks);
  return (lead, from, to) => {
    const classes = [];
    let attrs = '';
    const tone = readerBlockTone(tones, gaps, from, to);
    if (tone) {
      classes.push('git-change');
      attrs = ` data-change="${tone}"`;
    }
    for (let at = lead; at <= from; at++) {
      if (!gaps.has(at)) continue;
      classes.push('git-gap');
      break;
    }
    return { classes, attrs };
  };
}

// A file's lines as runs to draw: lines of one tone in a row as one run, with their text and their numbers. Where lines
// were removed, a gap run marks the place. Every line's text ends in its newline but the file's last, when the file has
// none.
function readerRuns(text, marks) {
  const lines = readerSplit(text);
  const { tones, gaps } = readerTones(lines.length, marks);
  const runs = [];
  const add = (tone, line, number) => {
    const last = runs[runs.length - 1];
    if (last && !last.gap && last.tone === tone) {
      last.lines.push(line);
      last.numbers.push(number);
      return;
    }
    runs.push({ tone, lines: [line], numbers: [number] });
  };
  for (let at = 0; at < lines.length; at++) {
    if (gaps.has(at)) runs.push({ gap: true });
    add(tones[at], lines[at], at + 1);
  }
  if (gaps.has(lines.length)) runs.push({ gap: true });
  if (!text.endsWith('\n')) {
    const last = runs.findLast((run) => !run.gap);
    if (last) last.open = true;
  }
  return runs;
}

// One column of a run: its lines each ending in a newline, but the file's last when it has none.
function readerColumn(items, open) {
  const joined = items.join('\n');
  if (open) return joined;
  return `${joined}\n`;
}

// A file read as markdown: its text lines wrap to the width of their pane.
function readerIsMarkdown(path) {
  return /\.(md|markdown)$/i.test(path);
}

// Code as runs: its line numbers beside its text, a marked run in its tone in both, and a gap mark across both where
// lines were removed. A run whose numbers are empty, as the lines a diff removed, shows none. With wrap, as for the
// lines of markdown, the text has no numbers beside it, since a wrapped line would fall out of step with its number,
// and a long line wraps to the width of its pane with its run's tone on the line as a whole.
function readerCode(runs, wrap = false) {
  const numbers = runs.map((run) => {
    if (run.gap) return html`<span class="git-run git-gap"></span>`;
    return html`<span class="git-run git-change margin" data-change=${run.tone ?? nothing}>${readerColumn(run.numbers, run.open)}</span>`;
  });
  const texts = runs.map((run) => {
    if (run.gap) return html`<span class="git-run git-gap"></span>`;
    return html`<span class="git-run git-change" data-change=${run.tone ?? nothing}>${readerColumn(run.lines, run.open)}</span>`;
  });
  if (wrap) {
    return html`<div class="ws-code" data-wrap><pre class="ws-text">${texts}</pre></div>`;
  }
  return html`<div class="ws-code"><pre class="ws-lines" aria-hidden="true">${numbers}</pre><pre class="ws-text">${texts}</pre></div>`;
}

// The path a link in the file at base leads to, from the root; null for a link that leaves the root or names a scheme.
function readerLink(base, target) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(target)) return null;
  const bare = target.replace(/[?#].*$/, '');
  if (bare === '') return base;
  let decoded = bare;
  try {
    decoded = decodeURIComponent(bare);
  } catch {
    // A target that is no escaped text is a path as written.
  }
  const parts = decoded.startsWith('/') ? [] : base.split('/').slice(0, -1);
  for (const part of decoded.split('/')) {
    if (part === '' || part === '.') continue;
    if (part !== '..') {
      parts.push(part);
      continue;
    }
    if (parts.length === 0) return null;
    parts.pop();
  }
  return parts.join('/');
}

function readerMarkdown(file, marks) {
  return readerKept(readerHtml, file, marks, () => {
    let mark = null;
    if (marks) mark = readerBlockMark(marks, file.text.split('\n').length);
    return markdownView(file.text, { links: (target) => readerLink(file.path, target), mark, className: 'ws-prose' });
  });
}

function readerPlain(file, marks) {
  return readerKept(readerLines, file, marks, () => readerRuns(file.text, marks));
}

function readerProblem(file) {
  if (file.problem === 'binary') return `A binary file of ${file.size} bytes. The workspace shows text only.`;
  if (file.problem === 'too large') return `${(file.size / 1048576).toFixed(1)} MB, more than the 2 MB the workspace shows.`;
  return 'This file is gone from disk.';
}

function readerView(ws) {
  if (!ws.open) {
    return html`<p class="ws-empty">No file is open.</p>`;
  }
  const file = ws.open;
  let body = html`<p class="ws-empty">${readerProblem(file)}</p>`;
  const renderer = rendererOf(file, ws);
  if (renderer) body = renderer.draw(file, ws);
  return html`
    <header class="pane-head compact"><h1 class="pane-title">${file.path}</h1></header>
    <div class="ws-reader" id="reader">${body}</div>
  `;
}

// A file opened afresh starts at its top.
function readerPainted(host) {
  const reader = host.querySelector('#reader');
  if (!reader) return;
  if (workspace.open.path !== readerShown) reader.scrollTop = 0;
  readerShown = workspace.open.path;
}

function readerScroll(ws, by) {
  ws.focus = 'reader';
  document.getElementById('reader')?.scrollBy({ top: by });
}

function readerPage(ws, direction) {
  const reader = document.getElementById('reader');
  if (!reader) return;
  readerScroll(ws, direction * reader.clientHeight * READER_PAGE);
}

function readerEdge(ws, bottom) {
  const reader = document.getElementById('reader');
  if (!reader) return;
  readerScroll(ws, bottom ? reader.scrollHeight : -reader.scrollHeight);
}

// Steps the keys' focus to the next link in the file, or the one before; Enter then follows it.
function readerStepLink(ws, delta) {
  ws.focus = 'reader';
  const links = [...document.querySelectorAll('#reader a[href]')];
  if (links.length === 0) throw new Refusal('this file links nowhere');
  const at = links.indexOf(document.activeElement);
  let next = at + delta;
  if (at < 0 && delta < 0) next = links.length - 1;
  links[(next + links.length) % links.length].focus();
}

// A link to another file opens it, as a command from the link.
function onReaderLink(event) {
  const link = event.target.closest?.('#reader a[data-path]');
  if (!link) return;
  event.preventDefault();
  execute('file.open', { path: link.dataset.path }, FROM.link);
}

plugin({
  name: 'reader',
  commands: [
    { name: 'reader.down', title: 'Reader: scroll down', run: (ws) => readerScroll(ws, READER_STEP) },
    { name: 'reader.up', title: 'Reader: scroll up', run: (ws) => readerScroll(ws, -READER_STEP) },
    { name: 'reader.pagedown', title: 'Reader: a page down', run: (ws) => readerPage(ws, 1) },
    { name: 'reader.pageup', title: 'Reader: a page up', run: (ws) => readerPage(ws, -1) },
    { name: 'reader.top', title: 'Reader: the top', run: (ws) => readerEdge(ws, false) },
    { name: 'reader.bottom', title: 'Reader: the end', run: (ws) => readerEdge(ws, true) },
    { name: 'reader.link.next', title: 'Reader: next link', run: (ws) => readerStepLink(ws, 1) },
    { name: 'reader.link.previous', title: 'Reader: link before', run: (ws) => readerStepLink(ws, -1) },
  ],
  keys: [
    { key: 'j', focus: 'reader', command: 'reader.down' },
    { key: 'ArrowDown', focus: 'reader', command: 'reader.down' },
    { key: 'k', focus: 'reader', command: 'reader.up' },
    { key: 'ArrowUp', focus: 'reader', command: 'reader.up' },
    { key: 'Space', focus: 'reader', command: 'reader.pagedown' },
    { key: 'd', focus: 'reader', command: 'reader.pagedown' },
    { key: 'Shift+Space', focus: 'reader', command: 'reader.pageup' },
    { key: 'u', focus: 'reader', command: 'reader.pageup' },
    { key: 'g', focus: 'reader', command: 'reader.top' },
    { key: 'G', focus: 'reader', command: 'reader.bottom' },
    { key: 'n', focus: 'reader', command: 'reader.link.next' },
    { key: 'N', focus: 'reader', command: 'reader.link.previous' },
  ],
  views: [{ slot: 'main', draw: readerView, painted: readerPainted }],
  renderers: [
    {
      name: 'markdown',
      test: (file) => file.text !== undefined && readerIsMarkdown(file.path),
      draw: (file, ws) => readerMarkdown(file, marksOf(ws, file)),
    },
    {
      name: 'plain',
      test: (file) => file.text !== undefined,
      draw: (file, ws) => readerCode(readerPlain(file, marksOf(ws, file))),
    },
  ],
});

document.addEventListener('click', onReaderLink);
