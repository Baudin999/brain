// The stylebook in brain's window: a view in the one window, at the strip's far end. It shows every token tokens.css
// defines, read from the sheet itself so the page never falls behind it, each as the window draws it in the theme now
// in use. Under the tokens it shows each named component, in its states: at rest, hovered, pressed or checked, current
// or under the cursor, focused, and disabled. components.css draws most; a component a module uses is drawn by its own
// sheet in components/, and its head names that sheet. The option list, the model selector and the embedding model
// picker are drawn here by their own functions, which their modules offer on window, and so are the markdown component,
// the image viewer, and brain code's image thumbnail, file explorer, permission request, question, stop confirm,
// process fold, agent row, agent flow, agent message, trail step, Navarone signal, verdict, tool call, tool run, information panel
// and tool detail. A component shown hovered carries data-hover and one shown focused data-focus, which its sheet draws
// as the pointer's hover and the keys' focus.
'use strict';

const STYLEBOOK = ':stylebook';

// The groups a token falls in, by its name or, for colors and fonts, by what its value is, and how each draws a token
// as a sample: look is the token, as var(). A tile group lays its samples out as tiles, the others as lines. A resolve
// group shows each token's value as the theme in use resolves it, the others as tokens.css writes it.
const TOKEN_GROUPS = [
  {
    title: 'Colors',
    tile: true,
    resolve: true,
    test: (name, value) => !name.startsWith('--shadow') && CSS.supports('color', value),
    draw: (sample, look) => {
      sample.classList.add('sb-swatch');
      sample.style.background = look;
    },
  },
  {
    title: 'Text sizes',
    test: (name) => name.startsWith('--text-'),
    draw: (sample, look) => {
      sample.textContent = 'What the graph says';
      sample.style.fontSize = look;
    },
  },
  {
    title: 'Radii',
    tile: true,
    test: (name) => name.startsWith('--radius-'),
    draw: (sample, look) => {
      sample.classList.add('sb-corner');
      sample.style.borderRadius = look;
    },
  },
  {
    title: 'Space',
    test: (name) => name.startsWith('--sp-'),
    draw: (sample, look) => {
      sample.classList.add('sb-space');
      sample.style.width = look;
    },
  },
  {
    title: 'Shadows',
    tile: true,
    test: (name) => name.startsWith('--shadow'),
    draw: (sample, look) => {
      sample.classList.add('sb-lifted');
      sample.style.boxShadow = look;
    },
  },
  {
    title: 'Fonts',
    test: (name, value) => CSS.supports('font-family', value),
    draw: (sample, look) => {
      sample.textContent = 'What the graph says, 0123';
      sample.style.fontFamily = look;
    },
  },
  { title: 'Other tokens', test: () => true, draw: () => {} },
];

// Every custom property the :root rules of tokens.css define, in the order it defines them, each with its value there.
function stylebookTokens() {
  const sheet = [...document.styleSheets].find((one) => one.href?.endsWith('/tokens.css'));
  if (!sheet) return [];
  const tokens = new Map();
  for (const rule of sheet.cssRules) {
    if (!(rule instanceof CSSStyleRule) || rule.selectorText !== ':root') continue;
    for (const name of rule.style) {
      if (!name.startsWith('--') || tokens.has(name)) continue;
      tokens.set(name, rule.style.getPropertyValue(name).trim());
    }
  }
  return [...tokens].map(([name, value]) => ({ name, value }));
}

function tokenGroups(tokens) {
  const groups = TOKEN_GROUPS.map((group) => ({ ...group, tokens: [] }));
  for (const token of tokens) {
    const group = groups.find((one) => one.test(token.name, token.value));
    group.tokens.push(token);
  }
  return groups.filter((group) => group.tokens.length);
}

// One token: its sample, its name, and its value, resolved or as written as its group says.
function tokenView(group, token) {
  const sample = h('span', { class: 'sb-sample' });
  group.draw(sample, `var(${token.name})`);
  const shown = group.resolve ? resolved(token.name) : token.value;
  return h(
    'li',
    { class: group.tile ? 'sb-token sb-tile' : 'sb-token sb-line' },
    sample,
    h('span', { class: 'sb-name' }, token.name),
    h('span', { class: 'sb-value', title: token.value }, shown),
  );
}

// A component's states side by side, each under its name.
function stateRow(label, states) {
  return h(
    'div',
    { class: 'sb-row' },
    h('span', { class: 'sb-row-label' }, label),
    states.map(([state, el]) => h('span', { class: 'sb-state' }, el, h('span', { class: 'sb-state-name' }, state))),
  );
}

function demoButton(cls, text, attrs = {}) {
  return h('button', { class: cls, type: 'button', tabindex: '-1', ...attrs }, text);
}

function buttonStates(cls, text) {
  return [
    ['rest', demoButton(cls, text)],
    ['hover', demoButton(cls, text, { 'data-hover': true })],
    ['pressed', demoButton(cls, text, { 'aria-pressed': 'true' })],
    ['disabled', demoButton(cls, text, { disabled: true })],
  ];
}

function demoSwitch(kind, on, extra = {}) {
  const attr = kind === 'radio' ? 'aria-checked' : 'aria-pressed';
  const options = ['Changes', 'Reads as', 'Both'];
  return h(
    'span',
    { class: 'segmented', role: kind === 'radio' ? 'radiogroup' : 'group', 'aria-label': 'Example' },
    options.map((text, index) =>
      h('button', { type: 'button', tabindex: '-1', role: kind === 'radio' ? 'radio' : null, [attr]: String(index === on), ...(extra[index] || {}) }, text),
    ),
  );
}

// A row as the queue shows one, its title over its small print; a vertical row as the strip shows its tab.
function demoRow(cls, attrs = {}) {
  if (cls.includes('vertical')) {
    return h('button', { class: cls, type: 'button', tabindex: '-1', ...attrs }, h('span', {}, 'Inbox'), h('span', { class: 'strip-count' }, '3'));
  }
  return h(
    'button',
    { class: cls, type: 'button', tabindex: '-1', ...attrs },
    h('span', { class: 'queue-title' }, 'A write waits'),
    h('span', { class: 'queue-sub' }, '2 hours ago'),
  );
}

function rowStates(cls, current) {
  const states = [
    ['rest', demoRow(cls)],
    cls.includes('option') ? ['cursor', demoRow(cls, { 'aria-selected': 'true' })] : ['hover', demoRow(cls, { 'data-hover': true })],
  ];
  if (current) states.push(['current', demoRow(cls, { 'aria-current': 'true' })]);
  states.push(['disabled', demoRow(cls, { disabled: true })]);
  return states;
}

// A row of a workspace's tree at its depth: a folder, open or shut, or a file when open is null.
function demoTreeRow(depth, name, open, attrs = {}) {
  const indent = Array.from({ length: depth }, () => h('span', { class: 'tree-indent' }));
  let twisty = '';
  if (open !== null) twisty = open ? '▾' : '▸';
  return h(
    'button',
    { class: 'row option tree', type: 'button', tabindex: '-1', ...attrs },
    indent,
    h('span', { class: 'tree-twisty' }, twisty),
    h('span', { class: 'tree-name' }, name),
  );
}

// A tree row in git's state for its file, or for a folder that holds such files.
function demoChangedTreeRow(state, depth, name, open) {
  const row = demoTreeRow(depth, name, open);
  row.dataset.change = state;
  return row;
}

// A file of a workspace's git lists: its state's letter and its path.
function demoChangesRow(state, path, attrs = {}) {
  return h(
    'button',
    { class: 'row option git-file', type: 'button', tabindex: '-1', 'data-change': state, ...attrs },
    h('span', { class: 'git-file-state' }, state[0].toUpperCase()),
    h('span', { class: 'git-file-path' }, path),
  );
}

// A commit of a workspace's remote area: its short hash and its subject.
function demoCommit(hash, subject) {
  return h('li', { class: 'git-commit' }, h('span', { class: 'git-commit-hash' }, hash), h('span', { class: 'git-commit-subject' }, subject));
}

// Code as a workspace's reader draws it, its lines in runs: numbers beside text, a run in a state in its wash with its
// bar in the margin, and a gap where lines were removed.
function demoCodeRuns(runs) {
  const numbers = runs.map(([state, lines, from]) => {
    if (state === 'gap') return h('span', { class: 'git-run git-gap' });
    const shown = lines.map((_, at) => (from === null ? '' : String(from + at))).join('\n') + '\n';
    return h('span', { class: 'git-run git-change margin', 'data-change': state }, shown);
  });
  const texts = runs.map(([state, lines]) => {
    if (state === 'gap') return h('span', { class: 'git-run git-gap' });
    return h('span', { class: 'git-run git-change', 'data-change': state }, lines.join('\n') + '\n');
  });
  return h('div', { class: 'sb-code-runs' }, h('pre', { class: 'sb-numbers' }, numbers), h('pre', {}, texts));
}

// A block of markdown in a state, and one after removed lines.
function demoProseBlocks() {
  return h('div', { class: 'prose sb-prose' },
    h('p', {}, 'A paragraph as it was.'),
    h('p', { class: 'git-change', 'data-change': 'changed' }, 'A paragraph that changed, coloured whole.'),
    h('p', { class: 'git-gap' }, 'A paragraph after lines that were removed.'));
}

// The palette's box as it floats over the window, here in place: its command field, then its rows or its none line.
function demoPalette(query, rows) {
  const list = rows.length
    ? rows.map(([name, group], at) => h('li', { class: 'row option palette-item', role: 'option', 'aria-selected': String(at === 0) }, h('span', { class: 'palette-name' }, name), h('span', { class: 'palette-group' }, group)))
    : [h('li', { class: 'palette-none' }, 'No command matches.')];
  return h(
    'div',
    { class: 'palette-box', role: 'dialog', 'aria-label': 'Example' },
    demoField('input', 'field bare command', query),
    h('ol', { class: 'palette-list', role: 'listbox' }, list),
  );
}

function demoPaneHead() {
  return h(
    'header',
    { class: 'pane-head' },
    h('h3', { class: 'pane-title' }, 'Does the lens keep its sections folded?'),
    h('p', { class: 'sb-pane-small' }, 'at commit c4e807b, in lens'),
  );
}

function demoSpreadHead() {
  return h(
    'header',
    { class: 'pane-head spread' },
    h('p', { class: 'doc-meta' }, h('span', {}, 'sitting 2 of 3'), h('span', {}, '1 unsettled')),
    h('h3', { class: 'pane-title' }, 'How the window draws its rows'),
  );
}

function demoCompactHead() {
  return h(
    'header',
    { class: 'pane-head compact' },
    h('h3', { class: 'pane-title' }, 'Chat'),
    h('span', {}, 'on brain'),
    h('span', {}, 'Opus, high effort'),
  );
}

function demoFlushHead() {
  return h(
    'header',
    { class: 'pane-head flush' },
    h('h3', { class: 'pane-title' }, '4 questions wait in lens'),
    h('p', { class: 'sb-pane-small' }, 'Move ', keys('j'), keys('k'), '  Open ', keys('Enter')),
  );
}

function demoColumnHead() {
  return h(
    'div',
    { class: 'pane-head compact flush queue-head' },
    h('h3', { class: 'pane-title' }, 'Waiting for you'),
    h('span', { class: 'queue-count' }, '3'),
  );
}

function demoIcon(cls, attrs = {}) {
  let glyph = icon(ICON_CLOSE);
  if (cls.includes('bubble')) glyph = '3';
  if (cls.includes('float')) glyph = icon(ICON_DOWN);
  return h('button', { class: cls, type: 'button', tabindex: '-1', title: 'Close', ...attrs }, glyph);
}

// A field holding a few words, as an input or, for a textarea, as its text.
function demoField(tag, cls, text, attrs = {}) {
  if (tag === 'textarea') return h('textarea', { class: cls, tabindex: '-1', ...attrs }, text);
  return h('input', { class: cls, type: 'text', tabindex: '-1', value: text, ...attrs });
}

function fieldStates(tag, cls, text) {
  return [
    ['rest', demoField(tag, cls, text)],
    ['focus', demoField(tag, cls, text, { 'data-focus': true })],
    ['disabled', demoField(tag, cls, text, { disabled: true })],
  ];
}

// A textarea field in the states of any field, with the two a growing box adds: empty and many lines.
function growStates(cls, text) {
  const many = Array.from({ length: 14 }, (_, i) => `${text}, line ${i + 1}`).join('\n');
  const [rest, focus, disabled] = fieldStates('textarea', cls, text);
  const empty = demoField('textarea', cls, '', { placeholder: text });
  const manyLines = demoField('textarea', cls, many);
  return [
    ['empty', empty],
    rest,
    ['many lines', manyLines],
    focus,
    disabled,
  ];
}

// A bare field in the frame that draws its box, the lens's query bar.
function demoFrame(attrs = {}) {
  return h('div', { class: 'field query', ...attrs }, demoField('input', 'field bare mono', 'section:lens'));
}

// A code mark with its weight where it ends, in a line of code.
function demoMark(cls, attrs = {}) {
  return h('span', { class: cls, tabindex: '-1', ...attrs }, 'lens.', h('span', { class: 'token function' }, 'fold'), '()', h('span', { class: 'mark-weight' }, '+2'));
}

function demoNestedMark() {
  return h('span', { class: 'mark', tabindex: '-1' }, 'open(', demoMark('mark'), ')');
}

function demoSelect(cls, attrs = {}) {
  return h('button', { class: cls, type: 'button', tabindex: '-1', ...attrs }, h('span', {}, 'Opus'), h('span', { class: 'select-aside' }, 'high'));
}

function demoFocusSelect(attrs = {}) {
  return h('button', { class: 'select subtle', type: 'button', tabindex: '-1', ...attrs }, h('span', { class: 'focus-label' }, 'Focus'), graphChip({ name: 'brain', hue: 205 }), keys('Alt', 'P'));
}

function demoToggle(attrs = {}) {
  return h('button', { class: 'toggle', role: 'switch', type: 'button', tabindex: '-1', 'aria-checked': 'false', ...attrs }, h('span', { class: 'toggle-knob' }));
}

function demoFieldRow(attrs = {}) {
  return h('div', { class: 'sb-pane sb-pane-wide' },
    h('div', { class: 'field-row', ...attrs },
      h('div', { class: 'field-row-label' }, h('span', {}, 'The open agent'), h('span', { class: 'field-row-hint' }, 'The agent the user talks with')),
      h('div', { class: 'field-row-controls' }, demoSelect('select'))));
}

const DEMO_MARKDOWN = [
  'Markdown as both windows draw it: **bold**, `code` and a [link](https://example.com).',
  '',
  '```mermaid',
  'flowchart LR',
  '  state --> draw --> viewport',
  '```',
].join('\n');

// A small drawing for the viewer to show: two boxes and a line, in the ink of the theme.
function demoImage() {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 200 80');
  const parts = [['rect', { x: 10, y: 20, width: 60, height: 40 }], ['rect', { x: 130, y: 20, width: 60, height: 40 }], ['line', { x1: 70, y1: 40, x2: 130, y2: 40 }]];
  for (const [tag, attrs] of parts) {
    const part = document.createElementNS('http://www.w3.org/2000/svg', tag);
    for (const [name, value] of Object.entries(attrs)) part.setAttribute(name, String(value));
    part.setAttribute('fill', 'none');
    part.setAttribute('stroke', 'currentColor');
    svg.append(part);
  }
  return svg;
}

// The image viewer in a frame of its own, at rest and zoomed.
function demoImageViewer(scale) {
  const viewer = imageViewer(demoImage());
  imageViewerDo(viewer, 'zoom', { factor: scale, x: 0, y: 0 });
  const frame = h('div', { class: 'sb-viewer' }, templateNode(imageViewerView(viewer, { framed: true })));
  imageViewerPaint(viewer, frame);
  return frame;
}

// The small drawing as an image's source, a data URL, in the soft ink of the theme, as an attached image is one.
function demoImageSource() {
  const svg = demoImage();
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('color', resolved('--ink-soft'));
  return `data:image/svg+xml,${encodeURIComponent(new XMLSerializer().serializeToString(svg))}`;
}

// An image thumbnail as a conversation draws it, or as the composer does, with its remove button; attrs go on its open
// button, as data-hover for the pointer's hover.
function demoThumb(removable, attrs = {}) {
  let onRemove = null;
  if (removable) onRemove = () => {};
  const node = templateNode(imageThumbView(demoImageSource(), { name: 'diagram.png', onRemove }));
  const open = node.querySelector('.image-thumb-open');
  for (const [name, value] of Object.entries(attrs)) open.setAttribute(name, value);
  return h('div', { class: 'sb-pane sb-pane-small' }, node);
}

const DEMO_FOLDER = { path: '/home/user/Pictures/lens', parent: '/home/user/Pictures', folders: ['drafts', 'shots'], images: ['index.png', 'tree.png', 'reader.png'] };
const DEMO_PLACES = [
  { name: 'Pictures', path: '/home/user/Pictures' },
  { name: 'This session', path: '/home/user/Projects/lens' },
  { name: 'lens', path: '/home/user/Pictures/lens' },
];

// The file explorer in a frame of its own: in a folder with an image marked and the cursor on another, still reading
// its first folder, or in a folder with nothing to show.
function demoExplorer(shown) {
  const explorer = fileExplorer(DEMO_PLACES);
  if (shown === 'empty') fileExplorerShow(explorer, { ...DEMO_FOLDER, folders: [], images: [] });
  if (shown === 'marked') {
    fileExplorerShow(explorer, DEMO_FOLDER);
    const source = demoImageSource();
    for (const name of ['index.png', 'tree.png']) fileExplorerThumb(explorer, `${DEMO_FOLDER.path}/${name}`, source);
    explorer.marked.add(`${DEMO_FOLDER.path}/index.png`);
    explorer.cursor = 3;
  }
  const none = () => {};
  const view = fileExplorerView(explorer, { onOpen: none, onPreview: none, onAttach: none, onClose: none, onChange: none, framed: true });
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(view));
}

// The permission requests the Stylebook shows: a Bash command, a file edit, another tool, and one a subagent asked.
const DEMO_ASKS = {
  bash: { id: 'ask-1', tool: 'Bash', input: { command: 'git push origin lens', description: 'Push the lens branch' } },
  edit: { id: 'ask-2', tool: 'Edit', input: { file_path: '/home/user/Projects/brain/src/Fold.cs', old_string: '    int depth = 1;', new_string: '    int depth = 2;' } },
  other: { id: 'ask-3', tool: 'WebFetch', input: { url: 'https://example.com/notes', prompt: 'What does it say?' } },
  subagent: { id: 'ask-4', tool: 'Write', input: { file_path: '/home/user/Projects/brain/notes.md', content: '# Notes\n' }, agent: 'builder', agentId: 'a71a6953474efca16' },
};

// A permission request as brain code's window draws it in a conversation, waiting or answering.
function demoPermission(ask, answering = false) {
  const view = permissionRequestView(ask, { answering, onAllow: () => {}, onDeny: () => {} });
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(view));
}

// The questions the Stylebook shows: options with a recommendation, several to choose with words of one's own, and
// words only; and one answered.
const DEMO_QUESTIONS = {
  options: { id: 'question-1', wait: true, state: 'open', questions: [{ text: 'Where should the **lens** keep its index?', options: [{ label: 'SQLite', description: 'One file, queried in place' }, { label: 'Files', description: 'A file per entry, read with grep' }], recommend: 'SQLite', why: 'one file to back up, and queries without a scan.' }] },
  multiple: { id: 'question-2', wait: false, state: 'open', questions: [{ text: 'Which views should show the **tally**?', options: [{ label: 'Home' }, { label: 'Signals' }, { label: 'Review' }], recommend: 'Signals', why: 'it is where the counts are read.', multiple: true, own: true }] },
  words: { id: 'question-3', wait: false, state: 'open', questions: [{ text: 'What should the empty list say?', recommend: 'Nothing waits for you', why: 'it says what is true in four words.' }] },
  answered: { id: 'question-4', wait: true, state: 'answered', questions: [{ text: 'Where should the **lens** keep its index?', options: [{ label: 'SQLite' }, { label: 'Files' }], recommend: 'SQLite', why: 'one file to back up.', own: true }], replies: [{ chosen: ['Files'], words: 'Grep is all I use.' }] },
};

// A question as brain code's window draws it in a conversation: open, answering, answered, closed or ended. choice,
// when given, sets attrs on the option at that index once drawn, as data-hover for the pointer's hover or
// aria-pressed for the user's choice, so the Stylebook shows an open choice in its states.
// Each drawing has a key of its own, since the component keeps one drawn text per key and an element sits in one place.
function demoQuestion(asked, answering = false, state = asked.state, choice = null) {
  const key = `stylebook ${asked.id} ${state} ${answering} ${JSON.stringify(choice)}`;
  const view = questionView({ ...asked, state }, { key, answering, onChange: () => {}, onAnswer: async () => false, onClose: async () => false });
  const node = templateNode(view);
  if (choice) {
    const option = node.querySelectorAll('.question-choice')[choice.index];
    for (const [name, value] of Object.entries(choice.attrs)) {
      option.setAttribute(name, value);
    }
  }
  return h('div', { class: 'sb-pane sb-pane-wide' }, node);
}

// The stop confirm as the windows ask it: before an interrupt stops the agents a brain code session sent, and before
// closing a chat stops what it started, a build that holds a slot among it; asking or stopping.
const DEMO_STOPS = {
  agents: {
    question: 'Stopping this turn stops the agents it sent:',
    work: [{ kind: 'builder', what: 'Build the lens' }, { kind: 'validator', what: 'Validate the lens' }],
    stop: 'Stop them and the turn',
    keep: 'Keep running',
  },
  chat: {
    question: 'Closing this chat stops what it started:',
    work: [{ kind: 'agent', what: 'builder: Build the lens' }, { kind: 'process', what: 'scripts/dotnet-guard.sh test brain.slnx', slot: true }],
    stop: 'Stop them and close',
    keep: 'Keep the chat',
  },
};

function demoStopConfirm(shown, stopping) {
  const view = stopConfirmView({ ...shown, stopping }, { onStop: () => {}, onKeep: () => {} });
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(view));
}

// The processes a session's agent started, as the process fold lists them: a build that holds a slot, a test run, and
// a shell that went idle four minutes before the Stylebook's now.
const DEMO_NOW = Date.parse('2026-11-07T10:00:00Z');
const DEMO_JOBS = [
  { pid: 4101, command: 'scripts/dotnet-guard.sh build brain.slnx --nologo', slot: true },
  { pid: 4102, command: 'tests/run.sh 2>&1 | tail -40', slot: false },
  { pid: 4103, command: 'while true; do inotifywait -e modify src; done', slot: false, idle: '2026-11-07T09:56:00Z' },
];

function demoProcessFold(work, open, stopped = null) {
  const shown = { work, open, now: DEMO_NOW, stopping: (job) => job.pid === stopped };
  const view = processFoldView(shown, { onToggle: () => {}, onStop: () => {} });
  let room = 'sb-pane sb-pane-wide';
  if (open) room = 'sb-pane sb-pane-wide sb-fold-room';
  return h('div', { class: room }, templateNode(view));
}

// The agents, commits and voices the Stylebook shows brain code's agent views with.
const DEMO_SENT = {
  builder: { agent: 'a6bc33033f88b2057', tool: 'toolu_1', type: 'builder', kind: 'builder', description: 'Build brain code folder argument', state: 'running', started: '2026-11-06T19:00:00Z' },
  validator: { agent: 'aeb4a3934e995aec2', tool: 'toolu_2', type: 'validator', kind: 'validator', description: 'Validate brain code folder slice', state: 'running', started: '2026-11-06T19:30:00Z' },
  ready: { agent: 'a4651004e4f80adbd', tool: 'toolu_3', type: 'brain-ready', kind: 'ready', description: 'Readiness check folder slice', state: 'finished', verdict: 'READY', started: '2026-11-06T18:58:00Z', ended: '2026-11-06T18:59:30Z' },
};
// Tool calls as brain code's conversation holds them, and the results they gave back, for the second flow's components.
const DEMO_CALLS = {
  read: { id: 'c1', name: 'Read', subject: 'src/Brain/Code/web/app.js', state: 'done', input: { file_path: 'src/Brain/Code/web/app.js' } },
  edit: { id: 'c2', name: 'Edit', subject: 'src/Brain/Code/web/code.css', state: 'done', input: { file_path: 'src/Brain/Code/web/code.css', old_string: '.code-tool { color: var(--muted); }', new_string: '.code-tool { color: var(--ink); }\n.code-tool-name { font-weight: 600; }' } },
  bash: { id: 'c3', name: 'Bash', subject: 'List the files', state: 'done', input: { command: 'ls src/Brain/Code', description: 'List the files' } },
  failed: { id: 'c4', name: 'Bash', subject: 'Run the gate', state: 'failed', input: { command: 'tests/run.sh' } },
  running: { id: 'c5', name: 'Grep', subject: 'toolCallView', state: 'running', input: { pattern: 'toolCallView', path: 'src' } },
  brain: { id: 'c6', name: 'Bash', subject: 'stylebook', brain: 'lookup', state: 'done', input: { command: 'brain lookup stylebook' } },
};

const DEMO_RESULTS = {
  read: { text: '     1\t// brain code\'s window\n     2\timport { html } from \'/lit-html.js\';\n     3\t', failed: false },
  bash: { text: 'AskContract.cs\nClaudeFiles.cs\nCodeServer.cs', failed: false },
  failed: { text: 'build failed: 2 errors', failed: true },
  brain: { text: '', failed: false, items: [{ ref: 'project:stylebook', title: 'The window\'s look lives in one stylebook' }, { ref: 'project:workspace-shared-stylebook', title: 'brain\'s window and the workspace draw from one stylebook' }] },
};

function demoToolCall(call, selected = false, attrs = {}) {
  const node = templateNode(toolCallView(call, { selected, onOpen: () => {} }));
  for (const [name, value] of Object.entries(attrs)) {
    node.querySelector('.tool-call').setAttribute(name, value);
  }
  return h('div', { class: 'sb-pane sb-pane-wide' }, node);
}

// A tool run drawn open, as the user opens it.
function openRun(pane) {
  pane.querySelector('.tool-run').open = true;
  return pane;
}

function demoToolRun(calls) {
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(toolRunView(calls, { selected: null, onOpen: () => {} })));
}

function demoToolDetail(call, result) {
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(toolDetailView(call, result)));
}

function demoInfoPanel(call, result) {
  const view = infoPanelView({ title: call.subject, kind: toolCallKind(call) }, toolDetailView(call, result), { onClose: () => {} });
  return h('div', { class: 'sb-pane sb-pane-wide sb-panel' }, templateNode(view));
}

const DEMO_COMMITS = [{ hash: '8124cb8', subject: 'brain code takes a folder, and a session that fails to start says why' }];
const DEMO_REASON = 'Start reads guard, resolve, return; each helper is one rule of a moved session.';
const DEMO_FINDING = 'Continued crams three lookups into one negated condition. Write them as named steps.';

function demoAgentRow(sent, opened = false) {
  const row = agentRowView(sent, { opened, now: Date.parse('2026-11-06T19:45:00Z'), onOpen: () => {} });
  return h('div', { class: 'sb-pane' }, templateNode(row));
}

// A session's agents as brain code's flow draws them: a piece of work that landed after two rounds, its worktree opened
// and removed; one whose validator is in round 1; one still building; and an Explore agent of no work.
const DEMO_FLOW = [
  { tool: 'f1', agent: 'r1', type: 'brain-ready', kind: 'ready', description: 'Check the lens', state: 'finished', verdict: 'NOT READY', asks: true, attached: 'f3', started: '2026-11-06T19:00:00Z', ended: '2026-11-06T19:00:40Z' },
  { tool: 'f2', agent: 'r2', type: 'brain-ready', kind: 'ready', description: 'Check the lens', state: 'finished', verdict: 'READY', attached: 'f3', started: '2026-11-06T19:01:00Z', ended: '2026-11-06T19:01:30Z' },
  {
    tool: 'f3', agent: 'b1', type: 'builder', kind: 'builder', description: 'Build the lens', state: 'finished', work: 'b1', started: '2026-11-06T19:02:00Z', ended: '2026-11-06T19:31:40Z',
    worktree: { path: '/home/user/Projects/lens/.claude/worktrees/lens', opened: '2026-11-06T19:03:00Z', removed: true, removedAt: '2026-11-06T19:34:10Z' },
    signals: { read: true, all: [{ ended: 'fixed' }, { ended: 'fixed' }, { ended: 'kept' }] },
    steps: [
      { kind: 'report', tool: 'f3', at: '2026-11-06T19:15:00Z', round: 0, commits: 1 },
      { kind: 'findings', tool: 'f5', at: '2026-11-06T19:20:00Z', round: 1, commits: 0 },
      { kind: 'answer', tool: 'f3', at: '2026-11-06T19:25:00Z', round: 1, commits: 1 },
      { kind: 'findings', tool: 'f5', at: '2026-11-06T19:28:00Z', round: 2, commits: 0 },
      { kind: 'answer', tool: 'f3', at: '2026-11-06T19:31:00Z', round: 2, commits: 1 },
      { kind: 'verdict', tool: 'f5', at: '2026-11-06T19:35:00Z', commits: 3, landed: 'cbfdcf1..8124cb8' },
    ],
  },
  { tool: 'f4', agent: 'r3', type: 'brain-ready', kind: 'ready', description: 'Check the lens review', state: 'finished', verdict: 'READY', attached: 'f5', started: '2026-11-06T19:16:00Z', ended: '2026-11-06T19:16:20Z' },
  { tool: 'f5', agent: 'v1', type: 'validator', kind: 'validator', description: 'Validate the lens', state: 'finished', work: 'b1', started: '2026-11-06T19:17:00Z', ended: '2026-11-06T19:35:00Z' },
  {
    tool: 'f6', agent: 'b2', type: 'builder', kind: 'builder', description: 'Build the dial', state: 'running', work: 'b2', started: '2026-11-06T19:06:00Z', now: 'Bash tests/run.sh',
    worktree: { path: '/home/user/Projects/lens/.claude/worktrees/dial', opened: '2026-11-06T19:06:40Z', removed: false },
    steps: [
      { kind: 'report', tool: 'f6', at: '2026-11-06T19:21:00Z', round: 0, commits: 2 },
      { kind: 'findings', tool: 'f7', at: '2026-11-06T19:33:00Z', round: 1, commits: 0 },
    ],
  },
  { tool: 'f7', agent: 'v2', type: 'validator', kind: 'validator', description: 'Validate the dial', state: 'running', work: 'b2', started: '2026-11-06T19:24:00Z', now: 'SendMessage b2' },
  { tool: 'f8', agent: 'x1', type: 'Explore', kind: 'other', description: 'Find the old gauge', state: 'finished', started: '2026-11-06T19:08:00Z', ended: '2026-11-06T19:09:00Z' },
  { tool: 'f9', agent: 'b3', type: 'builder', kind: 'builder', description: 'Build the gauge', state: 'running', work: 'b3', started: '2026-11-06T19:37:00Z', now: 'Edit src/Gauge.cs', steps: [] },
];

function demoAgentFlow(agents, open = null) {
  const flow = agentFlowView(agents, { root: 'brain', open, now: Date.parse('2026-11-06T19:45:00Z'), onOpen: () => {} });
  return h('div', { class: 'sb-pane sb-pane-wide sb-inset' }, templateNode(flow));
}

function demoTrailOutline(step) {
  const shown = { tool: 'toolu_1', at: '2026-11-06T19:37:06Z', commits: 0, landed: null, ...step };
  return h('div', { class: 'sb-pane' }, templateNode(trailOutlineView(shown, () => {})));
}

function demoAgentMessage(from) {
  const message = agentMessageView(from, markdownView('1. **BLOCKER** `code.css` is empty.\n2. One state gets one word.'));
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(message));
}

function demoTrailStep(step) {
  const shown = { agent: 'a6bc33033f88b2057', at: '2026-11-06T19:37:06Z', text: '', landed: null, ...step };
  const view = trailStepView(shown, markdownView('All eight findings are fixed; the gate is green.'));
  return h('div', { class: 'sb-pane sb-pane-wide' }, h('ol', { class: 'sb-plain-list' }, templateNode(view)));
}

function demoSignal(signal) {
  const view = navaroneSignalView({ id: '0000180', check: 'split-complexity', name: 'CodeSessions.Start', ...signal });
  return h('div', { class: 'sb-pane sb-pane-wide' }, h('ul', { class: 'sb-plain-list' }, templateNode(view)));
}

function demoVerdict(verdict, questions) {
  let asks = null;
  if (questions) asks = markdownView(questions);
  return h('div', { class: 'sb-pane' }, templateNode(verdictView(verdict, asks)));
}

// A project row with the cursor on it or off, and the pending bar with two projects waiting, as the window draws them.
function demoProjectRow(selected, reviews, disputes, current = false, path = '/home/user/Projects/brain/docs/brain/graph') {
  const row = projectRow({ chip: graphChip({ name: 'brain', hue: 205 }), path, reviews, disputes, selected, current, onChoose: () => {} });
  return h('div', { class: 'sb-pane' }, h('ol', { class: 'palette-list' }, templateNode(row)));
}

function demoWaitingCounts(reviews, disputes) {
  return h('div', { class: 'sb-pane' }, templateNode(waitingCounts({ reviews, disputes })));
}

function demoPendingBar(projects) {
  return h('div', { class: 'sb-pane sb-pane-wide sb-bar' }, templateNode(pendingBar({ projects: projects.map(([name, hue, reviews, disputes]) => ({ name, chip: graphChip({ name, hue }), reviews, disputes })), onPick: () => {} })));
}

const DEMO_OPTIONS = [
  { text: 'Gemma4 (local)', hint: 'not yet', disabled: true },
  { text: 'Claude Opus', chosen: true },
  { text: 'Claude Sonnet' },
  { text: 'Claude Haiku' },
];

// An option list with the cursor on its third option: the keys in it or in another list.
function demoOptionList(current) {
  return h('div', { class: 'sb-pane' }, templateNode(optionList({ label: 'Models', options: DEMO_OPTIONS, cursor: 2, current, onPoint: () => {}, onChoose: () => {} })));
}

// A model selector in its dialog box, the keys in its model list. A press moves its cursor, as in Settings.
const demoSelector = { instance: null };

function demoModelSelector() {
  const models = [{ id: 'gemma4', text: 'Gemma4 (local)', later: true }, ...MODELS.map(([id, text]) => ({ id, text }))];
  const efforts = EFFORTS.map((effort) => ({ id: effort, text: effort }));
  if (!demoSelector.instance) demoSelector.instance = modelSelector(models, efforts, { model: models[1].id, effort: 'high' });
  const view = modelSelectorView(demoSelector.instance, { chosen: [models[1].id, 'high'], onMoved: renderMain, onChoose: () => {} });
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(view));
}

// The embedding model picker as the dialog holds it: the list with the cursor on a model, or what its switch costs.
const DEMO_EMBEDDERS = [{ name: 'bge-small-en-v1.5', megabytes: 133 }, { name: 'snowflake-arctic-embed-m', megabytes: 436 }];

function demoEmbedderPicker(cost) {
  const picker = embedderPicker(DEMO_EMBEDDERS, DEMO_EMBEDDERS[0].name);
  embedderPickerKey(picker, 'j');
  if (cost) askedCost(picker, { model: DEMO_EMBEDDERS[1].name, download: 436, nodes: 412 });
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(embedderPickerView(picker, { onMoved: () => {}, onChoose: () => {} })));
}

// The setting's control with the switch under it, in the step the switch is on.
function demoSwitchRow(now) {
  const view = { active: DEMO_EMBEDDERS[0].name, switch: now };
  return h('div', { class: 'sb-pane sb-pane-wide' }, templateNode(embedderSwitchRow(demoSelect('select'), view)));
}

// A control at rest and hovered. Its content is made anew for each state, since a node sits in one place only.
function hoverStates(cls, content) {
  return [
    ['rest', demoButton(cls, content())],
    ['hover', demoButton(cls, content(), { 'data-hover': true })],
  ];
}

function linkStates(cls, content) {
  return [...hoverStates(cls, content), ['disabled', demoButton(cls, content(), { disabled: true })]];
}

// Each state shown in the place that gives it its type or its width: a title, an edge's target, a pane.
function inPlace(cls, states) {
  return states.map(([state, el]) => [state, h('div', { class: cls }, el)]);
}

function componentsView() {
  const project = graphChip({ name: 'brain', hue: 205 });
  return [
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Button, .button'),
      stateRow('plain', buttonStates('button', 'No, change it')),
      stateRow('yes', buttonStates('button yes', 'Yes, land it')),
      stateRow('no', buttonStates('button no', 'No')),
      stateRow('signed', [['yes', demoButton('button yes signed', 'Yes, land it')], ['no', demoButton('button no signed', 'No')]]),
      stateRow('in a tone', [['good', demoButton('button tone-good', 'Step 1')], ['bad', demoButton('button tone-bad', 'Step 2')]]),
      stateRow('highlight', buttonStates('button highlight', 'Reload brain code')),
      stateRow('with a key', [['rest', h('button', { class: 'button', type: 'button', tabindex: '-1' }, 'Send', keys('Ctrl', 'Enter'))]]),
      stateRow('floating', hoverStates('button float', () => ['Comment', keys('Alt', 'C')]))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Icon button, .icon'),
      stateRow('glyph', [['rest', demoIcon('icon')], ['hover', demoIcon('icon', { 'data-hover': true })], ['pressed', demoIcon('icon', { 'aria-pressed': 'true' })], ['disabled', demoIcon('icon', { disabled: true })]]),
      stateRow('bubble', [['rest', demoIcon('icon bubble')], ['hover', demoIcon('icon bubble', { 'data-hover': true })], ['open', demoIcon('icon bubble', { 'aria-expanded': 'true' })], ['orphan', demoIcon('icon bubble orphan')]]),
      stateRow('float', [['rest', demoIcon('icon float')], ['hover', demoIcon('icon float', { 'data-hover': true })]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Link, .link'),
      stateRow('link', linkStates('link', () => 'Show whole files')),
      stateRow('accent', [['rest', h('span', { class: 'prose' }, 'Read ', h('a', { class: 'link accent', href: '#', tabindex: '-1', onclick: (event) => event.preventDefault() }, 'the stylebook'), ' first.')]]),
      stateRow('plain', inPlace('edge-target', linkStates('link accent plain', () => 'What brain is'))),
      stateRow('subtle', linkStates('link subtle', () => [keys('Esc'), 'Back to Evidence'])),
      stateRow('heading', inPlace('pane-title', hoverStates('link heading', () => 'A dream of the stylebook'))),
      stateRow('rename', inPlace('pane-title', hoverStates('link heading rename', () => 'How the window draws its rows')))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Fold, .fold'),
      stateRow('in a diff', inPlace('sb-pane', hoverStates('fold', () => '⋯ 12 unchanged lines')))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Code mark, .mark'),
      stateRow('mark', inPlace('sb-code', [
        ['rest', demoMark('mark')],
        ['hover', demoMark('mark', { 'data-hover': true })],
        ['focus', demoMark('mark', { 'data-focus': true })],
        ['current', demoMark('mark mark-at')],
        ['nested', demoNestedMark()],
      ])),
      stateRow('link', inPlace('sb-code', [['rest', demoMark('mark mark-link')], ['hover', demoMark('mark mark-link', { 'data-hover': true })]]))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Field, .field'),
      stateRow('field', fieldStates('input', 'field', '2026-11-03')),
      stateRow('serif', growStates('field serif', 'Your comment on this text')),
      stateRow('mono, a textarea', growStates('field mono', 'The commit message')),
      stateRow('mono', fieldStates('input', 'field mono', 'section:lens')),
      stateRow('writing', growStates('field writing', 'Write to the agent')),
      stateRow('bare writing', growStates('field bare writing', 'Your words for the agent')),
      stateRow('low, one line at rest', growStates('field bare writing low', 'Your words')),
      stateRow('search', fieldStates('input', 'field search', 'stylebook')),
      stateRow('query, a frame', [['rest', demoFrame()], ['focus', demoFrame({ 'data-focus': true })]]),
      stateRow('command', [['rest', h('div', { class: 'sb-pane' }, demoField('input', 'field bare command', 'Type a command'))]]),
      stateRow('heading', [['rest', h('div', { class: 'sb-pane sb-pane-wide' }, demoField('input', 'field bare heading', 'How the window draws its rows'))]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Select, .select, components/select.css'),
      stateRow('select', [['rest', demoSelect('select')], ['hover', demoSelect('select', { 'data-hover': true })], ['open', demoSelect('select', { 'aria-expanded': 'true' })], ['keys on it', demoSelect('select', { 'data-focus': true })], ['disabled', demoSelect('select', { disabled: true })]]),
      stateRow('subtle', [['rest', demoFocusSelect()], ['hover', demoFocusSelect({ 'data-hover': true })]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Option list, .option-list, components/option-list.css'),
      stateRow('options', [['rest, not yet, in use, cursor', demoOptionList(false)], ['keys in it', demoOptionList(true)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Project row, .project-row, components/project-row.css'),
      stateRow('in the focus picker', [['rest', demoProjectRow(false, 3, 1)], ['cursor', demoProjectRow(true, 3, 1)], ['in focus', demoProjectRow(false, 3, 1, true)], ['nothing waiting', demoProjectRow(false, 0, 0)], ['all, no path', demoProjectRow(false, 4, 3, false, null)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Waiting counts, .waiting-counts, components/waiting-counts.css'),
      stateRow('reviews and disputes', [['both waiting', demoWaitingCounts(1, 2)], ['nothing waiting', demoWaitingCounts(0, 0)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Pending bar, .pending-bar, components/pending-bar.css'),
      stateRow('along the bottom of Home', [['two projects waiting', demoPendingBar([['brain', 205, 3, 1], ['studio', 20, 0, 2]])]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Model selector, .model-selector, components/model-selector.css'),
      stateRow('in its dialog', [['keys in the models', demoModelSelector()]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Embedding model picker, .embedder-picker, components/embedder-picker.css'),
      stateRow('in its dialog', [['the list, cursor on a model', demoEmbedderPicker(false)], ['what a switch costs', demoEmbedderPicker(true)]]),
      stateRow('under the setting', [
        ['downloading', demoSwitchRow({ phase: 'download', to: DEMO_EMBEDDERS[1].name, done: 180e6, total: 436e6 })],
        ['embedding', demoSwitchRow({ phase: 'embed', to: DEMO_EMBEDDERS[1].name, done: 120, total: 412 })],
        ['failed', demoSwitchRow({ phase: 'failed', to: DEMO_EMBEDDERS[1].name, error: 'the download failed' })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Markdown, .prose, shared/prose.css'),
      stateRow('as the windows draw it', [['text, code and a diagram, a click opens the viewer', h('div', { class: 'sb-pane sb-pane-wide' }, markdownView(DEMO_MARKDOWN))]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Image viewer, .image-viewer, shared/components/image-viewer.css'),
      stateRow('in its frame', [['the whole image', demoImageViewer(1)], ['zoomed', demoImageViewer(1.6)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Image thumbnail, .image-thumb, shared/components/image-thumb.css'),
      stateRow('where an image was said', [['rest', demoThumb(false)], ['hover', demoThumb(false, { 'data-hover': '' })]]),
      stateRow('in the composer', [['waiting to be sent', demoThumb(true)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'File explorer, .file-explorer, shared/components/file-explorer.css'),
      stateRow('in its frame', [['an image marked, the cursor on another', demoExplorer('marked')], ['reading', demoExplorer('reading')], ['nothing here', demoExplorer('empty')]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Permission request, .permission-request, shared/components/permission-request.css'),
      stateRow('what the call will do', [['a Bash command', demoPermission(DEMO_ASKS.bash)], ['a file edit', demoPermission(DEMO_ASKS.edit)], ['another tool', demoPermission(DEMO_ASKS.other)]]),
      stateRow('who asked', [['a subagent', demoPermission(DEMO_ASKS.subagent)]]),
      stateRow('answered', [['answering', demoPermission(DEMO_ASKS.bash, true)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Question, .question, shared/components/question.css'),
      stateRow('what it asks', [['options, recommended', demoQuestion(DEMO_QUESTIONS.options)], ['several, own words', demoQuestion(DEMO_QUESTIONS.multiple)], ['own words only', demoQuestion(DEMO_QUESTIONS.words)]]),
      stateRow('a choice', [['hover', demoQuestion(DEMO_QUESTIONS.options, false, 'open', { index: 1, attrs: { 'data-hover': '' } })], ['pressed', demoQuestion(DEMO_QUESTIONS.options, false, 'open', { index: 0, attrs: { 'aria-pressed': 'true' } })]]),
      stateRow('answered', [['answering', demoQuestion(DEMO_QUESTIONS.options, true)], ['answered', demoQuestion(DEMO_QUESTIONS.answered)], ['closed', demoQuestion(DEMO_QUESTIONS.options, false, 'closed')], ['ended', demoQuestion(DEMO_QUESTIONS.options, false, 'ended')]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Stop confirm, .stop-confirm, shared/components/stop-confirm.css'),
      stateRow('what it stops', [['agents a session sent', demoStopConfirm(DEMO_STOPS.agents, false)], ['a build slot held', demoStopConfirm(DEMO_STOPS.chat, false)]]),
      stateRow('answered', [['stopping', demoStopConfirm(DEMO_STOPS.agents, true)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Process fold, .process-fold, shared/components/process-fold.css'),
      stateRow('folded', [['all running', demoProcessFold(DEMO_JOBS.slice(0, 2), false)], ['one idle', demoProcessFold(DEMO_JOBS, false)]]),
      stateRow('folded out', [['running, a slot held, idle', demoProcessFold(DEMO_JOBS, true)], ['stopping', demoProcessFold(DEMO_JOBS, true, 4102)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Agent row, .agent-row, shared/components/agent-row.js'),
      stateRow('in brain code’s list', [
        ['running', demoAgentRow(DEMO_SENT.builder)],
        ['finished', demoAgentRow({ ...DEMO_SENT.builder, state: 'finished', ended: '2026-11-06T19:42:00Z' })],
        ['stopped', demoAgentRow({ ...DEMO_SENT.builder, state: 'stopped', ended: '2026-11-06T19:20:00Z' })],
        ['failed', demoAgentRow({ ...DEMO_SENT.builder, state: 'failed', ended: '2026-11-06T19:05:00Z' })],
        ['open', demoAgentRow(DEMO_SENT.validator, true)],
        ['doing now', demoAgentRow({ ...DEMO_SENT.builder, now: 'Bash tests/run.sh' })],
        ['READY', demoAgentRow(DEMO_SENT.ready)],
        ['NOT READY', demoAgentRow({ ...DEMO_SENT.ready, verdict: 'NOT READY' })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Agent flow, .agent-flow, shared/components/agent-flow.css'),
      stateRow('a session’s agents', [
        ['three pieces of work and an agent of no work', demoAgentFlow(DEMO_FLOW)],
        ['the validator open', demoAgentFlow(DEMO_FLOW.slice(5, 7), 'f7')],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Agent message, .agent-message, shared/components/agent-message.js'),
      stateRow('in a conversation', [['from another agent', demoAgentMessage('validator')], ['sent by the agent', demoAgentMessage(null)]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Trail step, .trail-step, shared/components/trail-step.js'),
      stateRow('a piece of work’s story', [
        ['report', demoTrailStep({ kind: 'report', round: 0, commits: DEMO_COMMITS })],
        ['findings', demoTrailStep({ kind: 'findings', round: 1, commits: [] })],
        ['answer', demoTrailStep({ kind: 'answer', round: 1, commits: DEMO_COMMITS })],
        ['report', demoTrailStep({ kind: 'verdict', commits: [] })],
        ['landed', demoTrailStep({ kind: 'verdict', landed: 'cbfdcf1..8124cb8', commits: DEMO_COMMITS })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Trail outline, .trail-outline, shared/components/trail-step.js'),
      stateRow('a step in the agent list', [
        ['report', demoTrailOutline({ kind: 'report', round: 0, commits: 2 })],
        ['findings', demoTrailOutline({ kind: 'findings', round: 1 })],
        ['answer', demoTrailOutline({ kind: 'answer', round: 1, commits: 1 })],
        ['landed', demoTrailOutline({ kind: 'verdict', landed: 'cbfdcf1..8124cb8' })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Navarone signal, .navarone-signal, shared/components/navarone-signal.js'),
      stateRow('how it ended', [
        ['open', demoSignal({ ended: 'open' })],
        ['fixed', demoSignal({ ended: 'fixed' })],
        ['answered', demoSignal({ ended: 'answered', reason: DEMO_REASON })],
        ['kept', demoSignal({ ended: 'kept', reason: DEMO_REASON })],
        ['replaced', demoSignal({ ended: 'replaced', reason: DEMO_REASON, finding: DEMO_FINDING })],
        ['clash', demoSignal({ ended: 'clash', reason: DEMO_REASON, finding: DEMO_FINDING })],
        ['judged', demoSignal({ ended: 'judged', reason: DEMO_REASON, finding: DEMO_FINDING, judgement: 'replace', words: 'Split it.' })],
        ['large', demoSignal({ ended: 'large', reason: 'The router is one switch; a route table is the fix.' })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Verdict, .verdict, shared/components/verdict.js'),
      stateRow('a readiness check', [['READY', demoVerdict('READY', null)], ['NOT READY', demoVerdict('NOT READY', '1. Which folder does the session start in?')]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Tool call, .tool-call, shared/components/tool-call.css'),
      stateRow('a line of the second flow', [
        ['done', demoToolCall(DEMO_CALLS.read)],
        ['hover', demoToolCall(DEMO_CALLS.read, false, { 'data-hover': '' })],
        ['shown in the panel', demoToolCall(DEMO_CALLS.read, true)],
        ['running', demoToolCall(DEMO_CALLS.running)],
        ['failed', demoToolCall(DEMO_CALLS.failed)],
        ['a brain query', demoToolCall(DEMO_CALLS.brain)],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Tool run, .tool-run, shared/components/tool-run.css'),
      stateRow('calls folded into a line', [
        ['shut', demoToolRun([DEMO_CALLS.read, DEMO_CALLS.read, DEMO_CALLS.read, DEMO_CALLS.read, DEMO_CALLS.bash, DEMO_CALLS.bash])],
        ['with a failed call', demoToolRun([DEMO_CALLS.bash, DEMO_CALLS.failed, DEMO_CALLS.brain])],
        ['open', openRun(demoToolRun([DEMO_CALLS.edit, DEMO_CALLS.bash, DEMO_CALLS.brain, DEMO_CALLS.running]))],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Information panel, .info-panel, shared/components/info-panel.css'),
      stateRow('opened by a click, closed by its button', [
        ['a command', demoInfoPanel(DEMO_CALLS.bash, DEMO_RESULTS.bash)],
        ['a brain query', demoInfoPanel(DEMO_CALLS.brain, DEMO_RESULTS.brain)],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Tool detail, .tool-detail, shared/components/tool-detail.css'),
      stateRow('by the kind of call', [
        ['an edit, as a diff', demoToolDetail(DEMO_CALLS.edit, { text: 'The file was edited.', failed: false })],
        ['a command and its output', demoToolDetail(DEMO_CALLS.bash, DEMO_RESULTS.bash)],
        ['a file read, as code', demoToolDetail(DEMO_CALLS.read, DEMO_RESULTS.read)],
        ['an image read, as its thumbnail', demoToolDetail(DEMO_CALLS.read, { text: '', failed: false, images: [demoImageSource()] })],
        ['a brain query, as a list', demoToolDetail(DEMO_CALLS.brain, DEMO_RESULTS.brain)],
        ['another tool', demoToolDetail(DEMO_CALLS.running, { text: 'src/Brain/Code/web/app.js:20', failed: false })],
      ]),
      stateRow('not yet, or not well', [
        ['running', demoToolDetail(DEMO_CALLS.bash, null)],
        ['failed', demoToolDetail(DEMO_CALLS.failed, DEMO_RESULTS.failed)],
        ['a brain query that found nothing', demoToolDetail(DEMO_CALLS.brain, { text: '', failed: false, items: [] })],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Toggle, .toggle, shared/components/toggle.css'),
      stateRow('switch', [['off', demoToggle()], ['hover', demoToggle({ 'data-hover': true })], ['on', demoToggle({ 'aria-checked': 'true' })], ['disabled', demoToggle({ disabled: true })]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Field row, .field-row, shared/components/field-row.css'),
      stateRow('in a form', [['rest', demoFieldRow()], ['current', demoFieldRow({ 'aria-current': 'true' })]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Chip, .chip'),
      stateRow('as a label', [
        ['neutral', graphChip({ name: 'personal', hue: null })],
        ['project', project],
        ['tag', h('span', { class: 'chip tag' }, 'review')],
        ['stale', h('span', { class: 'chip chip-stale' }, 'older code')],
      ]),
      stateRow('as a filter', buttonStates('chip tag', 'review'))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Segmented switch, .segmented'),
      stateRow('pressed', [['rest', demoSwitch('group', 0)], ['hover', demoSwitch('group', 0, { 1: { 'data-hover': true } })]]),
      stateRow('checked', [['rest', demoSwitch('radio', 1)], ['disabled', demoSwitch('radio', 1, { 2: { disabled: true } })]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Row, .row'),
      stateRow('plain', rowStates('row queue-item', true)),
      stateRow('option', [
        ...rowStates('row option queue-item', false),
        ['cursor, keys in its list', demoRow('row option queue-item', { 'aria-selected': 'true', 'aria-current': 'true' })],
      ]),
      stateRow('compact', rowStates('row compact queue-item', false)),
      stateRow('menu', [['rest', demoRow('row menu queue-item')], ['focus', demoRow('row menu queue-item', { 'data-focus': true })], ['hover', demoRow('row menu queue-item', { 'data-hover': true })]]),
      stateRow('vertical, a tab of the strip', [...rowStates('row vertical', true), ['pressed, a toggle on', demoRow('row vertical', { 'aria-pressed': 'true' })]]),
      stateRow('tree, a workspace’s', inPlace('sb-pane', [
        ['folder', demoTreeRow(0, 'src', true)],
        ['shut', demoTreeRow(0, 'docs', false)],
        ['file', demoTreeRow(1, 'Program.cs', null)],
        ['cursor', demoTreeRow(1, 'Cli.cs', null, { 'aria-selected': 'true' })],
        ['open', demoTreeRow(2, 'README.md', null, { 'aria-current': 'true' })],
      ])),
      stateRow('git file, a workspace’s git lists', inPlace('sb-pane', [
        ['rest', demoChangesRow('changed', 'src/Fold.cs')],
        ['cursor', demoChangesRow('added', 'src/Lens.cs', { 'aria-selected': 'true' })],
        ['deleted', demoChangesRow('deleted', 'docs/old.md')],
        ['untracked', demoChangesRow('untracked', 'notes.txt')],
      ])),
      stateRow('git commit, a workspace’s remote area', inPlace('sb-pane', [
        ['to push', h('ul', { class: 'sb-plain-list' }, demoCommit('3f2a9c1', 'The git view fills the window'))],
        ['long', h('ul', { class: 'sb-plain-list' }, demoCommit('a81b0de', 'A subject so long that it is cut at the edge of the remote area, not wrapped'))],
      ]))),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Git change, .git-change'),
      stateRow('in the tree', inPlace('sb-pane', [
        ['added', demoChangedTreeRow('added', 1, 'Lens.cs', null)],
        ['changed', demoChangedTreeRow('changed', 1, 'Fold.cs', null)],
        ['deleted', demoChangedTreeRow('deleted', 1, 'Old.cs', null)],
        ['untracked', demoChangedTreeRow('untracked', 1, 'notes.txt', null)],
        ['holds changes', demoChangedTreeRow('changed', 0, 'src', true)],
      ])),
      stateRow('in code', [
        ['lines', demoCodeRuns([
          [null, ['class Fold', '{'], 1],
          ['changed', ['    int depth = 2;'], 3],
          ['added', ['    int width;'], 4],
          ['gap', [], null],
          ['untracked', ['    // new'], 5],
          [null, ['}'], 6],
        ])],
        ['a diff', demoCodeRuns([
          [null, ['{'], 1],
          ['deleted', ['    int depth = 1;'], null],
          ['added', ['    int depth = 2;'], 2],
          [null, ['}'], 3],
        ])],
      ]),
      stateRow('in markdown', [['blocks', demoProseBlocks()]])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Palette, .palette-box'),
      stateRow('brain’s commands, a workspace’s bar', [
        ['found', demoPalette('stylebook', [['Open the stylebook', 'view'], ['README.md', 'docs']])],
        ['none', demoPalette('zzz', [])],
      ])),
    h('section', { class: 'doc-section sb-group' },
      h('h2', {}, 'Pane head, .pane-head'),
      stateRow('over panes', [['rest', h('div', { class: 'sb-pane' }, demoPaneHead())]]),
      stateRow('compact', [['rest', h('div', { class: 'sb-pane' }, demoCompactHead())]]),
      stateRow('spread', [['rest', h('div', { class: 'sb-pane sb-pane-wide' }, demoSpreadHead())]]),
      stateRow('flush', [['rest', h('div', { class: 'sb-pane sb-pane-wide sb-inset' }, demoFlushHead())]]),
      stateRow('compact, flush', [['rest', h('div', { class: 'sb-pane sb-inset' }, demoColumnHead())]])),
  ];
}

function renderStylebook(main) {
  const groups = tokenGroups(stylebookTokens());
  fill(main,
    h('div', { class: 'page stylebook' },
      h('p', { class: 'doc-meta' }, 'tokens.css and components.css, shared by brain\u2019s window and every workspace, and brain\u2019s components/, as they draw them now'),
      h('h1', { class: 'doc-title' }, 'Stylebook'),
      groups.map((group) =>
        h('section', { class: 'doc-section sb-group' },
          h('h2', {}, group.title),
          h('ol', { class: 'sb-tokens' }, group.tokens.map((token) => tokenView(group, token))))),
      componentsView()));
}

// The queue column on this page: what it is, in place of a queue of work.
function stylebookList() {
  return h('div', { class: 'sky-legend' },
    h('p', { class: 'sky-note' }, 'Every token and named component of brain\u2019s window and of a workspace\u2019s. A sheet or a script names a token for a color, a text size or a radius, never a value of its own, or the gate fails. A feature places a component and never restyles it.'));
}

registerInbox({
  name: STYLEBOOK,
  label: 'Stylebook',
  bottom: true,
  noChat: true,
  noWaiting: true,
  count: () => 0,
  list: stylebookList,
  render: renderStylebook,
  themed: () => {
    if (state.inbox === STYLEBOOK) renderMain();
  },
});
