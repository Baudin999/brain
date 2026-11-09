// Conversations: one topic, talked through over days with an agent, in two panes. Settled, what we agreed so far, sits
// beside the trail, the sitting's phases in order, with the input at the bottom. It is closer to editing a manuscript
// with a collaborator in the margin than to messaging.
//
// The window reads the conversations from brain serve, which reads conversations.json, and changes one only through
// brain serve, which runs the code `brain conversation` runs. Each sitting talks with the open agent, which brain serve
// runs and whose answer the window follows as it streams, asking brain serve again each time the sitting changed. Four
// keys drive it, as the design's table says: anywhere in the window, Alt+N starts a conversation and Alt+O opens one;
// inside one, Alt+T ends the sitting and starts the next, and Alt+C comments on the selected text. The header's dream
// button runs the code `brain dream` runs, its "dream into brain" button the code `brain dream --deep` runs, and on
// opening a conversation the window first shows what dreams changed in settled.md since the last sitting, drawn by
// dreams.js. app.js loads after this file and calls into it; everything here runs only once the window has a session.
//
// It meets the rest of the window at one seam: registerInbox at the end of this file puts it in the rail, and focus()
// says which project the user filters on. Everything else it keeps in talks, its own state.
'use strict';

// The conversations inbox in state.inbox. A source's name has no colon, so no source can take it.
const CONVERSATIONS = ':conversations';
const MODELS = [
  ['haiku', 'Haiku'],
  ['sonnet', 'Sonnet'],
  ['opus', 'Opus'],
  ['fable', 'Fable'],
];
const EFFORTS = ['low', 'medium', 'high', 'xhigh', 'max'];
// From this width of the conversation the comment bar holds whole cards beside the trail. A narrower one keeps a thin bar
// whose cards float over the trail, and offers the choice of a larger bar or a larger settled pane.
const WIDE_TALK = 1472;
const LAYOUT_KEY = 'brain.talk-layout';
const FOLLOW_MS = 150;
// The trail counts as read to its end this close to its bottom.
const BOTTOM_PX = 24;
// Each frame the trail moves this share of the way down to the answer's end, so it glides instead of jumping.
const GLIDE = 0.18;
const talks = {
  // Every active conversation, the places Alt+N offers, and each state.md brain could not read.
  conversations: { places: [], list: [], problems: [] },
  // The conversation in view: { ref, view, busy, renaming, pinned, expected }, view null while it loads. pinned holds
  // while the trail follows the answer down; expected is where the window last left the trail's scroll.
  current: null,
  // What the user wrote at the bottom of each conversation, kept until it is sent.
  drafts: {},
  // True when the box at the bottom takes focus on the next draw.
  focus: false,
  // True when the comment being written takes focus on the next draw.
  composeFocus: false,
  // The comment open in the bar, by conversation, sitting, carrying phase and number; every other one stays folded.
  openComment: null,
  // Comments written and not sent, by conversation: { phase, quote, text }, phase 0 being settled. They go out with the
  // text at the bottom as one phase.
  commentDrafts: {},
  // The comment being written: { ref, phase, quote, text }, or null.
  composing: null,
  // The text last selected in the trail or in settled: { phase, quote }, kept while the focus moves to a box or the palette.
  selected: null,
  // On a narrower screen, what gets the room beside the trail: settled, or comments, the bar.
  layout: 'settled',
  // The picker open: { mode: place, open or sitting, ... }, or null. A sitting picker with chat summons a chat instead.
  picker: null,
};
const PLACE_SAYS = {
  personal: 'about you',
  global: 'about the process and the world',
  project: 'about the project',
};

// ---- Loading ----

async function loadConversations() {
  const { ok, body } = await call('/api/conversations');
  if (!ok) {
    toast(body.error);
    return;
  }
  talks.conversations = { places: body.places, list: body.conversations, problems: body.problems };
  renderQueue();
  renderPalette();
  if (talks.picker) renderPickerList();
}

function inConversations() {
  return state.inbox === CONVERSATIONS;
}

// The conversations the inbox lists: the active ones, in the project the user focuses on. A conversation's key is its
// scope, or its project's name, as its ref starts with it.
function visibleConversations(project = focus()) {
  return talks.conversations.list.filter((row) => project === null || row.scope === project);
}

// A conversation's scope or project as a chip, a project in its own color, as every inbox shows it.
function scopeTag(origin, name) {
  const el = h('span', { class: 'chip', 'data-origin': origin.kind === 'project' ? 'project' : 'none' }, name);
  if (origin.hue !== null && origin.hue !== undefined) el.style.setProperty('--hue', String(origin.hue));
  return el;
}

function modelLabel(model) {
  return (MODELS.find(([id]) => id === model) || [model, model])[1];
}

// ---- The inbox's list ----

function conversationList() {
  const all = talks.conversations.list;
  const list = visibleConversations();
  const problems = talks.conversations.problems.map((problem) => h('p', { class: 'queue-empty queue-failing' }, problem));
  if (!all.length) return [h('p', { class: 'queue-empty' }, 'No conversation yet. ', keys('Alt', 'N'), ' starts one.'), problems];
  if (!list.length) return [h('p', { class: 'queue-empty' }, 'Nothing here in this filter.'), problems];
  return [
    h(
      'ol',
      {},
      list.map((row) =>
        h(
          'li',
          { class: 'queue-row' },
          h(
            'button',
            { class: 'row queue-item', 'aria-current': String(state.view === 'review' && talks.current?.ref === row.ref), onclick: () => openConversation(row.ref) },
            h('span', { class: 'queue-title' }, row.title),
            h('span', { class: 'queue-sub' }, `sitting ${row.sittings}, ${ago(row.current.started)}`),
          ),
          scopeTag(row.origin, row.scope),
        ),
      ),
    ),
    problems,
  ];
}

function stepConversation(delta) {
  const list = visibleConversations();
  if (!list.length) return;
  const at = list.findIndex((row) => row.ref === talks.current?.ref);
  openConversation(list[Math.max(0, Math.min(list.length - 1, at < 0 ? 0 : at + delta))].ref);
}

// ---- One conversation ----

async function openConversation(ref) {
  if (talks.current?.ref !== ref) talks.current = { ref, view: null, busy: false, renaming: false };
  showInbox(CONVERSATIONS);
  const { ok, body } = await call(`/api/conversation?ref=${encodeURIComponent(ref)}`);
  if (talks.current?.ref !== ref) return;
  if (!ok) {
    toast(body.error);
    talks.current = null;
  } else {
    talks.current.view = body;
    talks.current.pinned = body.working;
    talks.focus = true;
  }
  renderQueue();
  renderMain();
  if (talks.current?.ref !== ref) return;
  follow(talks.current);
  loadChanges(talks.current, true);
}

// What dreams changed in settled.md since the user's last sitting. On opening, while nothing is said in the sitting yet,
// the window shows it first.
async function loadChanges(talk, opening) {
  const { ok, body } = await call(`/api/conversation/changes?ref=${encodeURIComponent(talk.ref)}`);
  if (talks.current !== talk) return;
  if (!ok) {
    toast(body.error);
    return;
  }
  talk.changes = body;
  if (opening && body.dreams.length && talk.view && !talk.view.phases.length) talk.showChanges = true;
  if (inConversations()) renderMain();
}

function toggleChanges() {
  const talk = talks.current;
  if (!talk?.changes?.dreams.length) {
    toast('No dream changed settled.md since your last sitting.');
    return;
  }
  talk.showChanges = !talk.showChanges;
  talks.focus = !talk.showChanges;
  renderMain();
}

function dreamChanges(changes) {
  return h(
    'section',
    { class: 'talk-changes', 'aria-label': 'Dream changes' },
    h(
      'div',
      { class: 'talk-changes-head' },
      h('h2', { class: 'pane-label' }, 'What dreams changed in settled since your last sitting'),
      h('button', { class: 'link', onclick: toggleChanges }, 'Back to the conversation'),
      keys('Esc'),
    ),
    h(
      'ol',
      { class: 'talk-changes-dreams' },
      changes.dreams.map((dream) =>
        h(
          'li',
          {},
          h('button', { class: 'link', title: 'Read the dream whole in the dream log', onclick: () => openDream(dream.id) }, `${dream.outcome}, sittings ${dream.sittings.join(', ')}`),
          h('span', {}, ` ${dream.why ? `${dream.why}, ` : ''}${ago(dream.at)}`),
        ),
      ),
    ),
    settledDiff(changes.before, changes.after),
  );
}

// The dream button: brain serve runs the code `brain dream` runs, which can take minutes, and then the window shows
// what the dream changed.
async function dreamConversation() {
  const talk = talks.current;
  if (!talk?.view || talk.dreaming) return;
  talk.dreaming = 'settle';
  renderMain();
  try {
    if (await change('dream', { ref: talk.ref })) {
      toast('The dream landed in settled.');
      await loadChanges(talk, false);
      talk.showChanges = Boolean(talk.changes?.dreams.length);
      loadDreams();
    }
  } finally {
    talk.dreaming = null;
    if (inConversations()) renderMain();
  }
}

// The "dream into brain" button: brain serve settles what is unsettled, then the open agent embeds settled.md into the
// graph, each write waiting for the user's yes in node review, and the call returns once the agent ends.
async function deepDreamConversation() {
  const talk = talks.current;
  if (!talk?.view || talk.dreaming) return;
  talk.dreaming = 'deep';
  renderMain();
  try {
    if (await change('deep-dream', { ref: talk.ref })) {
      toast('The deep dream ended; its account is in the dream log.');
      await loadChanges(talk, false);
      loadDreams();
    }
  } finally {
    talk.dreaming = null;
    if (inConversations()) renderMain();
  }
}

// Shows a conversation brain serve just changed, and the inbox as the index now lists it.
function showConversation(view) {
  if (talks.current?.ref !== view.row.ref) talks.current = { ref: view.row.ref, view, busy: false, renaming: false };
  else talks.current.view = view;
  talks.focus = true;
  showInbox(CONVERSATIONS);
  loadConversations();
}

function renderTalkMain(main) {
  delete main.dataset.item;
  const talk = talks.current;
  if (!talk) {
    fill(main,
      h(
        'div',
        { class: 'quiet' },
        h('h2', {}, 'Conversations'),
        h('p', {}, 'One topic, talked through over days. What we agreed so far sits beside the trail of each sitting, and a conversation closes only when you close it.'),
        h('p', { class: 'keys' }, 'New ', keys('Alt', 'N'), '   Open ', keys('Alt', 'O')),
      ),
    );
    return;
  }
  if (!talk.view) {
    fill(main, h('div', { class: 'quiet' }, h('p', {}, 'Opening the conversation.')));
    return;
  }
  const view = talk.view;
  const key = `${view.row.ref}#${view.row.current.number}`;
  const old = main.dataset.talk === key && $('trail-scroll') ? { trail: $('trail-scroll')?.scrollTop, settled: $('settled')?.scrollTop, phases: Number(main.dataset.phases) } : null;
  const typing = document.activeElement?.id === 'talk-input' || document.activeElement?.id === 'comment-input' ? [document.activeElement.id, document.activeElement.selectionStart, document.activeElement.selectionEnd] : null;
  main.dataset.talk = key;
  main.dataset.phases = String(view.phases.length);
  fill(main, talkPage(view, layout(main)));
  if (talk.showChanges) {
    main.focus();
    return;
  }
  placeComments(main);
  const box = $('talk-input');
  box.value = talks.drafts[view.row.ref] || '';
  if ($('comment-input')) $('comment-input').value = talks.composing.text;
  const trail = $('trail-scroll');
  if (old) {
    $('settled').scrollTop = old.settled;
    trail.scrollTop = old.trail;
  }
  // While the trail follows the answer it glides down to its end; otherwise a new phase comes into view at the top, so
  // its answer reads from its first line.
  if (talk.pinned) glideDown(talk);
  else if (old && old.phases < view.phases.length) $(`phase-${old.phases + 1}`)?.scrollIntoView({ block: 'start' });
  else if (!old && view.phases.length) $(`phase-${view.phases.length}`)?.scrollIntoView({ block: 'start' });
  talk.expected = trail.scrollTop;
  if (talk.renaming) {
    const input = $('talk-rename');
    input.focus();
    input.select();
  } else if (talks.composeFocus) $('comment-input')?.focus();
  else if (typing) {
    const where = $(typing[0]) || box;
    where.focus();
    where.setSelectionRange(typing[1], typing[2]);
  } else if (talks.focus) box.focus();
  talks.focus = false;
  talks.composeFocus = false;
}

// How the conversation lays out at this width: whether the screen is wide, and whether the bar holds whole cards.
function layout(main) {
  const wide = main.clientWidth >= WIDE_TALK;
  return { wide, barWide: wide || talks.layout === 'comments' };
}

function loadTalkLayout() {
  try {
    if (localStorage.getItem(LAYOUT_KEY) === 'comments') talks.layout = 'comments';
  } catch {
    // A window without storage starts on the larger settled each time.
  }
}

function setTalkLayout(choice) {
  talks.layout = choice;
  try {
    localStorage.setItem(LAYOUT_KEY, choice);
  } catch {
    // A window without storage keeps the choice until it closes.
  }
  renderMain();
}

function layoutSwitch() {
  return h(
    'span',
    { class: 'segmented talk-layout', role: 'group', 'aria-label': 'Give the room to' },
    h('button', { 'aria-pressed': String(talks.layout === 'comments'), onclick: () => setTalkLayout('comments') }, 'Larger comments'),
    h('button', { 'aria-pressed': String(talks.layout !== 'comments'), onclick: () => setTalkLayout('settled') }, 'Larger settled'),
  );
}

function talkPage(view, fit) {
  const row = view.row;
  const sitting = row.current;
  return h(
    'div',
    { class: `talk ${fit.barWide ? 'bar-wide' : 'bar-thin'} ${fit.wide ? 'wide' : `narrow layout-${talks.layout}`}` },
    h(
      'header',
      { class: 'pane-head spread talk-head' },
      h(
        'p',
        { class: 'doc-meta' },
        scopeTag(row.origin, row.scope),
        h('span', {}, `sitting ${sitting.number} of ${row.sittings}`),
        h('span', {}, `${view.unsettled} unsettled`),
        h('span', {}, `${modelLabel(sitting.model)}${sitting.effort ? `, ${sitting.effort} effort` : ''}`),
        h('span', {}, `born ${row.born}`),
        fit.wide ? null : layoutSwitch(),
        h(
          'span',
          { class: 'talk-actions', role: 'group', 'aria-label': 'Conversation actions' },
          talks.current.changes?.dreams.length
            ? h('button', { class: 'button', 'aria-pressed': String(Boolean(talks.current.showChanges)), title: 'What dreams changed in settled.md since your last sitting', onclick: toggleChanges }, 'dream changes')
            : null,
          h(
            'button',
            { class: 'button', disabled: Boolean(talks.current.dreaming), title: 'Distill the unsettled sittings into settled.md: brain dream', onclick: dreamConversation },
            talks.current.dreaming === 'settle' ? 'dreaming' : 'dream',
          ),
          h(
            'button',
            { class: 'button', disabled: Boolean(talks.current.dreaming), title: 'Settle what is unsettled, then embed settled.md into the graph, each write waiting for your yes: brain dream --deep', onclick: deepDreamConversation },
            talks.current.dreaming === 'deep' ? 'dreaming into brain' : 'dream into brain',
          ),
          h('button', { class: 'button', title: "Commit this conversation's folder, TEMP excluded, in its scope's repo", onclick: commitConversation }, 'commit conversation'),
        ),
      ),
      titleView(row),
    ),
    talks.current.showChanges ? dreamChanges(talks.current.changes) : h(
      'div',
      { class: 'talk-panes' },
      h(
        'section',
        { class: 'pane trail', 'aria-label': 'Trail' },
        h(
          'div',
          { class: 'trail-scroll', id: 'trail-scroll', onscroll: onTrailScroll },
          h('h2', { class: 'pane-label' }, `Trail, sitting ${sitting.number}`),
          draftColumn(row),
          view.phases.length
            ? view.phases.map((phase, index) =>
                trailPhase(row, sitting, phase, view.comments.filter((comment) => (comment.phase || comment.in) === phase.number), view.working && index === view.phases.length - 1 ? view : null),
              )
            : h('p', { class: 'pane-empty' }, 'Nothing is said in this sitting yet. Write below, and send with Ctrl+Enter.'),
          view.problem ? h('p', { class: 'talk-problem', role: 'alert' }, view.problem) : null,
        ),
        talkInput(row, view.comments.length > 0),
      ),
      h(
        'section',
        { class: 'pane settled', id: 'settled', 'aria-label': 'Settled' },
        h('h2', { class: 'pane-label' }, 'Settled'),
        view.settled.trim()
          ? markdownView(view.settled)
          : h('p', { class: 'pane-empty' }, 'Nothing is settled yet. A dream writes here what the sittings agreed, and you can edit settled.md by hand.'),
      ),
    ),
  );
}

function titleView(row) {
  if (!talks.current.renaming) {
    return h('h1', { class: 'pane-title' }, h('button', { class: 'link heading rename', title: 'Rename', onclick: startRename }, row.title));
  }
  return h(
    'div',
    { class: 'talk-rename' },
    h('input', { id: 'talk-rename', class: 'field bare heading', type: 'text', value: row.title, maxlength: '120', 'aria-label': 'Title', spellcheck: 'false', onkeydown: onRenameKey }),
    h('span', { class: 'talk-rename-keys' }, 'Rename ', keys('Enter'), '   Keep ', keys('Esc')),
  );
}

function startRename() {
  if (!talks.current?.view) return;
  talks.current.renaming = true;
  renderMain();
}

async function onRenameKey(event) {
  if (event.key === 'Escape') {
    event.preventDefault();
    event.stopPropagation();
    talks.current.renaming = false;
    renderMain();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    event.stopPropagation();
    const title = event.target.value.trim();
    if (!title) {
      toast('A conversation needs a title.');
      return;
    }
    talks.current.renaming = false;
    await change('rename', { ref: talks.current.ref, title });
  }
}

// A phase as the trail draws it: numbered, the agent's with its model and effort, and its comments in the margin.
function trailPhase(row, sitting, phase, comments, live) {
  return phaseView(phase, live, {
    numbered: true,
    margin: comments.length ? h('div', { class: 'margin', 'aria-label': `Comments on phase ${phase.number}` }, comments.map((comment) => commentView(row, sitting, comment))) : null,
  });
}

// One phase, as the trail and the chat draw it. The agent's answer while it streams carries live, the view: one quiet
// line says what the agent does now. A numbered phase carries its number, its id and the agent's model and effort; a
// chat's says those once, in its head.
function phaseView(phase, live, { numbered = false, margin = null } = {}) {
  const agent = phase.who === 'agent';
  return h(
    'article',
    { class: `phase ${agent ? 'agent' : 'user'}${live ? ' live' : ''}`, id: numbered ? `phase-${phase.number}` : null },
    h(
      'header',
      { class: 'phase-head' },
      numbered ? h('span', { class: 'phase-no' }, String(phase.number)) : null,
      h('span', { class: 'phase-who' }, agent ? 'agent' : 'you'),
      h('time', { datetime: phase.at }, clock(phase.at)),
      numbered && agent ? h('span', {}, `${modelLabel(phase.model)}${phase.effort ? ` ${phase.effort}` : ''}`) : null,
    ),
    live ? h('p', { class: 'activity', 'aria-live': 'polite' }, live.activity || (phase.text ? 'Writing' : 'Thinking')) : null,
    h('div', { class: 'phase-body' }, markdownView(phase.text)),
    margin,
  );
}

// ---- The comment bar: each comment folded to a marker beside the passage it quotes, opened on a click or a key ----
//
// A comment anchors to a phase and the text it quotes, never to an offset. Its marker sits level with that text, and a
// comment whose text the phase no longer holds sits at the phase's top, marked orphaned. A comment on settled sits in
// the margin of the phase that carried it. A comment written and not yet sent is a draft: it is underlined where it
// points, and sits in the comment column beside the sent ones until it goes out with the text.

function commentKey(row, sitting, comment) {
  return `${row.ref}#${sitting.number}#p${comment.in}c${comment.number}`;
}

function commentView(row, sitting, comment) {
  const key = commentKey(row, sitting, comment);
  const open = talks.openComment === key;
  return h(
    'div',
    { class: 'comment', 'data-quote': comment.quote, 'data-target': String(comment.phase), 'data-open': open ? 'true' : null },
    h(
      'button',
      {
        class: 'icon bubble comment-mark',
        'aria-expanded': String(open),
        'aria-label': `Comment ${comment.number} on “${comment.quote}”`,
        title: open ? 'Fold the comment' : `“${comment.quote}”`,
        onclick: () => toggleComment(key),
      },
      String(comment.number),
    ),
    open
      ? h(
          'aside',
          { class: 'comment-card', 'aria-label': `Comment ${comment.number}` },
          h('p', { class: 'comment-quote' }, h('span', { class: 'comment-no' }, String(comment.number)), comment.phase ? null : h('span', { class: 'comment-on' }, 'settled'), `“${comment.quote}”`),
          h('p', { class: 'comment-orphan' }, comment.phase ? 'Its text is no longer in this phase.' : 'Its text is no longer in settled.'),
          h('p', { class: 'comment-text' }, comment.text),
          h('p', { class: 'comment-keys' }, keys('['), keys(']'), ' the others   ', keys('Esc'), ' fold'),
        )
      : null,
  );
}

function toggleComment(key) {
  talks.openComment = talks.openComment === key ? null : key;
  renderMain();
}

// Opens the next or the previous comment of the sitting in view, in the order they were made.
function stepComment(delta) {
  const view = talks.current?.view;
  if (!view?.comments.length) return;
  const keysInOrder = view.comments.map((comment) => commentKey(view.row, view.row.current, comment));
  const at = keysInOrder.indexOf(talks.openComment);
  talks.openComment = keysInOrder[at < 0 ? (delta > 0 ? 0 : keysInOrder.length - 1) : (at + delta + keysInOrder.length) % keysInOrder.length];
  renderMain();
  document.querySelector('.comment[data-open] .comment-mark')?.focus({ preventScroll: true });
  document.querySelector('.comment[data-open]')?.scrollIntoView({ block: 'nearest' });
}

// The quoted text under a root as a range, across the elements markdown made of it; null when the root lacks it. Runs of
// white space count as one, since a quote is kept as one line.
function quoteRange(body, quote) {
  const walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT);
  const text = [];
  const origin = [];
  let space = true;
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.parentElement.closest('figure')) continue;
    for (let i = 0; i < node.data.length; i++) {
      if (/\s/.test(node.data[i])) {
        if (space) continue;
        space = true;
        text.push(' ');
      } else {
        space = false;
        text.push(node.data[i]);
      }
      origin.push([node, i]);
    }
  }
  const at = text.join('').indexOf(quote.replace(/\s+/g, ' ').trim());
  if (at < 0 || !quote.trim()) return null;
  const range = document.createRange();
  range.setStart(...origin[at]);
  const [last, offset] = origin[at + quote.replace(/\s+/g, ' ').trim().length - 1];
  range.setEnd(last, offset + 1);
  return range;
}

// The prose a comment points into: its phase's body, or settled.
function targetBody(root, target) {
  return target === 0 ? root.querySelector('#settled .prose') : root.querySelector(`#phase-${target} .phase-body`);
}

// Sets each marker level with the text it quotes, one under the other where two would meet, and marks the open
// comment's text in the trail.
function placeComments(root) {
  const marked = [];
  for (const margin of root.querySelectorAll('.margin')) {
    const phase = margin.closest('.phase');
    const top = phase.getBoundingClientRect().top;
    let below = 0;
    for (const comment of margin.querySelectorAll('.comment')) {
      const target = Number(comment.dataset.target);
      const body = target === 0 ? targetBody(root, 0) : phase.querySelector('.phase-body');
      const range = body ? quoteRange(body, comment.dataset.quote) : null;
      comment.classList.toggle('orphan', !range);
      comment.querySelector('.comment-mark').classList.toggle('orphan', !range);
      const at = Math.max(below, range && target !== 0 ? range.getBoundingClientRect().top - top : 0);
      comment.style.top = `${at}px`;
      below = at + comment.offsetHeight + 6;
      if (comment.dataset.open && range) {
        marked.push(range);
        // A card that floats over the trail hangs under the quoted text, so the passage stays in view above it. In a wide
        // bar the card sits level with the passage instead, and ignores this.
        const quoted = range.getBoundingClientRect();
        comment.style.setProperty('--below', `${quoted.bottom - quoted.top + 6}px`);
      }
    }
  }
  const view = talks.current?.view;
  const drafts = (view && talks.commentDrafts[view.row.ref]) || [];
  const underlined = drafts.map((draft) => {
    const body = targetBody(root, draft.phase);
    return body ? quoteRange(body, draft.quote) : null;
  });
  document.querySelectorAll('.draft').forEach((item, index) => item.classList.toggle('orphan', !underlined[index]));
  if (globalThis.CSS?.highlights) {
    if (marked.length) CSS.highlights.set('comment', new Highlight(...marked));
    else CSS.highlights.delete('comment');
    if (underlined.some(Boolean)) CSS.highlights.set('draft', new Highlight(...underlined.filter(Boolean)));
    else CSS.highlights.delete('draft');
  }
}

function clock(iso) {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// The user scrolled the trail. Scrolling up from where the window left it lets go of the answer; coming back to the
// bottom follows it again.
function onTrailScroll(event) {
  const talk = talks.current;
  if (!talk) return;
  const trail = event.target;
  if (trail.scrollHeight - trail.scrollTop - trail.clientHeight <= BOTTOM_PX) {
    talk.pinned = true;
    glideDown(talk);
  } else if (trail.scrollTop < (talk.expected ?? 0) - 2) talk.pinned = false;
  talk.expected = trail.scrollTop;
}

// Moves the trail down to the end of the answer a share of the way each frame, for as long as it follows the answer. A
// user who asked for less motion gets the end at once.
function glideDown(talk) {
  if (talk.gliding) return;
  talk.gliding = true;
  const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const step = () => {
    const trail = $('trail-scroll');
    const gap = trail ? trail.scrollHeight - trail.clientHeight - trail.scrollTop : 0;
    if (talks.current !== talk || !talk.pinned || gap <= 1) {
      talk.gliding = false;
      return;
    }
    trail.scrollTop += still ? gap : Math.max(1, Math.ceil(gap * GLIDE));
    talk.expected = trail.scrollTop;
    requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
}

// Follows the sitting while the agent answers: each ask waits in brain serve until the sitting changes past the version
// drawn, then the window draws it again. It stops once the answer is in, or when the user leaves the conversation.
async function follow(talk) {
  if (talk.following) return;
  talk.following = true;
  try {
    while (talks.current === talk && talk.view?.working) {
      const { ok, body } = await call(`/api/conversation?ref=${encodeURIComponent(talk.ref)}&since=${talk.view.version}`);
      if (talks.current !== talk) return;
      if (!ok) {
        toast(body.error);
        return;
      }
      if (body.row.current.number !== talk.view.row.current.number) return;
      talk.view = body;
      if (inConversations()) renderMain();
      // The answer streams in many small pieces; drawing a few times a second is enough to read it as it comes.
      await new Promise((done) => setTimeout(done, FOLLOW_MS));
    }
  } finally {
    talk.following = false;
  }
}

function talkInput(row, commented) {
  const drafts = talks.commentDrafts[row.ref] || [];
  return h(
    'div',
    { class: 'talk-input' },
    h('textarea', {
      id: 'talk-input',
      class: 'field writing',
      placeholder: drafts.length ? 'Write to the agent, with the comments beside' : 'Write to the agent',
      'aria-label': 'Write to the agent',
      oninput: (event) => {
        talks.drafts[row.ref] = event.target.value;
      },
    }),
    h(
      'div',
      { class: 'talk-input-foot' },
      h('span', {}, 'Send ', keys('Ctrl', 'Enter')),
      h('span', {}, 'Comment on the selection ', keys('Alt', 'C')),
      h('span', {}, 'New trail ', keys('Alt', 'T')),
      commented ? h('span', {}, 'Comments ', keys('['), keys(']')) : null,
      h('button', { class: 'button send', disabled: talks.current.busy || talks.current.view.working, onclick: sendTalk }, 'Send'),
    ),
  );
}

// ---- Writing a comment: select text, Alt+C, write, Ctrl+Enter keeps it as a draft until it goes out with a phase ----

// The comments not yet sent and the one being written, in the comment column at the trail's right, which stays in view
// as the trail scrolls. Drafts on settled sit here too, since settled has no margin of its own.
function draftColumn(row) {
  const drafts = talks.commentDrafts[row.ref] || [];
  const writing = talks.composing?.ref === row.ref;
  if (!drafts.length && !writing) return null;
  return h('div', { class: 'draft-col' }, h('div', { class: 'draft-col-in', 'aria-label': 'Comments to send with the next phase' }, h('p', { class: 'draft-label' }, 'To send'), draftList(drafts), writing ? composer() : null));
}

function draftList(drafts) {
  return h(
    'ol',
    { class: 'drafts' },
    drafts.map((draft, index) =>
      h(
        'li',
        { class: 'draft' },
        h('span', { class: 'comment-no' }, String(index + 1)),
        h(
          'div',
          {},
          h('p', { class: 'comment-quote' }, draft.phase ? `On phase ${draft.phase}: ` : 'On settled: ', `“${draft.quote}”`),
          h('p', { class: 'draft-orphan' }, 'Its text is no longer there.'),
          h('p', { class: 'comment-text' }, draft.text),
        ),
        h('button', { class: 'link subtle draft-drop', title: 'Remove this comment', 'aria-label': `Remove comment ${index + 1}`, onclick: () => dropDraft(index) }, 'Remove'),
      ),
    ),
  );
}

function composer() {
  const on = talks.composing;
  return h(
    'div',
    { class: 'composer' },
    h('p', { class: 'comment-quote' }, on.phase ? `On phase ${on.phase}: ` : 'On settled: ', `“${on.quote}”`),
    h('textarea', {
      id: 'comment-input',
      class: 'field serif',
      placeholder: 'Your comment on this text',
      'aria-label': 'Your comment on the selected text',
      oninput: (event) => {
        on.text = event.target.value;
      },
    }),
    h('p', { class: 'comment-keys' }, 'Keep as a draft ', keys('Ctrl', 'Enter'), '   Drop ', keys('Esc')),
  );
}

// The text selected in the trail or in settled, as { phase, quote }; null when nothing there is selected.
function selectedText() {
  const selection = getSelection();
  if (!selection || selection.isCollapsed || !selection.rangeCount) return null;
  const range = selection.getRangeAt(0);
  const from = (node) => (node.nodeType === Node.TEXT_NODE ? node.parentElement : node)?.closest('.phase-body, #settled .prose');
  const body = from(range.startContainer);
  if (!body || body !== from(range.endContainer)) return null;
  const quote = selection.toString().replace(/\s+/g, ' ').trim();
  if (!quote) return null;
  return { phase: body.closest('.phase') ? Number(body.closest('.phase').id.replace('phase-', '')) : 0, quote };
}

// A small button beside the selected text, for a mouse; the key does the same.
function onSelectionChange() {
  const found = selectedText();
  const pop = $('comment-pop') || document.body.appendChild(h('button', { id: 'comment-pop', class: 'button float', hidden: true, onmousedown: (event) => event.preventDefault(), onclick: composeComment }));
  if (found && inConversations() && talks.current?.view) {
    talks.selected = found;
    const box = getSelection().getRangeAt(0).getBoundingClientRect();
    pop.replaceChildren('Comment ', keys('Alt', 'C'));
    pop.hidden = false;
    pop.style.top = `${Math.max(4, box.top - pop.offsetHeight - 6)}px`;
    pop.style.left = `${Math.max(4, Math.min(innerWidth - pop.offsetWidth - 4, box.left))}px`;
    return;
  }
  pop.hidden = true;
  if (getSelection().isCollapsed && !(document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement)) talks.selected = null;
}

function composeComment() {
  const talk = talks.current;
  if (!inConversations() || !talk?.view || state.view !== 'review') {
    toast('Alt+C comments on selected text inside a conversation.');
    return;
  }
  const found = selectedText() || talks.selected;
  if (!found) {
    toast('Select text in the trail or in settled first.');
    return;
  }
  talks.selected = null;
  talks.composing = { ref: talk.ref, ...found, text: '' };
  getSelection().removeAllRanges();
  $('comment-pop').hidden = true;
  talks.composeFocus = true;
  renderMain();
}

function keepDraft() {
  const on = talks.composing;
  if (!on.text.trim()) {
    toast('Write the comment first, or drop it with Esc.');
    return;
  }
  (talks.commentDrafts[on.ref] ||= []).push({ phase: on.phase, quote: on.quote, text: on.text.trim() });
  talks.composing = null;
  talks.focus = true;
  renderMain();
}

function dropComposer() {
  talks.composing = null;
  talks.focus = true;
  renderMain();
}

function dropDraft(index) {
  const drafts = talks.commentDrafts[talks.current.ref];
  drafts.splice(index, 1);
  if (!drafts.length) delete talks.commentDrafts[talks.current.ref];
  renderMain();
}

async function sendTalk() {
  const talk = talks.current;
  if (!talk?.view || talk.busy) return;
  if (talk.view.working) {
    toast('The agent is still answering.');
    return;
  }
  const text = (talks.drafts[talk.ref] || '').trim();
  const comments = talks.commentDrafts[talk.ref] || [];
  if (!text && !comments.length) {
    toast('Write something first.');
    return;
  }
  talk.busy = true;
  try {
    const { ok, body } = await call('/api/conversation/say', { method: 'POST', body: JSON.stringify({ ref: talk.ref, text, comments: comments.map(({ phase, quote, text: said }) => ({ target: phase, quote, text: said })) }) });
    if (!ok) toast(body.error);
    else {
      delete talks.drafts[talk.ref];
      delete talks.commentDrafts[talk.ref];
      talk.pinned = true;
      if (talks.current === talk) talk.view = body;
    }
  } finally {
    talk.busy = false;
    talks.focus = true;
    if (inConversations()) renderMain();
  }
  follow(talk);
}

async function commitConversation() {
  if (!talks.current?.view) return;
  if (await change('commit', { ref: talks.current.ref })) toast('Conversation committed.');
}

// A change brain serve makes through `brain conversation`: start, sitting, rename or commit. True when it went through.
async function change(what, request) {
  const { ok, body } = await call(`/api/conversation/${what}`, { method: 'POST', body: JSON.stringify(request) });
  if (!ok) {
    toast(body.error);
    renderMain();
    return false;
  }
  showConversation(body);
  return true;
}

// ---- The pickers: a place for a new conversation, a conversation to open, and a sitting's model and effort ----

function openPicker(mode, more = {}) {
  closePalette();
  talks.picker = { mode, query: '', cursor: 0, row: 0, ...firstSitting(more.chat), ...more };
  pickerShade().hidden = false;
  renderPicker();
}

function closePicker() {
  talks.picker = null;
  pickerShade().hidden = true;
  if (inConversations() && talks.current?.view) $('talk-input')?.focus();
  else $('main').focus();
}

// The pickers' shade and box, made once, the first time a picker opens, over the window like the command palette.
function pickerShade() {
  let shade = $('picker');
  if (!shade) {
    shade = h(
      'div',
      { class: 'palette picker', id: 'picker', hidden: true },
      h('div', { class: 'palette-box picker-box', id: 'picker-box', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Conversations', tabindex: '-1' }),
    );
    shade.addEventListener('mousedown', (event) => {
      if (event.target === shade) closePicker();
    });
    document.body.append(shade);
  }
  return shade;
}

function renderPicker() {
  const picker = talks.picker;
  const box = $('picker-box');
  if (picker.mode === 'sitting') {
    fill(box, ...sittingPicker(picker));
    box.focus();
    return;
  }
  const input = h('input', {
    id: 'picker-input',
    class: 'field bare command',
    type: 'text',
    autocomplete: 'off',
    spellcheck: 'false',
    placeholder: picker.mode === 'place' ? 'Personal, global or a project' : 'Type to find one',
    'aria-label': picker.mode === 'place' ? 'Where the conversation lives' : 'Which conversation',
    oninput: (event) => {
      picker.query = event.target.value;
      picker.cursor = 0;
      renderPickerList();
    },
  });
  fill(box,
    h('p', { class: 'picker-head' }, picker.mode === 'place' ? 'New conversation' : 'Open a conversation', h('span', { class: 'picker-head-keys' }, keys('Alt', picker.mode === 'place' ? 'N' : 'O'))),
    input,
    h('ol', { class: 'palette-list', id: 'picker-list', role: 'listbox' }),
    h('div', { class: 'picker-foot' }, h('span', {}, keys('↑'), keys('↓'), ' move'), h('span', {}, keys('Enter'), picker.mode === 'place' ? ' pick' : ' open'), h('span', {}, keys('Esc'), ' close')),
  );
  input.value = picker.query;
  renderPickerList();
  input.focus();
}

function placeItems() {
  return talks.conversations.places
    .filter((place) => matches({ name: place.name, group: PLACE_SAYS[place.kind] }, talks.picker.query))
    .map((place) => ({
      disabled: Boolean(place.problem),
      draw: () => [
        scopeTag(place.origin, place.name),
        h('span', { class: 'palette-name' }, PLACE_SAYS[place.kind]),
        place.problem ? h('span', { class: 'palette-group' }, place.problem) : null,
      ],
      run: () => (place.problem ? toast(place.problem) : openPicker('sitting', { place: place.name, back: 'place' })),
    }));
}

function openItems() {
  return talks.conversations.list
    .filter((row) => matches({ name: row.title, group: `${row.scope} ${row.ref}` }, talks.picker.query))
    .map((row) => ({
      draw: () => [
        scopeTag(row.origin, row.scope),
        h('span', { class: 'palette-name' }, row.title),
        h('span', { class: 'palette-group' }, `sitting ${row.sittings}, ${ago(row.current.started)}`),
      ],
      run: () => {
        closePicker();
        openConversation(row.ref);
      },
    }));
}

function pickerItems() {
  return talks.picker.mode === 'place' ? placeItems() : openItems();
}

function renderPickerList() {
  const picker = talks.picker;
  if (!picker || picker.mode === 'sitting') return;
  const items = pickerItems();
  picker.cursor = Math.min(picker.cursor, Math.max(0, items.length - 1));
  const none = picker.mode === 'place' ? 'No scope or project matches.' : talks.conversations.list.length ? 'No conversation matches.' : 'No conversation yet. Alt+N starts one.';
  fill($('picker-list'),
    ...(items.length
      ? items.map((item, index) =>
          h(
            'li',
            {
              class: 'row option palette-item picker-item',
              role: 'option',
              'aria-selected': String(index === picker.cursor),
              'aria-disabled': item.disabled ? 'true' : null,
              onmousedown: (event) => {
                event.preventDefault();
                item.run();
              },
            },
            item.draw(),
          ),
        )
      : [h('li', { class: 'palette-none' }, none)]),
  );
  $('picker-list').querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
}

function sittingPicker(picker) {
  const talk = talks.current?.view;
  const head = picker.chat ? `Chat on ${picker.chat.place}` : picker.place ? `New conversation in ${picker.place}` : `New trail in ${talk.row.title}`;
  const sub = picker.chat
    ? 'The open agent runs on this model and effort, and nothing of the chat is kept once you close it.'
    : picker.place
      ? 'Its first sitting runs on this model and effort.'
      : `Ends sitting ${talk.row.current.number} and starts sitting ${talk.row.current.number + 1}. A sitting keeps its model and effort.`;
  const row = (index, label, options, value, set) =>
    h(
      'div',
      { class: 'field-row picker-row', 'aria-current': index === picker.row ? 'true' : null },
      h('span', { class: 'picker-label' }, label),
      h(
        'span',
        { class: 'segmented', role: 'radiogroup', 'aria-label': label },
        options.map(([id, text]) =>
          h('button', { role: 'radio', 'aria-checked': String(id === value), tabindex: '-1', onclick: () => set(id) }, text),
        ),
      ),
    );
  return [
    h('p', { class: 'picker-head' }, head, h('span', { class: 'picker-head-keys' }, keys('Alt', picker.chat ? 'A' : picker.place ? 'N' : 'T'))),
    h('p', { class: 'picker-sub' }, sub),
    row(0, 'Model', MODELS, picker.model, (id) => setSitting('model', id)),
    row(1, 'Effort', EFFORTS.map((effort) => [effort, effort]), picker.effort, (id) => setSitting('effort', id)),
    h(
      'div',
      { class: 'picker-foot' },
      h('span', {}, keys('↑'), keys('↓'), ' model or effort'),
      h('span', {}, keys('←'), keys('→'), ' pick'),
      h('span', {}, keys('Enter'), ' start'),
      h('span', {}, keys('Esc'), picker.back ? ' back' : ' close'),
    ),
  ];
}

function setSitting(what, id) {
  talks.picker[what] = id;
  talks.picker.row = what === 'model' ? 0 : 1;
  renderPicker();
}

async function startSitting() {
  const picker = talks.picker;
  closePicker();
  if (picker.chat) await startChat(picker.chat, picker.model, picker.effort);
  else if (picker.place) await change('start', { scope: picker.place, model: picker.model, effort: picker.effort });
  else await change('sitting', { ref: talks.current.ref, model: picker.model, effort: picker.effort });
}

function onPickerKey(event) {
  const picker = talks.picker;
  if (!picker || onAltKey(event)) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    if (picker.back) openPicker(picker.back, { query: '' });
    else closePicker();
    return;
  }
  if (picker.mode === 'sitting') {
    onSittingKey(event, picker);
    return;
  }
  const items = pickerItems();
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    if (!items.length) return;
    picker.cursor = (picker.cursor + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    renderPickerList();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    items[picker.cursor]?.run();
  }
}

function onSittingKey(event, picker) {
  const lists = [MODELS.map(([id]) => id), EFFORTS];
  const what = picker.row === 0 ? 'model' : 'effort';
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Tab') {
    event.preventDefault();
    picker.row = 1 - picker.row;
    renderPicker();
  } else if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
    event.preventDefault();
    const list = lists[picker.row];
    const at = list.indexOf(picker[what]);
    picker[what] = list[Math.max(0, Math.min(list.length - 1, at + (event.key === 'ArrowRight' ? 1 : -1)))];
    renderPicker();
  } else if (event.key === 'Enter') {
    event.preventDefault();
    startSitting();
  }
}

// ---- Keys ----

// The keys of the design's table: Alt+N and Alt+O anywhere in the window, and Alt+T and Alt+C inside a conversation,
// which say so when pressed outside one; true when the key was one of them.
function onAltKey(event) {
  if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
  const actions = { KeyN: newConversation, KeyO: () => openPicker('open'), KeyT: newTrail, KeyC: composeComment };
  if (!actions[event.code]) return false;
  event.preventDefault();
  event.stopPropagation();
  actions[event.code]();
  return true;
}

function newConversation() {
  openPicker('place');
  loadConversations();
}

function newTrail() {
  if (!inConversations() || !talks.current?.view || state.view !== 'review') {
    toast('Alt+T starts a new trail inside a conversation. Open one with Alt+O.');
    return;
  }
  openPicker('sitting');
}

// The keys of the conversations inbox; true when the key was one of them.
function onTalkKey(event, key) {
  if (!inConversations() || state.view !== 'review') return false;
  if (key === 'escape' && talks.current?.showChanges) {
    toggleChanges();
    return true;
  }
  if (key === 'escape' && talks.openComment) {
    toggleComment(talks.openComment);
    return true;
  }
  const actions = {
    j: () => stepConversation(1),
    k: () => stepConversation(-1),
    i: () => $('talk-input')?.focus(),
    ']': () => stepComment(1),
    '[': () => stepComment(-1),
  };
  if (!actions[key]) return false;
  event.preventDefault();
  actions[key]();
  return true;
}

// The keys of the box at the bottom: Ctrl+Enter sends, Esc leaves it with the draft kept.
function onTalkInputKey(event, ctrl) {
  if (event.key === 'Escape') {
    event.preventDefault();
    $('main').focus();
    return true;
  }
  if (ctrl && event.key === 'Enter') {
    event.preventDefault();
    sendTalk();
    return true;
  }
  return false;
}

// The keys of the comment being written: Ctrl+Enter keeps it as a draft, Esc drops it.
function onComposerKey(event, ctrl) {
  if (event.key === 'Escape') {
    event.preventDefault();
    dropComposer();
    return true;
  }
  if (ctrl && event.key === 'Enter') {
    event.preventDefault();
    keepDraft();
    return true;
  }
  return false;
}

// Every key of the window comes here first; true when a conversation took it. The Alt keys of the design's table come
// first, an open picker takes every key, and the rest only in the conversations inbox.
function onKeyFirst(event) {
  if (onAltKey(event)) return true;
  if (talks.picker) {
    onPickerKey(event);
    return true;
  }
  const ctrl = event.ctrlKey || event.metaKey;
  if (event.target.id === 'comment-input') return onComposerKey(event, ctrl);
  if (event.target.id === 'talk-input') return onTalkInputKey(event, ctrl);
  const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
  if (typing || ctrl || event.altKey || !$('palette').hidden) return false;
  return onTalkKey(event, event.key.toLowerCase());
}

function conversationCommands() {
  const list = [
    { name: 'New conversation', keys: ['Alt', 'N'], run: newConversation },
    { name: 'Open a conversation', keys: ['Alt', 'O'], run: () => openPicker('open') },
  ];
  if (inConversations() && talks.current?.view && state.view === 'review') {
    list.push(
      { name: 'New trail: end this sitting and start the next', keys: ['Alt', 'T'], run: newTrail },
      { name: 'Rename this conversation', keys: [], run: startRename },
      { name: 'Dream: distill the unsettled sittings into settled', keys: [], run: dreamConversation },
      { name: 'Dream into brain: embed settled into the graph, each write waiting for your yes', keys: [], run: deepDreamConversation },
      { name: 'Show what dreams changed since your last sitting', keys: [], run: toggleChanges },
      { name: 'Write to the agent', keys: ['i'], run: () => $('talk-input')?.focus() },
      { name: 'Comment on the selected text', keys: ['Alt', 'C'], run: composeComment },
    );
    const drafts = talks.commentDrafts[talks.current.ref] || [];
    if (drafts.length) {
      list.push(
        { name: 'Remove the last draft comment', keys: [], run: () => dropDraft(drafts.length - 1) },
        { name: 'Remove every draft comment', keys: [], run: () => {
            delete talks.commentDrafts[talks.current.ref];
            renderMain();
          } },
      );
    }
    if (!layout($('main')).wide) {
      list.push(
        talks.layout === 'comments'
          ? { name: 'Larger settled: give the room to settled', keys: [], run: () => setTalkLayout('settled') }
          : { name: 'Larger comments: give the room to the comment bar', keys: [], run: () => setTalkLayout('comments') },
      );
    }
    if (talks.current.view.comments.length) {
      list.push({ name: 'Open the next comment', keys: [']'], run: () => stepComment(1) }, { name: 'Open the previous comment', keys: ['['], run: () => stepComment(-1) });
    }
  }
  if (inConversations() && visibleConversations().length > 1) {
    list.push({ name: 'Next conversation', keys: ['j'], run: () => stepConversation(1) }, { name: 'Previous conversation', keys: ['k'], run: () => stepConversation(-1) });
  }
  return list;
}

document.addEventListener('selectionchange', onSelectionChange);

// A resize can cross the width where the comment bar holds whole cards, so the conversation lays out again.
window.addEventListener('resize', () => inConversations() && renderMain());

// A drawn diagram moves the text under it, so the comments find their places again once it is drawn.
$('main').addEventListener('diagrams-drawn', () => inConversations() && placeComments($('main')));

registerInbox({
  name: CONVERSATIONS,
  label: 'Conversations',
  ownSide: true,
  // A conversation has its own agent, so Alt+A summons no chat here.
  noChat: true,
  count: (graph) => visibleConversations(graph).length,
  start: () => {
    loadTalkLayout();
    loadConversations();
  },
  list: conversationList,
  render: renderTalkMain,
  keys: onKeyFirst,
  commands: conversationCommands,
});
