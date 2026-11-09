// The chats: the open agent, summoned into the window on whichever tab is in view, to get knowledge out of the graph
// now. Each tab of the strip holds at most one chat, and several tabs' chats live at once: switching tabs ends none, so
// the user can switch to node review to answer a write a chat staged and come back. A chat starts with the context of
// the page it is summoned on, what was in view at that moment and never what the user selects afterwards; the conversations
// tab holds no chat, for conversations have their own agent. Alt+A summons the chat of the tab in view, after the model and effort picker conversations use,
// on the model and effort the settings name unless picked. It is the same agent a sitting runs: a write it stages waits for the user's yes
// in node review, like any other.
//
// A chat keeps nothing. brain serve holds what was said only while the chat is open, and the window keeps only each
// chat's id and draft. Closing a chat stops its agent, and it is gone: there is no list of past chats. While an agent it
// sent or a process it started still runs, brain serve refuses the close and names what would stop, and the panel asks
// the user first.
//
// brain serve's registry, in its memory only, holds every agent it runs by tab: each tab's chat, each conversation's
// live sitting and each running dream. The window asks it every few seconds, marks each tab that has a live agent in
// the strip, as an inbox shows its count, and shows a chat a newer window finds there.
//
// It meets the rest of the window at a few seams: app.js hands chatKey() every key, draws agentMark() on each tab of the
// strip, adds chatCommands() to the palette, calls loadAgents() once the window has its session and renderChat() each
// time a tab is shown, and the picker in conversations.js starts a chat through startChat(). Everything else it keeps in
// chat, its own state.
'use strict';

const AGENTS_MS = 3000;
const chat = {
  // The registry as brain serve last said it: { tab, kind, id, name, model, effort, started, running } for each agent.
  agents: [],
  // Each tab's chat, by the tab's name: { view, draft, busy, following, scrolled, atEnd, stopping }. view is what brain
  // serve says of it: { id, tab, place, model, effort, phases, working, activity, problem, version }. stopping is what
  // closing it would stop, while the panel asks; scrolled and atEnd are where the user scrolled it to and whether that
  // is its end, which it then follows as the answer grows.
  tabs: new Map(),
};

function chatHere() {
  return chat.tabs.get(state.inbox) || null;
}

function heldChat(view) {
  return { view, draft: '', busy: false, following: false, scrolled: 0, atEnd: true, stopping: null };
}

// Summons the chat of the tab in view: the picker first, or the input of the chat it holds. The context is read before
// the picker opens, so it is what was in view when the user pressed the key. The conversations tab has its own agent,
// so it holds no chat.
async function summonChat() {
  if (shownInbox().noChat) return;
  if (chatHere()) {
    renderChat();
    $('chat-input')?.focus();
    return;
  }
  const where = { tab: state.inbox, place: shownInbox().label, context: await chatContext() };
  if (chatHere() || state.inbox !== where.tab) return;
  openPicker('sitting', { chat: where });
}

// What the chat starts with, as text for the agent: the node page in view, else what the tab in view says through its
// context(), else nothing. brain serve hands it to the agent as it is.
async function chatContext() {
  return (state.view === 'node' && state.node && !state.node.error ? `The user has this node open.\n\n${nodeText(state.node)}` : await shownInbox().context?.()) || '';
}

// A node as the agent reads it: its title, where it sits, its text, its why and its edges.
function nodeText(node) {
  const edges = node.edges.map((e) => `- ${e.rel} ${e.ref || e.target}${e.title ? `, ${e.title}` : ''}`);
  return [
    `# ${node.title || node.slug}`,
    [node.ref, node.kind, ...node.subjects].filter(Boolean).join(', '),
    node.body,
    node.why ? `## Why\n\n${node.why}` : '',
    edges.length ? `## Edges\n\n${edges.join('\n')}` : '',
  ]
    .filter(Boolean)
    .join('\n\n');
}

async function startChat(where, model, effort) {
  const { ok, body } = await call('/api/chat/start', { method: 'POST', body: JSON.stringify({ tab: where.tab, place: where.place, context: where.context, model, effort }) });
  if (!ok) {
    toast(body.error);
    return;
  }
  chat.tabs.set(body.tab, heldChat(body));
  renderStrip();
  renderChat(true);
}

// Asks brain serve's registry every few seconds while the window is open, and marks the tabs again.
async function loadAgents() {
  while (!state.closed) {
    await readAgents().catch(() => {});
    await new Promise((done) => setTimeout(done, AGENTS_MS));
  }
}

// The registry now. A chat it holds that this window does not, as one a window before this one summoned, shows where it
// was summoned.
async function readAgents() {
  const { ok, body } = await call('/api/agents');
  if (!ok) return;
  const marked = marks();
  chat.agents = body.agents;
  let found = false;
  for (const entry of body.agents.filter((agent) => agent.kind === 'chat')) {
    if (chat.tabs.has(entry.tab)) continue;
    const open = await call(`/api/chat?id=${encodeURIComponent(entry.id)}`);
    if (!open.ok || chat.tabs.has(entry.tab)) continue;
    const one = heldChat(open.body);
    chat.tabs.set(entry.tab, one);
    followChat(one);
    found = true;
  }
  // The strip is drawn again only when a mark changed, so a strip item keeps its focus and its hover.
  if (marks() !== marked) renderStrip();
  if (found) renderChat();
}

// What every tab's mark says, as one string to tell whether the strip needs drawing again.
function marks() {
  return JSON.stringify(registered.map((inbox) => markNames(inbox.name)));
}

async function sendChat() {
  const one = chatHere();
  if (!one || one.busy) return;
  if (one.view.working) {
    toast('The agent is still answering.');
    return;
  }
  const text = one.draft.trim();
  if (!text) {
    toast('Write something first.');
    return;
  }
  one.busy = true;
  try {
    const { ok, body } = await call('/api/chat/say', { method: 'POST', body: JSON.stringify({ id: one.view.id, text }) });
    if (!ok) toast(body.error);
    else {
      one.draft = '';
      one.view = body;
      one.atEnd = true;
    }
  } finally {
    one.busy = false;
    renderChat(true);
  }
  followChat(one);
}

function held(one) {
  return chat.tabs.get(one.view.tab) === one;
}

// Follows a chat while its agent answers, each ask waiting in brain serve until it changed past the version drawn,
// whether its tab is in view or not.
async function followChat(one) {
  if (one.following) return;
  one.following = true;
  try {
    while (held(one) && one.view.working) {
      const asked = one.view;
      const { ok, body } = await call(`/api/chat?id=${encodeURIComponent(asked.id)}&since=${asked.version}`);
      if (!held(one)) return;
      if (!ok) {
        toast(body.error);
        chat.tabs.delete(asked.tab);
        renderStrip();
        renderChat();
        return;
      }
      one.view = body;
      renderChat();
      await new Promise((done) => setTimeout(done, FOLLOW_MS));
    }
  } finally {
    one.following = false;
  }
}

// Closes the chat of the tab in view: its agent stops and nothing of it is left, the draft included. When brain serve
// names what would stop with it, the panel asks first, and stop closes it on the user's word.
async function closeChat(stop = false) {
  const one = chatHere();
  if (!one || one.busy) return;
  one.busy = true;
  try {
    const { ok, status, body } = await call('/api/chat/close', { method: 'POST', body: JSON.stringify({ id: one.view.id, stop }) });
    if (ok) {
      chat.tabs.delete(one.view.tab);
      chat.agents = chat.agents.filter((agent) => agent.id !== one.view.id);
      renderStrip();
      $('main').focus();
    } else if (status === 409 && body.running) one.stopping = body.running;
    else toast(body.error);
  } finally {
    one.busy = false;
    renderChat();
  }
  if (chatHere() === one && one.stopping) $('chat').querySelector('.stop-confirm .button.no')?.focus();
}

function keepChat() {
  const one = chatHere();
  if (!one) return;
  one.stopping = null;
  renderChat(true);
}

// ---- Drawing ----

// What a tab's mark names: each live agent on it, its own chat first.
function markNames(tab) {
  const names = chat.agents.filter((agent) => agent.tab === tab).map((agent) => `${agent.kind}: ${agent.name}`);
  if (chat.tabs.has(tab) && !names.some((name) => name.startsWith('chat: '))) names.unshift('chat');
  return names;
}

// A tab's mark in the strip while an agent of brain serve's lives on it, as an inbox shows its count: its chat, a
// conversation's sitting or a dream.
function agentMark(tab) {
  const names = markNames(tab);
  if (!names.length) return null;
  return h('span', { class: 'strip-live', title: names.join('\n'), 'aria-label': `${names.length} live` }, 'live');
}

// Draws the chat of the tab in view over the main pane's right edge, in place; hidden while that tab holds none. It
// keeps where the user scrolled to, and follows the answer down while they read at its end.
function renderChat(focusInput = false) {
  const el = $('chat');
  const one = chatHere();
  el.hidden = !one;
  if (!one) {
    fill(el);
    return;
  }
  const view = one.view;
  const typing = document.activeElement?.id === 'chat-input' ? [document.activeElement.selectionStart, document.activeElement.selectionEnd] : null;
  fill(el,
    h(
      'header',
      { class: 'pane-head compact' },
      h('h2', { class: 'pane-title' }, 'Chat'),
      h('span', {}, `on ${view.place}`),
      h('span', {}, `${modelLabel(view.model)}, ${view.effort} effort`),
      h('button', { class: 'button chat-close', disabled: one.busy, title: 'Close the chat: its agent stops and nothing of it is kept', onclick: () => closeChat() }, 'Close'),
    ),
    one.stopping ? stoppingView(one.stopping) : null,
    h(
      'div',
      { class: 'chat-scroll', onscroll: (event) => onChatScroll(one, event) },
      view.phases.length
        ? view.phases.map((phase, index) => phaseView(phase, view.working && index === view.phases.length - 1 ? view : null))
        : h('p', { class: 'pane-empty' }, 'Ask about the graph, or have the agent change it: every write waits for your yes in node review. Nothing of this chat is kept once you close it.'),
      view.problem ? h('p', { class: 'talk-problem', role: 'alert' }, view.problem) : null,
    ),
    h(
      'div',
      { class: 'talk-input' },
      h('textarea', {
        id: 'chat-input',
        class: 'field writing',
        placeholder: 'Write to the agent',
        'aria-label': 'Write to the agent',
        oninput: (event) => {
          one.draft = event.target.value;
        },
      }),
      h(
        'div',
        { class: 'talk-input-foot' },
        h('span', {}, 'Send ', keys('Ctrl', 'Enter')),
        h('span', {}, 'Leave ', keys('Esc')),
        h('button', { class: 'button send', disabled: one.busy || view.working, onclick: sendChat }, 'Send'),
      ),
    ),
  );
  const scroll = el.querySelector('.chat-scroll');
  scroll.scrollTop = one.atEnd ? scroll.scrollHeight : one.scrolled;
  const box = el.querySelector('#chat-input');
  box.value = one.draft;
  if (typing) {
    box.focus();
    box.setSelectionRange(typing[0], typing[1]);
  } else if (focusInput && !one.stopping) box.focus();
}

// What closing the chat would stop, named before the user says so: the shared stop confirm.
function stoppingView(running) {
  const shown = { question: 'Closing this chat stops what it started:', work: running, stop: 'Stop them and close', keep: 'Keep the chat' };
  return templateNode(stopConfirmView(shown, { onStop: () => closeChat(true), onKeep: keepChat }));
}

function onChatScroll(one, event) {
  const scroll = event.target;
  one.scrolled = scroll.scrollTop;
  one.atEnd = scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight <= BOTTOM_PX;
}

// ---- Keys ----

// Alt+A summons the chat of the tab in view; in its box Ctrl+Enter sends and Esc leaves it with the draft kept, and
// while the panel asks to stop what runs, Esc keeps the chat. True when it took the key.
function chatKey(event) {
  const ctrl = event.ctrlKey || event.metaKey;
  if (event.altKey && !ctrl && !event.shiftKey && event.code === 'KeyA') {
    if (shownInbox().noChat) return false;
    event.preventDefault();
    summonChat();
    return true;
  }
  const one = chatHere();
  if (!one || !$('chat').contains(event.target)) return false;
  if (event.key === 'Escape' && one.stopping) {
    event.preventDefault();
    keepChat();
    return true;
  }
  if (event.target.id !== 'chat-input') return false;
  if (event.key === 'Escape') {
    event.preventDefault();
    $('main').focus();
    return true;
  }
  if (ctrl && event.key === 'Enter') {
    event.preventDefault();
    sendChat();
    return true;
  }
  return false;
}

function chatCommands() {
  if (shownInbox().noChat) return [];
  if (!chatHere()) return [{ name: 'Chat: summon the open agent on this tab', keys: ['Alt', 'A'], run: summonChat }];
  return [
    { name: 'Write to the chat', keys: ['Alt', 'A'], run: summonChat },
    { name: 'Close the chat', keys: [], run: () => closeChat() },
  ];
}
