// Dreams: the log of every dream, in its own inbox. A dream distills a conversation's unsettled sittings into its
// settled.md; two cheap dreamers draft it, and when they diverge or one is unsure, Opus settles it. The log shows the
// escalations by default, with one switch for all, so the user can see where the cheap dreamers were wrong together. A
// dream opens whole: what it changed in settled.md in one column, each draft, and why it escalated. A deep dream, which
// embeds settled.md into the graph and changes no settled.md, opens as its agent's final words.
//
// It also draws settled.md's changes in one column for a conversation, which shows what dreams changed since the user's
// last sitting when it opens. It reads the log from brain serve, which reads the data repo's dreams/, and changes
// nothing: a dream runs only from a conversation, through the code `brain dream` runs.

const DREAMS = ':dreams';
const dreamLog = {
  // Every dream in the log, the newest first.
  list: [],
  // True when the log shows every dream, false for the escalations only.
  all: false,
  // The dream in view: { id, view }, view null while it loads.
  current: null,
};

async function loadDreams() {
  const { ok, body } = await call('/api/dreams');
  if (!ok) {
    toast(body.error);
    return;
  }
  dreamLog.list = body;
  renderQueue();
  if (state.inbox === DREAMS) renderMain();
}

// The dreams the log lists: in the project the user focuses on, the escalations unless the switch shows all.
function visibleDreams() {
  const project = focus();
  return dreamLog.list.filter((row) => (project === null || row.scope === project) && (dreamLog.all || row.outcome === 'escalated'));
}

function showAllDreams(all) {
  dreamLog.all = all;
  renderQueue();
}

function dreamList() {
  const list = visibleDreams();
  return [
    h(
      'div',
      { class: 'dream-filter' },
      h(
        'span',
        { class: 'segmented', role: 'group', 'aria-label': 'Which dreams' },
        h('button', { 'aria-pressed': String(!dreamLog.all), onclick: () => showAllDreams(false) }, 'Escalations'),
        h('button', { 'aria-pressed': String(dreamLog.all), onclick: () => showAllDreams(true) }, 'All'),
      ),
    ),
    list.length
      ? h(
          'ol',
          {},
          list.map((row) =>
            h(
              'li',
              { class: 'queue-row' },
              h(
                'button',
                { class: 'row queue-item', 'aria-current': String(state.view === 'review' && dreamLog.current?.id === row.id), onclick: () => openDream(row.id) },
                h('span', { class: 'queue-title' }, row.title),
                h('span', { class: 'queue-sub' }, `${row.outcome}, ${ago(row.at)}`),
              ),
              scopeTag(row.origin, row.scope),
            ),
          ),
        )
      : h('p', { class: 'queue-empty' }, dreamLog.list.length ? 'No dream here in this filter.' : 'No dream yet. A conversation dreams from its header.'),
  ];
}

async function openDream(id) {
  dreamLog.current = { id, view: null };
  showInbox(DREAMS);
  const { ok, body } = await call(`/api/dream?id=${encodeURIComponent(id)}`);
  if (dreamLog.current?.id !== id) return;
  if (!ok) {
    toast(body.error);
    dreamLog.current = null;
  } else dreamLog.current.view = body;
  renderQueue();
  renderMain();
}

function renderDreamMain(main) {
  const dream = dreamLog.current;
  if (!dream?.view) {
    fill(main,
      h(
        'div',
        { class: 'quiet' },
        h('h2', {}, 'Dreams'),
        h('p', {}, dream ? 'Opening the dream.' : 'Every dream, logged. The escalations show first: where two cheap dreamers diverged or doubted, and Opus settled it.'),
      ),
    );
    return;
  }
  const view = dream.view;
  const row = view.row;
  if (view.deep) {
    renderDeepDream(main, view);
    return;
  }
  const shape = view.shape;
  fill(main,
    h(
      'div',
      { class: 'page page-code' },
      h(
        'p',
        { class: 'doc-meta' },
        scopeTag(row.origin, row.scope),
        h('span', { class: `dream-outcome ${row.outcome}` }, row.outcome),
        h('span', {}, `sittings ${row.sittings.join(', ')}`),
        h('span', {}, `${shape.phases} phases, ${shape.characters} characters of trail`),
        h('span', {}, row.models.join(', ')),
        h('span', {}, ago(row.at)),
      ),
      h('h1', { class: 'doc-title' }, h('button', { class: 'link heading', title: 'Open the conversation', onclick: () => openConversation(row.conversation) }, row.title)),
      row.why ? h('p', { class: 'dream-why' }, row.why) : null,
      h('section', { class: 'block' }, h('h2', { class: 'block-caption' }, 'settled.md, before and after'), settledDiff(view.before, view.after)),
      h(
        'div',
        { class: 'compare' },
        [...view.drafts, view.escalation].filter(Boolean).map((draft) =>
          h(
            'section',
            { class: 'block' },
            h('h2', { class: 'block-caption' }, draft === view.escalation ? `${draft.model}, the escalation` : draft.model, h('span', { class: 'dream-sure' }, draft.sure ? 'sure' : `unsure: ${draft.doubt}`)),
            draft.text.trim() ? markdownView(draft.text) : h('p', { class: 'pane-empty' }, 'An empty draft.'),
          ),
        ),
      ),
      view.comparison
        ? h('p', { class: 'dream-why' }, `${view.comparison.model} compared the drafts: ${view.comparison.agreed ? 'they agree.' : view.comparison.why}`)
        : null,
    ),
  );
}

// A deep dream: the agent that ran it, the dream that settled the sittings first when one did, why it failed when it
// did, and the agent's final words, its account of what it changed in the graph.
function renderDeepDream(main, view) {
  const { row, deep } = view;
  fill(main,
    h(
      'div',
      { class: 'page page-wide' },
      h(
        'p',
        { class: 'doc-meta' },
        scopeTag(row.origin, row.scope),
        h('span', { class: `dream-outcome ${row.outcome}` }, row.outcome),
        h('span', {}, `${modelLabel(deep.model)}, ${deep.effort} effort`),
        deep.settled ? h('button', { class: 'link', title: 'The dream that settled the sittings first', onclick: () => openDream(deep.settled) }, `after dream ${deep.settled}`) : null,
        h('span', {}, ago(row.at)),
      ),
      h('h1', { class: 'doc-title' }, h('button', { class: 'link heading', title: 'Open the conversation', onclick: () => openConversation(row.conversation) }, row.title)),
      row.why ? h('p', { class: 'dream-why' }, row.why) : null,
      h(
        'section',
        { class: 'block' },
        h('h2', { class: 'block-caption' }, 'What the agent said at the end'),
        deep.words.trim() ? markdownView(deep.words) : h('p', { class: 'pane-empty' }, 'The agent said nothing at the end.'),
      ),
    ),
  );
}

// settled.md before and after, in one column, wrapped as markdown is.
function settledDiff(before, after) {
  const path = 'settled.md';
  const rows = diffRowsOf({ path, before, after });
  if (!rows.some((row) => row.kind !== 'same')) return h('p', { class: 'file-same' }, 'No line changed.');
  return drowsView(rows, false, path, rowsView(rows, false));
}

// The open dream for a chat: its row, its drafts and how it was settled, and settled.md as the dream changed it.
function dreamContext() {
  const view = dreamLog.current?.view;
  if (!view) return null;
  const { row, shape, drafts, comparison, escalation, deep } = view;
  if (deep) return ['The user has this deep dream open. A deep dream embeds a conversation\'s settled.md into the graph, each write on the user\'s yes.', JSON.stringify({ row, deep }, null, 2)].join('\n\n');
  const lines = diffText(view.before, view.after);
  return [
    'The user has this dream open. A dream distills a conversation into settled.md; you cannot change settled.md in this chat.',
    JSON.stringify({ row, shape, drafts, comparison, escalation }, null, 2),
    `settled.md before and after, + added and - removed:\n\n${lines.join('\n')}`,
  ].join('\n\n');
}

function dreamCommands() {
  if (state.inbox !== DREAMS) return [];
  return [dreamLog.all ? { name: 'Show the escalations only', keys: [], run: () => showAllDreams(false) } : { name: 'Show every dream', keys: [], run: () => showAllDreams(true) }];
}

registerInbox({
  name: DREAMS,
  label: 'Dreams',
  ownSide: true,
  // A dream is a record to review, not work that waits for the user.
  count: () => 0,
  start: loadDreams,
  list: dreamList,
  render: renderDreamMain,
  commands: dreamCommands,
  context: dreamContext,
});
