// The inboxes beside node review: other tools' questions for the user to judge, one inbox per source in sources.tsv.
//
// A source's inbox lists its questions under their groups, each row led by the question's title. A question opens as a
// bench: one question, one screen, read top to bottom with nothing beside it. The problem leads, the title and under it
// the first text block; then the code, a diff side by side or a code block with its focus lines lit; then each further
// text block in full as a voice under its caption; then one closed Details fold with the small print and every facts
// block; then the choices. A source sends data only, and this page draws every block of it: text, code with marks and a
// tally, a diff over several files, claims, facts, a timeline of steps that each hold blocks, and a block of a type
// brain does not know, shown as such. A field brain does not draw is shown too, never dropped. A caption, a tag, a label
// and a fact's value are the source's text, and brain knows the meaning of none of them. The answer goes to brain serve
// only on the user's own keys or clicks. app.js loads after this file and calls into it; everything here runs only once the window has a
// session.
'use strict';

// Lines of unchanged code kept around a change before the rest folds away.
const CONTEXT = 3;
// A fold shorter than this shows its lines instead: a fold row would hide less than it costs to read.
const MIN_FOLD = 4;
const CHOICE_KEYS = 9;

// The fields brain draws for each block type and each part of one; any other field shows as the source sent it.
const SHARED_FIELDS = ['type', 'caption'];
const BLOCK_FIELDS = {
  text: [...SHARED_FIELDS, 'text'],
  code: [...SHARED_FIELDS, 'path', 'startLine', 'code', 'highlights', 'marks', 'tally'],
  diff: [...SHARED_FIELDS, 'files'],
  claims: [...SHARED_FIELDS, 'claims'],
  facts: [...SHARED_FIELDS, 'facts', 'agreement'],
  timeline: [...SHARED_FIELDS, 'steps'],
};
const MARK_FIELDS = ['start', 'end', 'label', 'weight', 'note', 'text', 'marks', 'queries', 'section'];
const TALLY_FIELDS = ['label', 'base', 'value'];
const FACT_FIELDS = ['label', 'value', 'tone'];
const STEP_FIELDS = ['label', 'when', 'tone', 'blocks'];

const diffRows = new WeakMap();
// The pages that host a source in a tab of their own, by the source's name: { wrap, actions }, see hostSource.
const sourceHosts = {};

// ---- The sources ----

// A page hosts a source in a tab of its own: wrap(inbox) gets the source's inbox and returns the tab, which keeps the
// source's questions and adds what the page shows beside them, and actions(q) gives what an open question's head adds,
// a node or null. A source with no host is an inbox of its own.
function hostSource(name, wrap, actions) {
  sourceHosts[name] = { wrap, actions };
}

function sourceTab(name) {
  const inbox = sourceInbox(name);
  const hosted = sourceHosts[name];
  return hosted ? hosted.wrap(inbox) : inbox;
}

async function loadQuestions() {
  if (state.asking) return;
  state.asking = true;
  renderQueue();
  try {
    const { ok, body } = await call('/api/questions');
    if (ok) {
      for (const gone of state.sources.filter((old) => !body.some((source) => source.name === old.name))) dropInbox(gone.name);
      state.sources = body.map((source) => ({
        name: source.name,
        error: source.error,
        questions: source.questions.map((view) => ({ ...view.question, tags: view.question.tags || [], graphs: view.graphs, drift: view.drift, sums: view.sums, agreements: view.agreements })),
      }));
      for (const source of state.sources) registerInbox(sourceTab(source.name));
    } else toast(body.error);
  } finally {
    state.asking = false;
    for (const source of state.sources) {
      if (state.tags[source.name] && !source.questions.some((q) => q.tags.includes(state.tags[source.name]))) delete state.tags[source.name];
    }
    renderQueue();
    if (state.view === 'review' && state.inbox !== NODE_REVIEW) renderMain();
    renderPalette();
  }
}

function askAgain() {
  toast('Asking every source again.');
  loadProjects();
  loadQuestions();
}

// A source's inbox in the strip. Its questions are read by name on every call, so asking again changes them in place.
function sourceInbox(name) {
  const source = () => state.sources.find((one) => one.name === name);
  return {
    name,
    label: name,
    count: (graph) => source().questions.filter((q) => belongsTo(q, graph)).length,
    error: () => source().error,
    list: () => [tagFilter(source()), questionList(source())],
    render: renderQuestionMain,
    layout: () => questionLayout(source()),
    keys: (event) => state.inbox === name && onQuestionKey(event),
    commands: () => (state.inbox === name ? questionCommands(source()) : []),
    context: () => questionContext(source()),
  };
}

// The question in view for a chat, as the generic blocks the page draws; brain knows nothing of what the source means.
function questionContext(source) {
  const q = source.error ? null : currentQuestion(source);
  if (!q) return null;
  const { title, tags, repo, created, choices, drift, sums, agreements, blocks } = q;
  return [
    `The user has this open problem in view from ${source.name}, an inbox of another tool's questions, sent as the blocks brain draws. You may explain every side of it. The judgment is the user's click, and you never make it.`,
    JSON.stringify({ title, tags, repo, created, choices, drift, sums, agreements, blocks }, null, 2),
  ].join('\n\n');
}

function currentSource() {
  return state.sources.find((source) => source.name === state.inbox) || null;
}

function questionTagShown(source, q) {
  const tag = state.tags[source.name];
  return !tag || q.tags.includes(tag);
}

// The questions an inbox shows: those of the project in focus, and of the tag, when one is chosen, group by group.
function visibleQuestions(source) {
  return groupsOf(source.questions.filter((q) => inFocus(q) && questionTagShown(source, q))).flatMap((group) => group.questions);
}

// The questions under their groups, each group where its first question stands; those without a group in one unnamed
// group, its name ''.
function groupsOf(list) {
  const groups = new Map();
  for (const q of list) {
    const name = q.group ?? '';
    if (!groups.has(name)) groups.set(name, []);
    groups.get(name).push(q);
  }
  return [...groups].map(([name, questions]) => ({ name, questions }));
}

// True while the inbox shows the question its cursor is on, as a bench; false while it lists them all.
function isOpen(source) {
  return Boolean(state.opened[source.name]) && !source.error && Boolean(currentQuestion(source));
}

// The main pane lists the inbox while no question is open, and holds the bench alone while one is.
function questionLayout(source) {
  if (isOpen(source)) return 'bench';
  return source.error ? null : 'list';
}

function currentQuestion(source = currentSource()) {
  if (!source) return null;
  const list = visibleQuestions(source);
  return list.find((q) => q.id === state.picks[source.name]) || list[0] || null;
}

// ---- One source's queue ----

function tagFilter(source) {
  const counts = new Map();
  for (const q of source.questions.filter(inFocus)) for (const tag of q.tags) counts.set(tag, (counts.get(tag) || 0) + 1);
  if (!counts.size) return null;
  const chosen = state.tags[source.name];
  return h(
    'div',
    { class: 'tag-filter', role: 'group', 'aria-label': `Show the questions of one ${source.name} tag` },
    [...counts].map(([tag, count]) =>
      h('button', { class: 'chip tag', 'aria-pressed': String(chosen === tag), onclick: () => filterTag(source, tag) }, tag, h('span', { class: 'chip-count' }, String(count))),
    ),
  );
}

function filterTag(source, tag) {
  if (state.tags[source.name] === tag) delete state.tags[source.name];
  else state.tags[source.name] = tag;
  state.pickedAt = performance.now();
  renderQueue();
  renderMain();
}

// The sign of the question's first top-level agreement block, so items in disagreement stand out; nothing without one.
function rowAgreement(q) {
  const first = q.agreements.find((one) => one.at.length === 1);
  return first ? agreementMark(first, false) : null;
}

// The queue column beside a question open on its own: the inbox's groups, each row led by its title.
function questionList(source) {
  if (source.error) return h('p', { class: 'queue-empty queue-failing' }, `${source.name} is failing: ${source.error}`);
  const list = visibleQuestions(source);
  if (!list.length) return h('p', { class: 'queue-empty' }, source.questions.some(inFocus) ? 'Nothing here with this tag.' : 'Nothing waits.');
  const picked = currentQuestion(source);
  return groupsOf(list).map((group) => [
    h('p', { class: 'queue-group' }, h('span', { class: 'queue-group-name' }, group.name), h('span', { class: 'queue-count' }, String(group.questions.length))),
    h(
      'ol',
      {},
      group.questions.map((q) =>
        h(
          'li',
          { class: 'queue-row' },
          h(
            'button',
            { class: 'row queue-item', 'aria-current': String(state.view === 'review' && q === picked), onclick: () => pickQuestion(source, q.id, true) },
            h('span', { class: 'queue-title' }, rowAgreement(q), q.title),
            h('span', { class: 'queue-sub' }, [...q.tags, q.id].join(', ')),
          ),
          graphChips(q),
        ),
      ),
    ),
  ]);
}

// Puts the cursor on a question, and with open shows it; without, an open question stays open and a list stays a list.
function pickQuestion(source, id, open = false) {
  state.view = 'review';
  state.node = null;
  if (open) state.opened[source.name] = true;
  if (state.picks[source.name] !== id || currentQuestion(source)?.id !== id) {
    state.picks[source.name] = id;
    state.pickedAt = performance.now();
    state.verdict = null;
  }
  renderQueue();
  renderMain();
}

function stepQuestion(delta) {
  const source = currentSource();
  if (!source) return;
  const list = visibleQuestions(source);
  if (!list.length) return;
  const at = list.indexOf(currentQuestion(source));
  pickQuestion(source, list[Math.max(0, Math.min(list.length - 1, at + delta))].id);
  if (!isOpen(source)) document.querySelector('.inbox-row[aria-current="true"]')?.scrollIntoView({ block: 'nearest' });
}

function openQuestion() {
  const source = currentSource();
  const q = currentQuestion(source);
  if (q) pickQuestion(source, q.id, true);
}

// Esc: back to the inbox's list, its cursor on the question that was open.
function closeQuestion() {
  const source = currentSource();
  state.opened[source.name] = false;
  state.verdict = null;
  renderQueue();
  renderMain();
  $('main').focus();
}

// ---- The inbox: the questions under their groups, each row led by its title, its tags and id in small print ----

function inboxPage(source) {
  const list = visibleQuestions(source);
  const groups = groupsOf(list);
  const picked = currentQuestion(source);
  return h(
    'div',
    { class: 'inbox-page' },
    h(
      'header',
      { class: 'pane-head flush inbox-head' },
      h('h1', { class: 'pane-title' }, `${list.length} ${list.length === 1 ? 'question waits' : 'questions wait'} in ${source.name}`),
      h('p', { class: 'keys' }, 'Move ', keys('j'), keys('k'), '  Open ', keys('Enter'), '  First ', keys('g', 'g'), '  Last ', keys('G')),
    ),
    tagFilter(source),
    groups.map((group) =>
      h(
        'section',
        { class: 'inbox-group' },
        h(
          'h2',
          { class: 'inbox-group-head' },
          group.name ? h('span', { class: 'inbox-group-name' }, group.name) : null,
          h('span', { class: 'inbox-group-count' }, `${group.questions.length} ${group.questions.length === 1 ? 'question' : 'questions'}`),
        ),
        h('ol', { class: 'inbox-rows' }, group.questions.map((q) => h('li', {}, inboxRow(source, q, q === picked)))),
      ),
    ),
  );
}

function inboxRow(source, q, current) {
  return h(
    'button',
    { class: 'row inbox-row', 'aria-current': String(current), onclick: () => pickQuestion(source, q.id, true) },
    h('span', { class: 'inbox-title' }, rowAgreement(q), q.title),
    h(
      'span',
      { class: 'inbox-small' },
      q.stale ? h('span', { class: 'chip chip-stale' }, 'older code') : null,
      q.tags.map((tag) => h('span', { class: 'chip tag' }, tag)),
      graphChips(q),
    ),
    h('span', { class: 'inbox-id' }, q.id),
  );
}

// ---- One question, in full ----

function renderQuestionMain(main) {
  const source = currentSource();
  if (source.error) {
    delete main.dataset.item;
    fill(main,
      h(
        'div',
        { class: 'quiet' },
        h('h2', {}, `${source.name} is failing`),
        h('p', { class: 'quiet-error' }, source.error),
        h('p', { class: 'keys' }, 'Fix its line in sources.tsv or its command, then ask again ', h('kbd', {}, 'F5')),
      ),
    );
    return;
  }
  const q = currentQuestion(source);
  if (q && !isOpen(source)) {
    const listed = main.dataset.item === `list\u0000${source.name}`;
    const scroll = main.scrollTop;
    main.dataset.item = `list\u0000${source.name}`;
    fill(main, inboxPage(source));
    main.scrollTop = listed ? scroll : 0;
    return;
  }
  if (!q) {
    delete main.dataset.item;
    fill(main,
      h(
        'div',
        { class: 'quiet' },
        h('h2', {}, `Nothing from ${source.name} waits for you`),
        h('p', {}, source.questions.some(inFocus) ? 'Its other questions are outside the tag you chose.' : 'When it has a question for you, it appears here.'),
        h('p', { class: 'keys' }, 'Ask again ', h('kbd', {}, 'F5'), '   Next inbox ', h('kbd', {}, 'l')),
      ),
    );
    return;
  }
  const key = draftKey(source, q);
  const same = main.dataset.item === key;
  const scroll = main.scrollTop;
  const writing = same && document.activeElement?.id === 'verdict-text' ? document.activeElement : null;
  const selection = writing ? [writing.selectionStart, writing.selectionEnd] : null;
  main.dataset.item = key;
  fill(main, questionPage(source, q));
  main.scrollTop = same ? scroll : 0;
  const box = $('verdict-text');
  box.value = state.drafts[key] || '';
  if (selection) {
    box.focus();
    box.setSelectionRange(...selection);
  } else if (state.focusWords) box.focus();
  state.focusWords = false;
}

function draftKey(source, q) {
  return `${source.name}\u0000${q.id}`;
}

// ---- The bench: the problem, the code, the arguments, the details, the choices ----

// The Details folds that are open, by the question's draft key, so a redraw keeps the fold as the user left it.
const detailsOpen = new Set();

// The blocks of a question in the bench's places, each with its index, which names it in brain's checks. The first block,
// when it is text, is the problem; a facts block goes behind Details; the rest read in the order the source sent them.
function benchParts(blocks) {
  const parts = { problem: null, body: [], details: [] };
  blocks.forEach((block, index) => {
    const entry = { block, index };
    if (index === 0 && block.type === 'text') parts.problem = entry;
    else if (block.type === 'facts') parts.details.push(entry);
    else parts.body.push(entry);
  });
  return parts;
}

function questionPage(source, q) {
  const parts = benchParts(q.blocks);
  return h(
    'div',
    { class: 'bench' },
    benchHead(source, q, parts.problem),
    blocksView(q, parts.body, ''),
    benchDetails(source, q, parts.details),
    verdictBar(source, q),
  );
}

// The sentence of what is wrong, under its caption when the source gave one.
function benchProblem(block) {
  return h(
    'div',
    { class: 'bench-problem' },
    block.caption ? h('h2', { class: 'block-caption' }, block.caption) : null,
    markdownView(block.text),
    extras(block, BLOCK_FIELDS.text),
  );
}

// What is wrong, first: a stale question says so, then the title, large, and the sentence under it.
function benchHead(source, q, problem) {
  const drift = q.drift.length;
  return h(
    'header',
    { class: 'bench-head', 'data-stop': '' },
    q.stale
      ? h(
          'p',
          { class: 'stale question-stale', role: 'note' },
          h('strong', {}, 'Older code. '),
          `${source.name} says this question is about older code than its repo holds now.`,
        )
      : null,
    h('h1', { class: 'doc-title' }, q.title),
    problem ? benchProblem(problem.block) : null,
    sourceHosts[source.name]?.actions?.(q),
    drift
      ? h('p', { class: 'drift-sum' }, `${drift} ${drift === 1 ? 'thing' : 'things'} in this question ${drift === 1 ? 'does' : 'do'} not add up. Each is said where it sits.`)
      : null,
  );
}

// The small print of the question and its facts blocks, behind one fold that is closed until the user opens it.
function benchDetails(source, q, facts) {
  return h(
    'details',
    {
      class: 'bench-details',
      'data-stop': '',
      open: detailsOpen.has(draftKey(source, q)),
      ontoggle: (event) => {
        if (event.target.open) detailsOpen.add(draftKey(source, q));
        else detailsOpen.delete(draftKey(source, q));
      },
    },
    h('summary', { class: 'link' }, 'Details'),
    h(
      'p',
      { class: 'doc-meta question-small' },
      h('span', {}, source.name),
      graphChips(q),
      q.tags.map((tag) => h('span', { class: 'subject' }, tag)),
      h('span', {}, ago(q.created)),
      q.commit ? h('span', {}, 'at commit ', h('code', {}, q.commit)) : null,
      q.group ? h('span', {}, 'in ', h('code', {}, q.group)) : null,
      h('span', { class: 'question-id' }, q.id),
      h('span', { class: 'question-repo', title: q.repo }, q.repo),
    ),
    facts.map(({ block, index }) => blockSection(q, block, childPath('', index))),
  );
}

// A block's path: its index, and inside a timeline the step's and the block's, joined by dots as brain's drift names it.
function childPath(prefix, index) {
  return prefix === '' ? String(index) : `${prefix}.${index}`;
}

// The blocks as entries, each with its index, which names it in brain's checks wherever the block is drawn.
function indexed(blocks) {
  return blocks.map((block, index) => ({ block, index }));
}

// Entries in order, at the bench, inside a timeline's step and in a context the lens draws: text blocks next to each
// other read side by side when the width allows, so two arguments can be held against each other line by line.
function blocksView(q, entries, prefix) {
  return runs(entries).map((run) => {
    const views = run.map(({ block, index }) => blockSection(q, block, childPath(prefix, index)));
    return views.length > 1 ? h('div', { class: 'compare' }, views) : views[0];
  });
}

// Entries ({ block, index }) grouped into runs: text blocks that follow one another share a run, any other block has its own.
function runs(entries) {
  const out = [];
  for (const entry of entries) {
    const last = out[out.length - 1];
    if (entry.block.type === 'text' && last && last[0].block.type === 'text') last.push(entry);
    else out.push([entry]);
  }
  return out;
}

// A block with its caption, what in it does not add up, its body, and the fields of it brain does not draw.
function blockSection(q, block, path) {
  const code = block.type === 'code';
  // The source's caption, or for code its path; a block with neither has no label, never its type.
  const label = block.caption || (code ? block.path : null);
  const drift = q.drift.filter((one) => one.at.join('.') === path).map((one) => one.says);
  return h(
    'section',
    { class: `block block-${block.type}`, 'data-stop': '' },
    label || code ? h('h2', { class: 'block-caption' }, block.caption, code && block.path ? h('span', { class: 'block-path' }, `${block.path}:${lineStart(block)}`) : null, code ? copyButton(() => codeCopy(block)) : null) : null,
    drift.length ? h('div', { class: 'drift' }, h('p', { class: 'drift-head' }, 'Does not add up'), h('ul', {}, drift.map((says) => h('li', {}, says)))) : null,
    blockBody(q, block, path),
    BLOCK_FIELDS[block.type] ? extras(block, BLOCK_FIELDS[block.type]) : null,
  );
}

function blockBody(q, block, path) {
  switch (block.type) {
    case 'text':
      return markdownView(block.text);
    case 'code':
      return block.marks?.length || block.tally ? scoredCode(q, block, path) : plainCode(block);
    case 'diff':
      return diffFiles(q, block, path);
    case 'claims':
      return claimList(block.claims);
    case 'facts':
      return factList(q, block, path);
    case 'timeline':
      return timelineView(q, block, path);
    default:
      return unknownBlock(block);
  }
}

// The fields of a block or a part of one that brain does not draw, as the source sent them; none when there are none.
function extras(part, known) {
  const rest = extraFields(part, known);
  if (!rest) return null;
  return h('div', { class: 'extras' }, h('p', {}, 'Fields brain does not draw, as the source sent them:'), h('pre', {}, JSON.stringify(rest, null, 2)));
}

function extraFields(part, known) {
  const rest = Object.entries(part).filter(([key, value]) => !known.includes(key) && value !== null && value !== undefined);
  return rest.length ? Object.fromEntries(rest) : null;
}

function lineStart(block) {
  return Number.isInteger(block.startLine) ? block.startLine : 1;
}

// Code by line number. draw(i) makes the content of line i. A line whose index is in changed is tinted.
function codeLines(block, span, draw, attrs, changed = new Map()) {
  const start = lineStart(block);
  const marked = new Set(block.highlights || []);
  return h(
    'div',
    { class: 'code', role: 'figure', 'aria-label': block.path || 'Code', ...attrs },
    span.lines.map((line, i) => {
      const classes = ['code-line'];
      if (marked.has(start + i)) {
        classes.push('marked');
      }
      if (changed.has(i)) {
        classes.push('changed');
      }
      return h('div', { class: classes.join(' ') }, h('span', { class: 'ln' }, String(start + i)), h('span', { class: 'code-text' }, line ? draw(i) : ' '));
    }),
  );
}

// The text of line i with the span that differs, when it has one, drawn stronger in the colours of the code.
function changedLine(span, i, range) {
  if (!range) {
    return span.paintLine(i);
  }
  const from = span.starts[i];
  const end = from + span.lines[i].length;
  const [first, last] = range;
  return [...span.paint(from, from + first), h('span', { class: 'code-change' }, ...span.paint(from + first, from + last)), ...span.paint(from + last, end)];
}

// Code in Prism's colours, with no marks on it. block.changes, a line's index to the span of it that differs or null for
// all of it, tints those lines, and block.side says whether they were removed (before) or added (after).
function plainCode(block) {
  const span = codeSpan(block);
  const changes = block.changes ?? new Map();
  const draw = (i) => changedLine(span, i, changes.get(i));
  return codeLines(block, span, draw, { 'data-side': block.side ?? null }, changes);
}

// ---- Copying: the source's own text with its file name, ready to paste into an agent; nothing is written to brain ----

// A button that copies what text() returns, says so for a moment, and toasts when the clipboard refuses.
function copyButton(text) {
  const button = h('button', { class: 'button copy', title: 'Copy the code with its file name (yy)' }, 'Copy');
  button.onclick = async () => {
    try {
      await navigator.clipboard.writeText(text());
      button.textContent = 'Copied';
      setTimeout(() => (button.textContent = 'Copy'), 1400);
    } catch {
      toast('The clipboard refused the copy.');
    }
  };
  return button;
}

// A fence longer than any run of backticks in the text, so the text cannot close it.
function fenced(text, lang = '') {
  const longest = Math.max(2, ...(text.match(/`+/g) || []).map((run) => run.length));
  const fence = '`'.repeat(longest + 1);
  return `${fence}${lang}\n${text}\n${fence}`;
}

function codeCopy(block) {
  const start = lineStart(block);
  const text = codeText(block);
  const range = `${start}-${start + text.split('\n').length - 1}`;
  return `${block.path ? `${block.path}:${range}\n` : ''}${fenced(text)}`;
}

// The file's change as a unified diff: a hunk for each run of changes with CONTEXT lines around it.
function diffCopy(entry) {
  const { rows, file } = entry;
  const near = rows.map((_, i) => rows.slice(Math.max(0, i - CONTEXT), i + CONTEXT + 1).some((row) => row.kind !== 'same'));
  const lines = [file.before === '' ? '--- /dev/null' : `--- a/${file.path}`, file.after === '' ? '+++ /dev/null' : `+++ b/${file.path}`];
  for (let i = 0; i < rows.length; i++) {
    if (!near[i]) continue;
    let end = i;
    while (end < rows.length && near[end]) end++;
    const hunk = rows.slice(i, end);
    const old = hunk.filter((row) => row.kind !== 'add');
    const now = hunk.filter((row) => row.kind !== 'del');
    const from = old.length ? old[0].before : i > 0 ? rows[i - 1].before ?? 0 : 0;
    const to = now.length ? now[0].after : i > 0 ? rows[i - 1].after ?? 0 : 0;
    lines.push(`@@ -${from},${old.length} +${to},${now.length} @@`, ...hunk.map((row) => ({ same: ' ', add: '+', del: '-' })[row.kind] + row.text));
    i = end;
  }
  return `${file.path}\n${fenced(lines.join('\n'), 'diff')}`;
}

// yy: copies the code of the part in view, the first code block or diff file in it.
function copyCurrent() {
  const stops = stopsInView();
  const current = stops[markedIndex(stops)] ?? stops.filter(({ at }) => at <= 12).pop() ?? stops[0];
  const button = current?.el.querySelector('.copy');
  if (button) button.click();
  else toast('No code in this part.');
}

// The code as the window shows it: the one trailing newline a file ends with draws no line of its own.
function codeText(block) {
  return block.code.replace(/\n$/, '');
}

// ---- Marks on code: each weight where it sits, the tally they add up to, and a step through them ----

function number(value) {
  return String(Number(value.toFixed(10)));
}

function weightText(weight) {
  return weight > 0 ? `+${number(weight)}` : number(weight);
}

// The code painted line by line, and the offset in it of a file position the block shows: null for one it does not show.
function codeSpan(block) {
  const painted = paintedLines(block.code, block.language || languageOf(block.path));
  const first = lineStart(block);
  const offset = (position) => {
    const i = position.line - first;
    if (!(i >= 0 && i < painted.lines.length && position.col >= 1 && position.col - 1 <= painted.lines[i].length)) return null;
    return painted.starts[i] + position.col - 1;
  };
  return { ...painted, offset };
}

// The marks as a tree in reading order, each with where it falls in the code, and every mark of it in that order.
function markTree(span, marks, flat) {
  return [...marks]
    .sort((a, b) => a.start.line - b.start.line || a.start.col - b.start.col)
    .map((mark) => {
      const node = { mark, n: flat.length, from: span.offset(mark.start), to: span.offset(mark.end) };
      flat.push(node);
      node.children = markTree(span, mark.marks || [], flat);
      return node;
    });
}

// The text from one offset to another with every mark in it drawn where it sits, nested marks inside their parent. A
// mark over several lines draws on each, its weight on the line it ends on. A mark brain cannot place is not drawn, and
// brain's drift for the block says why.
function markPieces(span, from, to, nodes, at) {
  const out = [];
  let cursor = from;
  for (const node of nodes) {
    if (node.from === null || node.to === null) continue;
    const start = Math.max(node.from, cursor);
    const end = Math.min(node.to, to);
    if (end <= start) continue;
    if (start > cursor) out.push(...span.paint(cursor, start));
    const last = node.to <= to + 1;
    const link = node.mark.queries?.length || node.mark.section ? ' mark-link' : '';
    out.push(
      h(
        'span',
        { class: `${node.n === at ? 'mark mark-at' : 'mark'}${link}`, 'data-mark': String(node.n), tabindex: last ? '0' : null },
        markPieces(span, start, end, node.children, at),
        last ? h('span', { class: 'mark-weight' }, weightText(node.mark.weight)) : null,
      ),
    );
    cursor = end;
  }
  if (to > cursor) out.push(...span.paint(cursor, to));
  return out;
}

function scoredCode(q, block, path) {
  const key = `${q.id}:${path}`;
  const span = codeSpan(block);
  const flat = [];
  const tree = markTree(span, block.marks || [], flat);
  const at = state.markAt?.key === key ? state.markAt.at : -1;
  const readout = h('p', { class: 'mark-readout', 'aria-live': 'polite' });
  const rest = () => fill(readout, stepNote(block, flat, at));
  const show = (target) => {
    const el = target.closest('.mark');
    if (el) fill(readout, markNote(flat[Number(el.dataset.mark)].mark));
    else rest();
  };
  rest();
  // A mark a context links from acts on a click, or on Enter while it has the focus.
  const act = (event) => {
    const el = event.target.closest('.mark-link');
    if (!el || !q.onMark) return;
    event.preventDefault();
    q.onMark(flat[Number(el.dataset.mark)].mark, el);
  };
  const code = codeLines(
    block,
    span,
    (i) => markPieces(span, span.starts[i], span.starts[i] + span.lines[i].length, tree, at),
    {
      'data-marks': flat.length ? key : null,
      'data-count': String(flat.length),
      onmouseover: (event) => show(event.target),
      onmouseleave: rest,
      onfocusin: (event) => show(event.target),
      onfocusout: rest,
      onclick: act,
      onkeydown: (event) => event.key === 'Enter' && act(event),
    },
  );
  const sum = block.tally ? q.sums.find((one) => one.at.join('.') === path) : null;
  return h('div', { class: block.tally ? 'scored tallied' : 'scored' }, sum ? tallyLine(block.tally, sum, flat.length) : null, code, flat.length ? readout : null);
}

// base + weights = total, as brain serve added them up. When that total is not the source's value, it says both.
function tallyLine(tally, sum, count) {
  return h(
    'div',
    { class: sum.addsUp ? 'tally' : 'tally off' },
    h('span', { class: 'tally-label' }, tally.label),
    h('span', { class: 'tally-sum' }, `${number(tally.base)} + ${number(sum.weights)} = ${number(sum.total)}`),
    h(
      'span',
      { class: 'tally-note' },
      sum.addsUp ? `the base and the weights of ${count} ${count === 1 ? 'mark' : 'marks'}` : `brain adds up ${number(sum.total)}, and the source says ${number(tally.value)}`,
    ),
    extras(tally, TALLY_FIELDS),
  );
}

// What the readout under the code says when no mark is hovered: how to step, or the mark stepped to and the total so far.
function stepNote(block, flat, at) {
  if (at < 0) return [`${flat.length} ${flat.length === 1 ? 'mark' : 'marks'}. `, keys('n'), ' steps through them and adds them up, ', keys('N'), ' steps back. Hover or focus a mark for its note.'];
  const node = flat[at];
  const terms = flat.slice(0, at + 1).map((one) => number(one.mark.weight));
  const running = flat.slice(0, at + 1).reduce((total, one) => total + one.mark.weight, block.tally ? block.tally.base : 0);
  return [
    h('span', { class: 'mark-step' }, `${at + 1} of ${flat.length}`),
    markNote(node.mark),
    h('span', { class: 'mark-running' }, `So far: ${[...(block.tally ? [number(block.tally.base)] : []), ...terms].join(' + ')} = ${number(running)}`),
  ];
}

function markNote(mark) {
  const rest = extraFields(mark, MARK_FIELDS);
  return h(
    'span',
    { class: 'mark-note' },
    h('strong', {}, mark.label),
    ` ${weightText(mark.weight)}`,
    mark.note ? `: ${mark.note}` : null,
    rest ? h('code', { class: 'mark-extra' }, JSON.stringify(rest)) : null,
  );
}

// Steps to the next mark or the one before, across every code block with marks in view, the open steps of a timeline too.
function stepMark(delta) {
  const blocks = [...$('main').querySelectorAll('[data-marks]')];
  if (!blocks.length) return;
  const count = (el) => Number(el.dataset.count);
  const now = blocks.findIndex((el) => el.dataset.marks === state.markAt?.key);
  let target = null;
  if (now >= 0 && state.markAt.at + delta >= 0 && state.markAt.at + delta < count(blocks[now])) target = { el: blocks[now], at: state.markAt.at + delta };
  else {
    const next = now < 0 ? (delta > 0 ? 0 : blocks.length - 1) : now + delta;
    if (next >= 0 && next < blocks.length) target = { el: blocks[next], at: delta > 0 ? 0 : count(blocks[next]) - 1 };
  }
  if (!target) return;
  const key = target.el.dataset.marks;
  state.markAt = { key, at: target.at };
  renderMain();
  const block = [...$('main').querySelectorAll('[data-marks]')].find((el) => el.dataset.marks === key);
  block?.querySelector(`.mark[data-mark="${target.at}"]`)?.scrollIntoView({ block: 'nearest' });
}

// ---- Facts: a label and the source's value, coloured by its tone ----

function factList(q, block, path) {
  const agreement = q.agreements.find((one) => one.at.join('.') === path);
  // Facts of one value share a look: the order the value first appears in picks it.
  const looks = agreement ? agreement.split.map((one) => one.value) : [];
  const chips = block.facts.map((fact) => {
    const look = looks.indexOf(fact.value);
    const cls = ['fact', fact.tone ? `tone-${fact.tone}` : '', look >= 0 ? `value-${look % 4}` : ''].filter(Boolean).join(' ');
    return h('div', { class: cls }, h('dt', {}, fact.label), h('dd', {}, fact.value), extras(fact, FACT_FIELDS));
  });
  return h('div', {}, agreement ? agreementMark(agreement, true) : null, h('dl', { class: 'facts' }, chips));
}

// brain's comparison of a facts block: = and "agree" when every value is equal, ≠ and "disagree" with the split when
// not. The sign and the word carry it; colour only adds. On a tab or a queue item only the sign shows.
function agreementMark(agreement, full) {
  const word = agreement.agrees ? 'agree' : 'disagree';
  const split = agreement.split.map((one) => `${one.count} ${one.value}`).join(' \u00b7 ');
  return h(
    'span',
    { class: `agreement ${word}${full ? ' agreement-full' : ''}`, title: agreement.agrees ? word : `${word}: ${split}`, ...(full ? {} : { role: 'img', 'aria-label': word }) },
    h('span', { class: 'agreement-sign', 'aria-hidden': 'true' }, agreement.agrees ? '=' : '\u2260'),
    full ? h('span', { class: 'agreement-word' }, word) : null,
    full && !agreement.agrees ? h('span', { class: 'agreement-split' }, split) : null,
  );
}

// ---- A timeline: its steps as a strip, oldest first, the latest open, and two side by side on request ----

function timelineState(key, count) {
  state.timelines[key] ||= { open: count - 1, beside: null };
  return state.timelines[key];
}

function timelineView(q, block, path) {
  const key = `${q.id}:${path}`;
  const steps = block.steps;
  const view = timelineState(key, steps.length);
  const shown = view.beside === null ? [view.open] : [view.beside, view.open].sort((a, b) => a - b);
  return h(
    'div',
    { class: 'timeline', 'data-timeline': key, 'data-count': String(steps.length) },
    h(
      'ol',
      { class: 'timeline-strip' },
      steps.map((step, i) =>
        h(
          'li',
          {},
          h(
            'button',
            { class: step.tone ? `button tone-${step.tone}` : 'button', 'aria-pressed': String(shown.includes(i)), onclick: () => openStep(key, i) },
            h('span', { class: 'step-no' }, String(i + 1)),
            h('span', { class: 'step-label' }, step.label),
            step.when ? h('span', { class: 'step-when' }, step.when.slice(0, 10)) : null,
            stepAgreement(q, childPath(path, i)),
            stepDrift(q, childPath(path, i)),
          ),
        ),
      ),
    ),
    h(
      'p',
      { class: 'diff-keys' },
      steps.length > 1 ? h('button', { class: 'link', onclick: () => toggleBeside(key) }, view.beside === null ? 'Two steps side by side' : 'One step') : null,
      steps.length > 1 ? keys('v') : null,
      steps.length > 1 ? h('span', {}, keys('gt'), keys('gT'), ' move between steps') : null,
    ),
    h('div', { class: shown.length > 1 ? 'steps steps-two' : 'steps' }, shown.map((i) => stepView(q, steps[i], i, childPath(path, i)))),
  );
}

// The first agreement block in a step's blocks, as its sign, so a step that is not open says where the history split.
function stepAgreement(q, path) {
  const first = q.agreements.find((one) => one.at.join('.').startsWith(`${path}.`));
  return first ? agreementMark(first, false) : null;
}

// How many things in a step's blocks do not add up, in words on the strip, so a step that is not open says it too.
function stepDrift(q, path) {
  const count = q.drift.filter((one) => one.at.join('.').startsWith(`${path}.`)).length;
  return count ? h('span', { class: 'step-drift' }, `${count} not adding up`) : null;
}

function stepView(q, step, index, path) {
  return h(
    'div',
    { class: step.tone ? `step tone-${step.tone}` : 'step' },
    h(
      'h3',
      { class: 'step-head' },
      h('span', { class: 'step-no' }, `Step ${index + 1}`),
      h('span', {}, step.label),
      step.when ? h('span', { class: 'step-when', title: step.when }, ago(step.when)) : null,
    ),
    step.blocks.length ? blocksView(q, indexed(step.blocks), path) : h('p', { class: 'file-same' }, 'This step has no blocks.'),
    extras(step, STEP_FIELDS),
  );
}

// The timeline the step keys move: the one last used while it is in view, else the first in view.
function currentTimeline() {
  const all = [...$('main').querySelectorAll('[data-timeline]')];
  const el = all.find((one) => one.dataset.timeline === state.timeline) || all[0];
  return el ? { key: el.dataset.timeline, count: Number(el.dataset.count) } : null;
}

function openStep(key, index) {
  const view = state.timelines[key];
  state.timeline = key;
  if (index !== view.beside) view.open = index;
  renderMain();
}

// Pins the open step's neighbour beside it, the one before when there is one; the step keys then move the open step.
function toggleBeside(key) {
  const view = state.timelines[key];
  state.timeline = key;
  view.beside = view.beside === null ? (view.open > 0 ? view.open - 1 : view.open + 1) : null;
  renderMain();
}

function moveStep(delta) {
  const timeline = currentTimeline();
  if (!timeline) return;
  const view = timelineState(timeline.key, timeline.count);
  let next = view.open + delta;
  if (next === view.beside) next += delta;
  state.timeline = timeline.key;
  if (next < 0 || next >= timeline.count) return;
  view.open = next;
  renderMain();
}

function claimList(claims) {
  const holding = claims.filter((claim) => claim.holds).length;
  return h(
    'div',
    { class: 'claims' },
    h('p', { class: 'claims-sum' }, `${holding} of ${claims.length} ${claims.length === 1 ? 'holds' : 'hold'}`),
    h(
      'ol',
      {},
      claims.map((claim) =>
        h(
          'li',
          { class: claim.holds ? 'claim holds' : 'claim fails' },
          h('span', { class: 'claim-mark' }, claim.holds ? 'holds' : 'fails'),
          h(
            'div',
            { class: 'claim-body' },
            markdownView(claim.text),
            claim.evidence ? h('div', { class: 'claim-evidence' }, h('span', { class: 'claim-label' }, 'Evidence'), markdownView(claim.evidence)) : null,
          ),
        ),
      ),
    ),
  );
}

// A block of a type brain does not draw shows its fields as the source sent them, so nothing is dropped unseen.
function unknownBlock(block) {
  const fields = Object.fromEntries(Object.entries(block).filter(([, value]) => value !== null && value !== undefined));
  return h(
    'div',
    { class: 'unknown' },
    h('p', {}, `brain does not know the block type “${block.type}”. Its fields, as the source sent them:`),
    h('pre', {}, JSON.stringify(fields, null, 2)),
  );
}

// ---- A diff over several files: a summary to jump from, then each file with its line numbers before and after ----

function fileState(file) {
  if (file.before === '') return 'added';
  if (file.after === '') return 'removed';
  return 'changed';
}

const FILE_MARKS = { added: '+', removed: '−', changed: '±' };

// Every line of the file with its kind and its numbers before and after, and the painted lines it is drawn from: a removed
// line from the file before, any other from the file after. Worked out once per file.
function diffRowsOf(file) {
  if (diffRows.has(file)) return diffRows.get(file);
  const language = languageOf(file.path);
  const was = paintedLines(file.before, language);
  const now = paintedLines(file.after, language);
  const rows = numberedRows(file.before, file.after);
  for (const row of rows) {
    // A removed line is drawn from the file before, every other line from the file after.
    if (row.kind === 'del') {
      row.source = was;
      row.index = row.before - 1;
    } else {
      row.source = now;
      row.index = row.after - 1;
    }
  }
  diffRows.set(file, rows);
  return rows;
}

function diffFiles(q, block, index) {
  const files = block.files.map((file, at) => {
    const rows = diffRowsOf(file);
    return {
      file,
      rows,
      key: `${q.id}:${index}:${at}`,
      added: rows.filter((row) => row.kind === 'add').length,
      removed: rows.filter((row) => row.kind === 'del').length,
    };
  });
  const sections = files.map((entry) => fileSection(entry));
  const splits = hasCode(block.files);
  return h(
    'div',
    { class: 'files' },
    files.length > 1
      ? h(
          'ol',
          { class: 'file-list' },
          files.map((entry, at) =>
            h(
              'li',
              {},
              h(
                'button',
                { class: 'row compact', onclick: () => sections[at].scrollIntoView({ block: 'start' }) },
                h('span', { class: `file-state ${fileState(entry.file)}` }, FILE_MARKS[fileState(entry.file)]),
                h('span', { class: 'file-path' }, entry.file.path),
                counts(entry),
              ),
            ),
          ),
        )
      : null,
    h(
      'p',
      { class: 'diff-keys' },
      splits ? h('button', { class: 'link', onclick: toggleSplit }, state.split ? 'One column' : 'Side by side') : null,
      splits ? keys('s') : null,
      h('button', { class: 'link', onclick: toggleWhole }, state.whole ? 'Fold unchanged lines' : 'Show whole files'),
      keys('e'),
    ),
    sections,
  );
}

// Side by side is for code: a markdown file's diff always reads in one column, so a diff of markdown alone has no switch.
function hasCode(files) {
  return files.some((file) => !isMarkdownPath(file.path));
}

// Whether a question has a diff with a file that can read side by side.
function canSplit(question) {
  return anyBlock(question.blocks, (block) => block.type === 'diff' && hasCode(block.files));
}

function counts(entry) {
  return h('span', { class: 'file-counts' }, entry.added ? h('span', { class: 'count-add' }, `+${entry.added}`) : null, entry.removed ? h('span', { class: 'count-del' }, `−${entry.removed}`) : null);
}

// Side by side is for a changed file; an added or removed file has one side only and reads in one column.
function fileSection(entry) {
  const status = fileState(entry.file);
  // A wrapped line would fall out of step with the line it faces, so a markdown file always reads in one column.
  const split = state.split && status === 'changed' && !isMarkdownPath(entry.file.path);
  return h(
    'section',
    { class: 'file', 'data-stop': '' },
    h(
      'div',
      { class: 'file-head' },
      h('span', { class: `file-state ${status}` }, FILE_MARKS[status], ' ', status === 'changed' ? 'changed' : status === 'added' ? 'new file' : 'removed'),
      h('span', { class: 'file-path' }, entry.file.path),
      counts(entry),
      copyButton(() => diffCopy(entry)),
    ),
    entry.added || entry.removed
      ? drowsView(entry.rows, split, entry.file.path, segments(entry).flatMap((segment) => (segment.fold ? [foldRow(segment)] : rowsView(segment.rows, split))))
      : h('p', { class: 'file-same' }, 'No line changed.'),
  );
}

// The rows to show and the folds between them: unchanged runs keep CONTEXT lines next to each change.
function segments(entry) {
  const rows = entry.rows;
  const out = [];
  let shownFrom = 0;
  let i = 0;
  while (i < rows.length) {
    if (rows[i].kind !== 'same') {
      i++;
      continue;
    }
    let end = i;
    while (end < rows.length && rows[end].kind === 'same') end++;
    const keepStart = i === 0 ? 0 : CONTEXT;
    const keepEnd = end === rows.length ? 0 : CONTEXT;
    const from = i + keepStart;
    const to = end - keepEnd;
    const key = `${entry.key}:${from}`;
    if (!state.whole && to - from >= MIN_FOLD && !state.unfolded.has(key)) {
      if (from > shownFrom) out.push({ rows: rows.slice(shownFrom, from) });
      out.push({ fold: true, key, count: to - from });
      shownFrom = to;
    }
    i = end;
  }
  if (shownFrom < rows.length) out.push({ rows: rows.slice(shownFrom) });
  return out;
}

function foldRow(segment) {
  return h(
    'button',
    {
      class: 'fold drow-fold',
      onclick: () => {
        state.unfolded.add(segment.key);
        renderMain();
      },
    },
    h('span', { class: 'drow-fold-text' }, `⋯ ${segment.count} unchanged lines`),
  );
}

const ROW_MARKS = { same: ' ', add: '+', del: '−' };

// The rows' box. Its --cols is the longest line in columns, which every side sets as the least width of its text, so all the
// sides of the file scroll as far and a scroll of one carries the rest with it.
function drowsView(rows, split, path, children) {
  const box = h('div', { class: split ? 'drows split' : 'drows', 'data-wrap': isMarkdownPath(path) }, children);
  if (!split) return box;
  let longest = 0;
  for (const row of rows) {
    longest = Math.max(longest, row.text.replaceAll('\t', '        ').length);
  }
  box.style.setProperty('--cols', String(longest));
  return box;
}

function rowText(row) {
  if (!row.text) return ' ';
  return row.source.paintLine(row.index);
}

function rowsView(rows, split) {
  if (!split) {
    return rows.map((row) =>
      h(
        'div',
        { class: `drow ${row.kind}` },
        h('span', { class: 'ln' }, row.before === null ? '' : String(row.before)),
        h('span', { class: 'ln' }, row.after === null ? '' : String(row.after)),
        h('span', { class: 'diff-mark' }, ROW_MARKS[row.kind]),
        h('span', { class: 'code-text' }, rowText(row)),
      ),
    );
  }
  const faced = pairs(rows);
  const left = h('div', { class: 'drow-side', onscroll: carrySides }, faced.map(([row]) => half(row, 'before', 'del')));
  const right = h('div', { class: 'drow-side', onscroll: carrySides }, faced.map(([, row]) => half(row, 'after', 'add')));
  return [h('div', { class: 'drow-pair' }, left, right)];
}

// A side scrolled sideways takes every other side of the file along, so Before and After keep their lines paired.
function carrySides(event) {
  const left = event.target.scrollLeft;
  for (const side of event.target.closest('.drows').querySelectorAll('.drow-side')) {
    if (Math.abs(side.scrollLeft - left) > 1) side.scrollLeft = left;
  }
}

function half(row, side, changed) {
  if (!row) return h('div', { class: 'drow empty' }, h('span', { class: 'ln' }), h('span', { class: 'diff-mark' }), h('span', { class: 'code-text' }, ' '));
  const kind = row.kind === 'same' ? 'same' : changed;
  return h('div', { class: `drow ${kind}` }, h('span', { class: 'ln' }, String(row[side])), h('span', { class: 'diff-mark' }, ROW_MARKS[kind]), h('span', { class: 'code-text' }, rowText(row)));
}

// Side by side, a run of removed lines faces the run of added lines that replaced it.
function pairs(rows) {
  const out = [];
  let i = 0;
  while (i < rows.length) {
    if (rows[i].kind === 'same') {
      out.push([rows[i], rows[i]]);
      i++;
      continue;
    }
    const removed = [];
    const added = [];
    while (i < rows.length && rows[i].kind === 'del') removed.push(rows[i++]);
    while (i < rows.length && rows[i].kind === 'add') added.push(rows[i++]);
    for (let k = 0; k < Math.max(removed.length, added.length); k++) out.push([removed[k] || null, added[k] || null]);
  }
  return out;
}

function toggleSplit() {
  state.split = !state.split;
  renderMain();
}

function toggleWhole() {
  state.whole = !state.whole;
  renderMain();
}

// Every part of the question in order, with where its top sits against the top of the pane. A part that moveStop
// scrolled to sits its scroll margin below the top, so the margin is taken off and that part reads 0.
function stopsInView() {
  const pane = questionScroller();
  const top = pane.getBoundingClientRect().top;
  return [...pane.querySelectorAll('[data-stop]')].map((el) => ({ el, at: el.getBoundingClientRect().top - top - parseFloat(getComputedStyle(el).scrollMarginTop) }));
}

// What scrolls the question: the main pane.
function questionScroller() {
  return $('main');
}

// The pane's scroll position when ]] or [[ marked a part. A part within the last screen cannot reach the top of the
// pane, so while the pane has not moved since, the keys step from the mark, not from where the part sits.
let markedScroll = null;

// The part ]] or [[ marked, while the pane is where they left it; -1 when nothing is marked or the user scrolled.
function markedIndex(stops) {
  return questionScroller().scrollTop === markedScroll ? stops.findIndex(({ el }) => el.classList.contains('at')) : -1;
}

// Moves the main pane to the next part of the question, or back to the one before: a block, a column or a file.
function moveStop(delta) {
  const main = questionScroller();
  const stops = stopsInView();
  const marked = markedIndex(stops);
  const next = marked >= 0 ? stops[marked + delta] : delta > 0 ? stops.find((stop) => stop.at > 2) : stops.reverse().find((stop) => stop.at < -2);
  if (!next) return;
  next.el.scrollIntoView({ block: 'start' });
  for (const el of main.querySelectorAll('.at')) el.classList.remove('at');
  next.el.classList.add('at');
  markedScroll = main.scrollTop;
}

// ---- The verdict: a choice by its number, the user's words or none, sent with Ctrl+Enter ----

// The words box stays under the choices whether a choice is picked or not. Its draft is kept per question until the
// verdict goes, and goes with it.
function verdictBar(source, q) {
  const chosen = state.verdict?.id === q.id ? q.choices.find((choice) => choice.id === state.verdict.choice) : null;
  const list = visibleQuestions(source);
  const key = draftKey(source, q);
  const words = h(
    'div',
    { class: 'reason words' },
    h(
      'label',
      { for: 'verdict-text' },
      chosen ? `Your verdict: ${chosen.label}. Your words go to ${source.name} with it, or send it without.` : `Your words for ${source.name}, sent with your verdict. Pick a choice to send.`,
    ),
    h('textarea', {
      id: 'verdict-text',
      class: 'field bare writing low',
      oninput: (event) => {
        state.drafts[key] = event.target.value;
      },
    }),
    h(
      'div',
      { class: 'reason-foot' },
      h('button', { class: 'button yes', disabled: state.busy || !chosen, onclick: sendVerdict }, 'Send', keys('Ctrl', 'Enter')),
      h('span', {}, 'Write ', keys('i')),
      h('span', {}, 'Leave the box ', keys('Esc')),
      h('span', { id: 'pending-key', class: 'pending-key' }, state.pending?.key || ''),
      list.length > 1 ? h('span', { class: 'answer-note' }, `${list.indexOf(q) + 1} of ${list.length}`) : null,
    ),
  );
  return h(
    'div',
    { class: 'answer' },
    h(
      'div',
      { class: 'answer-bar' },
      q.choices.map((choice, index) =>
        h(
          'button',
          { class: 'button', 'aria-pressed': String(choice === chosen), disabled: state.busy, onclick: () => chooseVerdict(index) },
          choice.label,
          index < CHOICE_KEYS ? h('kbd', {}, String(index + 1)) : null,
        ),
      ),
    ),
    words,
  );
}

function chooseVerdict(index) {
  const source = currentSource();
  const q = currentQuestion(source);
  if (!q || !q.choices[index] || state.busy || state.view !== 'review') return;
  if (state.picks[source.name] !== q.id) {
    state.picks[source.name] = q.id;
    state.pickedAt = performance.now();
  }
  state.verdict = { id: q.id, choice: q.choices[index].id };
  state.focusWords = true;
  renderMain();
}

function focusWords() {
  state.focusWords = true;
  renderMain();
}

function cancelVerdict() {
  state.verdict = null;
  renderMain();
  $('main').focus();
}

async function sendVerdict() {
  const source = currentSource();
  const q = currentQuestion(source);
  const verdict = state.verdict;
  if (!q || !verdict || verdict.id !== q.id || state.busy) return;
  if (performance.now() - state.pickedAt < SETTLE_MS) {
    toast('This question just came into view. Read it, then answer.');
    return;
  }
  const label = q.choices.find((choice) => choice.id === verdict.choice).label;
  const key = draftKey(source, q);
  state.busy = true;
  renderMain();
  try {
    const { ok, body } = await call('/api/answer/question', {
      method: 'POST',
      body: JSON.stringify({ source: source.name, id: q.id, choice: verdict.choice, words: (state.drafts[key] || '').trim() || null }),
    });
    if (ok) {
      const at = visibleQuestions(source).indexOf(q);
      source.questions = source.questions.filter((other) => other !== q);
      const rest = visibleQuestions(source);
      if (rest.length) {
        state.picks[source.name] = rest[Math.min(at, rest.length - 1)].id;
        state.pickedAt = performance.now();
      } else state.opened[source.name] = false;
      state.verdict = null;
      delete state.drafts[key];
      toast(`Answered “${label}”. ${source.name} collects it on its next run.`);
    } else toast(body.error);
  } finally {
    state.busy = false;
    renderQueue();
    renderMain();
    renderPalette();
  }
}

// ---- Keys and commands of an inbox beside node review ----

// The first key of a two-key sequence, and what each second key does. The first waits PENDING_MS for the second.
const PENDING_MS = 1000;
// Keys that only hold a modifier: they never end a pending key, since Shift comes before the T of gT.
const MODIFIERS = new Set(['Shift', 'Control', 'Alt', 'Meta', 'CapsLock', 'AltGraph']);
const SEQUENCES = {
  g: { g: () => goEnd(-1), t: () => moveStep(1), T: () => moveStep(-1) },
  y: { y: copyCurrent },
  ']': { ']': () => moveStop(1) },
  '[': { '[': () => moveStop(-1) },
};

// gg and G: the first or last question in the inbox.
function goEnd(delta) {
  stepQuestion(delta * Infinity);
}

// Shows the pending key in the view's foot, as vim shows it in its command line.
function setPending(first) {
  clearTimeout(state.pending?.timer);
  state.pending = first ? { key: first, timer: setTimeout(() => setPending(null), PENDING_MS) } : null;
  const el = document.getElementById('pending-key');
  if (el) el.textContent = first || '';
}

// The keys of a question in view, as vim motions; true when the key was one of them.
function onQuestionKey(event) {
  if (!plainKey(event) || state.view !== 'review') return false;
  const key = event.key.toLowerCase();
  const typed = event.key;
  if (MODIFIERS.has(typed)) return false;
  if (contextMenuKey(event)) return true;
  const first = state.pending?.key;
  setPending(null);
  const second = first && SEQUENCES[first][typed];
  if (second) {
    event.preventDefault();
    second();
    return true;
  }
  if (key === 'escape' && state.verdict) {
    cancelVerdict();
    return true;
  }
  if (key === 'escape' && isOpen(currentSource())) {
    closeQuestion();
    return true;
  }
  if (typed === 'Enter' && !isOpen(currentSource())) {
    event.preventDefault();
    openQuestion();
    return true;
  }
  if (SEQUENCES[typed]) {
    event.preventDefault();
    setPending(typed);
    return true;
  }
  if (/^[1-9]$/.test(key)) {
    chooseVerdict(Number(key) - 1);
    if (state.verdict) event.preventDefault();
    return true;
  }
  const actions = {
    s: toggleCurrentSplit,
    e: toggleWhole,
    j: () => stepQuestion(1),
    k: () => stepQuestion(-1),
    G: () => goEnd(1),
    n: () => stepMark(1),
    N: () => stepMark(-1),
    v: toggleCurrentBeside,
    i: focusWords,
  };
  if (!actions[typed]) return false;
  event.preventDefault();
  actions[typed]();
  return true;
}

function toggleCurrentSplit() {
  const question = currentQuestion();
  if (question && canSplit(question)) {
    toggleSplit();
  }
}

function toggleCurrentBeside() {
  const timeline = currentTimeline();
  if (timeline && timeline.count > 1) {
    timelineState(timeline.key, timeline.count);
    toggleBeside(timeline.key);
  }
}

// True when a block of the question, or of a timeline's step in it, passes the test.
function anyBlock(blocks, test) {
  return blocks.some((block) => test(block) || (block.type === 'timeline' && block.steps.some((step) => anyBlock(step.blocks, test))));
}

// The keys of the words box under the choices: Esc leaves it with the draft kept, Ctrl+Enter sends a picked verdict.
function onVerdictKey(event, ctrl) {
  if (event.key === 'Escape') {
    event.preventDefault();
    $('main').focus();
  } else if (ctrl && event.key === 'Enter') {
    event.preventDefault();
    if (state.verdict?.id === currentQuestion()?.id) sendVerdict();
    else toast('Pick a choice first: leave the box with Esc, then press its number.');
  }
}

// The commands of the source in view.
function questionCommands(source) {
  const list = [];
  const q = state.view === 'review' && isOpen(source) ? currentQuestion(source) : null;
  if (state.view === 'review' && !isOpen(source) && currentQuestion(source)) list.push({ name: 'Open the question', keys: ['Enter'], run: openQuestion });
  if (q) list.push({ name: 'Back to the inbox', keys: ['Esc'], run: closeQuestion });
  if (q) {
    q.choices.forEach((choice, index) => list.push({ name: `Verdict: ${choice.label}`, keys: index < CHOICE_KEYS ? [String(index + 1)] : [], run: () => chooseVerdict(index) }));
    list.push(
      { name: 'Next part of the question', keys: [']]'], run: () => moveStop(1) },
      { name: 'Previous part of the question', keys: ['[['], run: () => moveStop(-1) },
      { name: state.whole ? 'Fold unchanged lines' : 'Show whole files', keys: ['e'], run: toggleWhole },
      { name: 'Write your words', keys: ['i'], run: focusWords },
    );
    if (canSplit(q)) {
      list.push({ name: state.split ? 'Show diffs in one column' : 'Show diffs side by side', keys: ['s'], run: toggleSplit });
    }
    if (anyBlock(q.blocks, (block) => block.type === 'code' && block.marks?.length)) {
      list.push({ name: 'Next mark', keys: ['n'], run: () => stepMark(1) }, { name: 'Previous mark', keys: ['N'], run: () => stepMark(-1) });
    }
    if (q.blocks.some((block) => block.type === 'timeline')) {
      list.push(
        { name: 'Next step of the timeline', keys: ['gt'], run: () => moveStep(1) },
        { name: 'Previous step of the timeline', keys: ['gT'], run: () => moveStep(-1) },
        { name: 'Two steps side by side, or one', keys: ['v'], run: toggleCurrentBeside },
      );
    }
  }
  const shownList = visibleQuestions(source);
  if (shownList.length > 1) {
    list.push(
      { name: 'Next question', keys: ['j'], run: () => stepQuestion(1) },
      { name: 'Previous question', keys: ['k'], run: () => stepQuestion(-1) },
      { name: 'First question', keys: ['gg'], run: () => stepQuestion(-Infinity) },
      { name: 'Last question', keys: ['G'], run: () => stepQuestion(Infinity) },
    );
  }
  const tags = new Set(source.questions.filter(inFocus).flatMap((other) => other.tags));
  for (const tag of tags) {
    list.push({ name: state.tags[source.name] === tag ? `Show every tag` : `Show only ${tag}`, group: source.name, keys: [], run: () => filterTag(source, tag) });
  }
  for (const other of shownList) list.push({ name: other.title, group: other.tags.join(', ') || source.name, keys: [], run: () => pickQuestion(source, other.id) });
  return list;
}
