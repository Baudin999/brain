// brain code's window: the sessions a developer works with agents in, listed on the side, the one in view beside them
// with the agents it sent, and an agent a session sent opened read only in its place, in the view of its kind or as its
// whole conversation. It is an interface over sessions: each runs in brain code's server, and the window only draws
// what the server says and sends what the user does.
//
// Global state is what the server says and what the user looks at: the sessions, the agents to start one with, the
// session in view, the agents each sent, whether an agent of it is open and which side of it shows, each open agent's
// view, and each conversation's events read so far. A page draws from it and never changes it; a click calls an action
// here, which changes it and draws. The tool calls of the conversation, the second flow, open in the information panel
// on the right, which shows the call the user last clicked and changes on no other act; the settings choose which kinds
// of that flow are drawn and whether the agents panel draws a flow or a list, and the server keeps them. The server says on the window's event stream which session
// changed; the window then reads only the events past those it has. A state change only draws: the conversation scrolls
// to its newest event only where new events arrived, and only when it was at its end, as a terminal does; nothing an
// agent does picks a session or opens a page, and a new agent or round only marks its session. A permission request
// that comes, and a question a session asks, is a new message the same way: it scrolls the conversation in view to it
// only when that was at its end, and it never picks its session or opens the page of open questions; its session's row
// says it asks. The text the session's agent is writing comes with its conversation until it is an event of it, and
// scrolls the same way. Ctrl+C, or the composer's stop, interrupts the agent's turn, as in a terminal; while an agent
// the session sent still runs, which Claude Code stops with the turn, the window names each and asks first, and only
// the user's stop interrupts. Esc never interrupts. It closes what is in front of the user: the information panel, the
// confirm that asks, the file explorer, the image viewer or the folded-out processes. Close in a session's head asks
// first, in a modal that names the session; Keep, Esc or a click outside leaves it running. The processes a session's
// agent started that still run fold out of a chip in its head, over its conversation, and fold away on Esc or a click
// outside; while they are out, the sessions read again each second, so they come and go as they do. Once a newer build
// of brain code is published, the head of the sessions shows the reload button, disabled while a session runs and
// highlighted once none does; a click puts the server on the new build, every session with it, and this window moves
// onto it in place, on the session that was in view. Each action and each read is a record, sent to the server for the
// log of perf/code, on this machine only.
//
// Text selected in the conversation or in the information panel is quoted into the composer of the session in view,
// from the Quote button beside it or with Alt+Q. Under the image viewer, the file explorer or the close confirm there
// is no Quote button, and Alt+Q quotes nothing.
//
// A session's folder has its workspace, the folder's code, which its own server serves and the window shows in a frame.
// The strip down the window's left edge has a tab for the conversation of the session in view, Alt+C, and one for its
// folder's code, Alt+W, and the toggle Alt+V shows the two side by side, which the server keeps. Each gives the keys to
// what it shows: the workspace's keys work while its frame has the focus, and brain code's while the conversation has
// it; the frame hands brain code these three keys. Side by side, the pane with the focus carries the focus line. The
// first time a folder's workspace shows, the server starts it; its frame stays while a session runs in the folder.
// Nothing an agent does switches a tab or opens a file.
//
// Images reach the composer of the session in view by one attach path: dropped on the window, pasted, or attached from
// the file explorer, which Alt+P opens over the window, in the folder last attached from. The explorer reads the disk
// through the server only, and the server remembers a folder once an image was attached from it. A thumbnail in the
// conversation, and an image the explorer previews, open in the image viewer, over the explorer when it is open.
import { html, render, nothing } from '/lit-html.js';
import { imageViewer, imageViewerKey, imageViewerPaint, imageViewerDo, imageViewerView } from '/components/image-viewer.js';
import {
  fileExplorer, fileExplorerColumns, fileExplorerKey, fileExplorerMarked, fileExplorerReveal, fileExplorerShow, fileExplorerThumb,
  fileExplorerUnseen, fileExplorerView,
} from '/components/file-explorer.js';
import { composerAttach, composerQuote } from '/components/composer.js';
import { quoteOfSelection, quotePlace, quoteView } from '/components/quote.js';
import { attachProblem, dataUrlOf, imageOfFile, isImageFile, thumbnailOf } from '/attach.js';
import { sessionListView } from '/components/session-list.js';
import { newSessionView } from '/components/new-session.js';
import { questionsRowView } from '/components/questions-row.js';
import { sessionPage, closeLabel } from '/pages/session.js';
import { stopConfirmView } from '/components/stop-confirm.js';
import { agentPage } from '/pages/agent.js';
import { questionsPage } from '/pages/questions.js';
import { settingsCogView } from '/components/settings-cog.js';
import { historyButtonView, historyView } from '/components/history.js';
import { reloadView } from '/components/reload.js';
import { settingsPage } from '/pages/settings.js';
import { toolPanelView } from '/components/tool-calls.js';
import { viewStripView } from '/components/view-strip.js';
import { workspaceFolderOf, workspaceFrameOf, workspaceFramesPaint, workspacePaneView } from '/components/workspace-pane.js';

const SESSION_KEY = 'code.session';
const RECORDS_SEND_MS = 10000;
const READ_BUDGET_MS = 100;
// How often the sessions read again while a session's processes are folded out, so they show as they come and go.
const FOLD_READ_MS = 1000;
// How near its end, in pixels, a conversation counts as at its end.
const AT_END_PX = 48;
// The keys of the strip, by the key's place, since Alt can change the letter a layout types: the conversation, the
// code, and side by side.
const VIEW_KEYS = { KeyC: 'conversation', KeyW: 'code', KeyV: 'beside' };

const state = {
  closed: null,
  home: '',
  sessions: [],
  agents: [],
  selected: null,
  // The agents each session sent, by the session's id, in the order it sent them.
  sent: new Map(),
  // The agent open in place of the session, { id, name, shows }, by the id of the call that sent it, or null; shows is
  // 'happened', the view of its kind, or 'conversation'.
  agent: null,
  // The view of each open agent, as the server read it, by the id of the call that sent it.
  views: new Map(),
  // Whether the conversation in view is away from its end, which shows its button to the end.
  away: false,
  // How many follows to the end wait for their frame: a conversation about to be followed to its end is not away from it.
  following: 0,
  // Each conversation's events read so far, by its key: a session's id, or the id of the call that sent an agent.
  conversations: new Map(),
  // The page in view: a session's, the page of every open question, or the settings.
  page: 'session',
  // Which kinds of the second flow the conversation draws, how the agents panel draws, and whether the conversation and
  // the code show side by side, as the server keeps them.
  settings: { tools: true, brain: true, agents: 'flow', beside: false },
  // The tab in view while the two are not side by side: 'conversation' or 'code', the workspace of the session's folder.
  view: 'conversation',
  // The workspace of each folder that showed one, by the folder: { url } its frame opens on, or { problem }.
  workspaces: new Map(),
  // Where the keys are: 'conversation', brain code's own, or 'workspace', the frame of the workspace in view.
  focus: 'conversation',
  // The selected text the Quote button offers to put in the composer, { text, top, left }, or null.
  quote: null,
  // The tool call the information panel shows, { key, id }: the key of the conversation it is in and the call's id, or
  // null. Only the user's click sets it.
  panel: null,
  // The permission requests and the questions whose answer is on its way, each as its session's id and its own.
  answering: new Set(),
  // The sessions whose turn the user interrupted, until the server says the turn ended.
  stopping: new Set(),
  // The session whose interrupt waits for the user's word, since it would stop the agents it sent, or null.
  confirming: null,
  // The id of the session whose close waits for the user's word.
  closing: null,
  // The session whose running processes are folded out over its conversation, or null.
  fold: null,
  // The processes whose stop is on its way, each as its session's id and its pid.
  ending: new Set(),
  viewer: null,
  // The history modal over the window, { folder, sessions, open, problem }, or null while it is closed. sessions is the
  // open agent's past sessions in the folder, null until read; open is the one being read, { id, title, conversation }.
  history: null,
  // The file explorer over the window, with why its last read failed, or null while it is closed.
  explorer: null,
  explorerProblem: null,
  problem: null,
  // What a reload onto a newer build waits for, as the server says: null until one is published, 'running' while a
  // session runs, and 'ready' once none does; and whether the user's reload is on its way.
  reload: null,
  reloading: false,
  // The session the window showed before a reload, which this window opens in view, or null.
  opened: null,
};

// How many thumbnails the explorer reads at once.
const THUMB_READS = 4;
let thumbReads = 0;

// The reads under way, by key, and whether another was asked for while one ran.
const reading = new Map();
const unsent = [];

// ---- Records ----

function record(name, args, ms, outcome, budget) {
  unsent.push({ at: Date.now(), name, args, source: 'window', ms: Math.round(ms * 10) / 10, outcome, budget, over: budget !== null && ms > budget });
}

// Times work as the record name; a failure is said through said, the window's problem unless it names another place.
async function timed(name, args, budget, work, said = sayProblem) {
  const started = performance.now();
  let outcome = 'ok';
  try {
    return await work();
  } catch (error) {
    outcome = 'failed';
    said(error.message);
    return null;
  } finally {
    draw();
    record(name, args, performance.now() - started, outcome, budget);
  }
}

function sayProblem(message) {
  state.problem = message;
}

function sayExplorerProblem(message) {
  state.explorerProblem = message;
}

// A user's action in the file explorer: its failure shows in the explorer, the one place he looks while it is open.
function explore(name, args, budget, work) {
  state.explorerProblem = null;
  return timed(name, args, budget, work, sayExplorerProblem);
}

// A user's action: the problem shown goes, since the action may be what answers it, and the action is timed.
function act(name, args, budget, work) {
  state.problem = null;
  return timed(name, args, budget, work);
}

// ---- The server ----

// A call whose refusal throws, with the server's reason; with a body it posts it as JSON.
async function ask(path, sent) {
  let options = {};
  if (sent !== undefined) options = { method: 'POST', body: JSON.stringify(sent) };
  const { ok, body } = await windowCall(path, options);
  if (!ok) throw new Error(body.error);
  return body;
}

async function readSessions() {
  const listed = await ask('/api/sessions');
  state.sessions = listed.sessions;
  state.home = listed.home;
  state.reload = listed.reload ?? null;
  state.opened = listed.opened ?? null;
  for (const session of state.sessions) {
    // An interrupt claude refused shows as the session's problem while its turn still runs: Stop works again.
    if (session.problem) state.stopping.delete(session.id);
    if (session.working) continue;
    state.stopping.delete(session.id);
    // A turn that ended by itself leaves nothing to confirm.
    if (state.confirming === session.id) state.confirming = null;
  }
  // A fold with nothing left in it, or of a session that went, folds away.
  const folded = state.sessions.find((one) => one.id === state.fold);
  if (!folded || folded.running.length === 0) state.fold = null;
  // A folder no session runs in any more has no workspace here.
  const folders = new Set(state.sessions.map((session) => session.cwd));
  for (const folder of state.workspaces.keys()) {
    if (!folders.has(folder)) state.workspaces.delete(folder);
  }
}

function conversationOf(key) {
  if (!state.conversations.has(key)) state.conversations.set(key, { events: [], found: false, generation: null, live: null });
  return state.conversations.get(key);
}

// Runs the read work under key, unless one runs: then it runs once more after that one ended.
async function once(key, work) {
  if (reading.has(key)) {
    reading.set(key, true);
    return;
  }
  reading.set(key, false);
  try {
    await work();
  } finally {
    const again = reading.get(key);
    reading.delete(key);
    if (again) once(key, work);
  }
}

// Reads a conversation's events past those it has, in the reading the server gave it. When the server's reading is a new
// one, its transcript written anew or another file, the server sends every event from the first, and they replace the
// ones the window had. The text the agent is writing now comes with it, until it is one of the events.
function readConversation(key, path) {
  return once(`conversation:${key}`, async () => {
    const conversation = conversationOf(key);
    const generation = encodeURIComponent(conversation.generation || '');
    const read = await ask(`${path}&from=${conversation.events.length}&generation=${generation}`);
    const wasAtEnd = atEnd();
    const live = read.live || null;
    const grew = read.events.length > 0 || live !== conversation.live;
    conversation.found = read.found;
    conversation.live = live;
    if (read.from === 0) conversation.events = [];
    conversation.generation = read.generation;
    if (read.from === conversation.events.length) conversation.events.push(...read.events);
    if (grew && wasAtEnd) scrollWhenDrawn(key);
  });
}

// Reads the agents the session sent.
function readSent(session) {
  return once(`sent:${session}`, async () => {
    const read = await ask(`/api/sent?session=${session}`);
    state.sent.set(session, read.agents);
  });
}

// Reads the view of the agent the session's call tool sent.
function readView(session, tool) {
  return once(`view:${tool}`, async () => {
    const read = await ask(`/api/sent-agent?session=${session}&tool=${encodeURIComponent(tool)}`);
    state.views.set(tool, read);
  });
}

// Reads what is in view: the session's conversation, or the open agent's view and, on its other side, its
// conversation; and the agents the session sent, which both draw.
async function readShown() {
  if (!state.selected) return;
  const session = state.selected;
  const reads = [readSent(session)];
  if (!state.agent) reads.push(readConversation(session, `/api/conversation?session=${session}`));
  if (state.agent) reads.push(readView(session, state.agent.id));
  if (state.agent?.shows === 'conversation') {
    const tool = encodeURIComponent(state.agent.id);
    reads.push(readConversation(state.agent.id, `/api/subagent?session=${session}&tool=${tool}`));
  }
  await Promise.all(reads);
}

// ---- The viewport ----

function scroller() {
  return document.getElementById('conversation');
}

function atEnd() {
  const shown = scroller();
  if (!shown) return true;
  return shown.scrollHeight - shown.scrollTop - shown.clientHeight < AT_END_PX;
}

// Whether the conversation in view is away from its end; a view with no conversation is not.
function awayNow() {
  if (state.following > 0) return false;
  const shown = scroller();
  if (!shown) return false;
  return !atEnd();
}

// The user pressed the button to the end: the one place the viewport moves on purpose.
function toEnd() {
  const shown = scroller();
  if (shown) shown.scrollTop = shown.scrollHeight;
}

// The user scrolled the conversation: the button to its end shows or hides, and nothing else moves.
function scrolled(event) {
  readQuote();
  if (event.target.id !== 'conversation') return;
  if (awayNow() !== state.away) draw();
}

// Scrolls the conversation of key to its newest event once it is drawn, when it is still the one in view.
function scrollWhenDrawn(key) {
  state.following++;
  requestAnimationFrame(() => {
    state.following--;
    if (shownKey() !== key) return;
    const shown = scroller();
    if (shown) shown.scrollTop = shown.scrollHeight;
  });
}

// The key of the conversation in view, or null when the open agent shows the view of its kind.
function shownKey() {
  if (!state.agent) return state.selected;
  if (state.agent.shows === 'conversation') return state.agent.id;
  return null;
}

// ---- Actions ----

async function pick(id) {
  await act('session.pick', id, READ_BUDGET_MS, async () => {
    state.fold = null;
    state.selected = id;
    state.agent = null;
    state.panel = null;
    state.page = 'session';
    await readShown();
    scrollWhenDrawn(id);
    if (viewsShown()?.code) await openWorkspace();
  });
}

// ---- The views of the session in view ----

// The session in view on its own page, or null.
function sessionInView() {
  if (state.page !== 'session') return null;
  return state.sessions.find((one) => one.id === state.selected) ?? null;
}

// What shows of the session in view, { conversation, code }, or null with none in view: both side by side, else the
// tab's.
function viewsShown() {
  if (!sessionInView()) return null;
  if (state.settings.beside) return { conversation: true, code: true };
  return { conversation: state.view === 'conversation', code: state.view === 'code' };
}

// Asks the server for the workspace of the folder of the session in view, which starts it the first time, unless its
// frame is open. A workspace that could not start says why in its pane, and is asked for again the next time it shows.
async function openWorkspace() {
  const folder = sessionInView()?.cwd;
  if (!folder) return;
  if (state.workspaces.get(folder)?.url) return;
  state.workspaces.delete(folder);
  draw();
  try {
    const launched = await ask('/api/workspace', { folder });
    state.workspaces.set(folder, { url: launched.url });
  } catch (error) {
    state.workspaces.set(folder, { problem: error.message });
  }
}

// The user's tab: the conversation, or the folder's code, which starts its workspace the first time; the keys go to
// what it shows.
async function showView(view) {
  if (!sessionInView()) return;
  await act('view.show', view, null, async () => {
    state.view = view;
    if (view === 'code') await openWorkspace();
  });
  giveKeys(view);
}

// The user's toggle: the conversation and the code side by side, or the tab's alone, which the server keeps.
async function toggleBeside() {
  if (!sessionInView()) return;
  const beside = !state.settings.beside;
  await act('view.beside', String(beside), null, async () => {
    state.settings = await ask('/api/settings', { ...state.settings, beside });
    if (beside) await openWorkspace();
  });
}

// Puts the keys in what view shows: the composer of the conversation, or the frame of the workspace.
function giveKeys(view) {
  const host = document.getElementById('app');
  if (view === 'code') {
    workspaceFrameOf(host, sessionInView()?.cwd)?.focus();
    return;
  }
  host.querySelector('.code-composer textarea')?.focus();
}

// Where the keys went: into a workspace's frame, or anywhere of brain code's own. Side by side it draws the focus line.
function focusMoved() {
  let focus = 'conversation';
  if (document.activeElement instanceof HTMLIFrameElement) focus = 'workspace';
  if (focus === state.focus) return;
  state.focus = focus;
  draw();
}

// A key the strip takes: Alt+C, Alt+W or Alt+V with no other modifier, while a session is in view; true when it took it.
function viewKeyed(event) {
  if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return false;
  const view = VIEW_KEYS[event.code];
  if (!view || !sessionInView()) return false;
  if (view === 'beside') toggleBeside();
  if (view !== 'beside') showView(view);
  return true;
}

// A workspace's frame says something: a key of the strip it holds no binding for, or that its workspace ended, which
// then starts again the next time it shows.
function heardFrame(event) {
  const host = document.getElementById('app');
  const folder = workspaceFolderOf(host, event);
  if (!folder) return;
  const said = event.data ?? {};
  if (said.workspace === 'key') viewKeyed(said);
  if (said.workspace !== 'ended') return;
  state.workspaces.set(folder, { problem: 'The workspace’s server stopped. Alt+W starts it again.' });
  draw();
}

async function showSettings() {
  await act('settings.open', '', null, async () => {
    state.page = 'settings';
  });
}

// Opens the history of the open agent's past sessions in the folder in view, and reads the list.
async function showHistory() {
  const folder = folderOfView();
  state.history = { folder, sessions: null, open: null, problem: null };
  draw();
  await act('history.open', folder, null, async () => {
    try {
      const listed = await ask(`/api/history?folder=${encodeURIComponent(folder)}`);
      state.history.sessions = listed.sessions;
    } catch (error) {
      state.history.problem = error.message;
      state.history.sessions = [];
    }
  });
}

// Reads the conversation of a past session into the modal.
async function readPast(past) {
  const history = state.history;
  history.open = { id: past.id, title: past.title, conversation: null };
  history.problem = null;
  draw();
  await act('history.read', past.id, null, async () => {
    try {
      history.open.conversation = await ask(`/api/history-session?folder=${encodeURIComponent(history.folder)}&id=${encodeURIComponent(past.id)}`);
    } catch (error) {
      history.open = null;
      history.problem = error.message;
    }
  });
}

function leavePast() {
  state.history.open = null;
  draw();
}

function closeHistory() {
  state.history = null;
  draw();
}

// The history owns the keys while it is open: Esc leaves the conversation for the list, and from the list closes it.
function historyKeyed(event) {
  event.stopPropagation();
  if (event.key !== 'Escape') return;
  event.preventDefault();
  if (state.history.open) leavePast();
  else closeHistory();
}

// Turns one kind of the second flow on or off, and the server keeps the choice.
async function setKind(id, on) {
  await act('settings.set', `${id} ${on}`, null, async () => {
    state.settings = await ask('/api/settings', { ...state.settings, [id]: on });
  });
}

// Draws the agents panel as a flow or as a list, and the server keeps the choice.
async function setShows(shows) {
  await act('settings.set', `agents ${shows}`, null, async () => {
    state.settings = await ask('/api/settings', { ...state.settings, agents: shows });
  });
}

// The user closed the panel.
async function closePanel() {
  await act('panel.close', '', null, async () => {
    state.panel = null;
  });
}

// The user clicked a tool call: the panel shows it, in the conversation in view.
async function openCall(call) {
  await act('panel.open', call.id, null, async () => {
    state.panel = { key: shownKey(), id: call.id };
  });
}

async function showQuestions() {
  await act('questions.open', '', null, async () => {
    state.page = 'questions';
  });
}

// Brings an open ask of the session in view into view in its conversation, where the user asked to see it.
async function showQuestion(asked) {
  await act('question.show', asked.id, null, async () => {
    const shown = scroller();
    if (!shown) return;
    const question = shown.querySelector(`[data-question="${asked.id}"]`);
    if (question) question.scrollIntoView({ block: 'nearest' });
  });
}

// The user's word on the questions of a session's brain ask, posted to path: on its way until the list read again. True
// once the server took it. Only his own press sends it, and it moves nothing else.
async function settle(session, asked, name, path, body) {
  const key = answerKey(session, asked);
  state.answering.add(key);
  draw();
  const took = await act(name, key, null, async () => {
    try {
      await ask(path, { session: session.id, ask: asked.id, ...body });
      await readSessions();
      return true;
    } finally {
      state.answering.delete(key);
    }
  });
  return took === true;
}

// His answers to the questions.
function reply(session, asked, replies) {
  return settle(session, asked, 'question.reply', '/api/reply', { replies });
}

// His word that he closes the questions unanswered, so the composer is his as it was.
function dismiss(session, asked) {
  return settle(session, asked, 'question.dismiss', '/api/dismiss', {});
}

// Opens the agent the selected session's call tool sent, in the view of its kind.
// A step the click names is scrolled to in the view, once it is drawn.
async function openAgent(tool, step) {
  await act('agent.open', tool, READ_BUDGET_MS, async () => {
    const sent = (state.sent.get(state.selected) || []).find((one) => one.tool === tool);
    state.agent = { id: tool, name: sent?.type, shows: 'happened' };
    await readShown();
    if (step) scrollToStep(step);
  });
}

// Scrolls the open agent's view to its trail step by key, once it is drawn.
function scrollToStep(step) {
  requestAnimationFrame(() => {
    const found = [...document.querySelectorAll('.trail-step')].find((one) => one.dataset.step === step);
    if (found) found.scrollIntoView({ block: 'start' });
  });
}

// Shows the open agent's view of its kind, 'happened', or its whole conversation, 'conversation'.
async function show(shows) {
  await act('agent.show', shows, READ_BUDGET_MS, async () => {
    state.agent = { ...state.agent, shows };
    await readShown();
    if (shows === 'conversation') scrollWhenDrawn(state.agent.id);
  });
}

async function back() {
  await act('agent.back', '', READ_BUDGET_MS, async () => {
    const left = state.agent.id;
    state.agent = null;
    state.conversations.delete(left);
    state.views.delete(left);
    await readShown();
  });
}

async function start(agent, cwd) {
  await act('session.start', agent, null, async () => {
    const started = await ask('/api/start', { agent, cwd });
    await readSessions();
    await pick(started.id);
  });
}

async function say(words, images) {
  const sent = await act('session.say', `${state.selected} ${images.length}`, null, async () => {
    await ask('/api/say', { session: state.selected, words, images });
    return true;
  });
  return sent === true;
}

// ---- Quotes ----

// Reads the selection again: the Quote button follows it, as it moves with a scroll or a resize, and goes with it. Only
// a running session in view with no overlay over it takes a quote. It draws only when the button changed.
function readQuote() {
  const open = attachable() && !state.viewer && !state.explorer && !state.closing && !state.history;
  let quote = null;
  if (open) {
    quote = quoteOfSelection(getSelection());
  }

  if (sameQuote(quote, state.quote)) return;
  if (!quote && !state.quote) return;
  state.quote = quote;
  draw();
}

// Whether two quotes are the same text in the same place; a missing quote is the same as none only in readQuote.
function sameQuote(one, other) {
  if (!one || !other) return false;
  return one.text === other.text && one.top === other.top && one.left === other.left;
}

// Puts the selected text in the composer of the session in view as a quote; the selection goes, so the cursor stays in
// the box. True when it did.
async function quote() {
  readQuote();
  const session = attachable();
  if (!state.quote || !session) return false;
  const text = state.quote.text;
  return act('quote.add', session.id, null, async () => {
    getSelection().removeAllRanges();
    const quoted = composerQuote(session.id, text);
    state.quote = null;
    return quoted;
  });
}

// ---- Images ----

// The session whose composer images attach to: the one in view on its own page, while it runs.
function attachable() {
  if (state.page !== 'session' || state.agent) return null;
  const session = state.sessions.find((one) => one.id === state.selected);
  if (!session || !session.alive) return null;
  return session;
}

// The one attach path: a drop, a paste and the explorer each end here. The images go to the composer of the session in
// view; what stops them is said instead, and null once they wait there.
function attach(images) {
  const session = attachable();
  if (!session) return 'Open a running session to attach images to it.';
  const problems = images.map(attachProblem).filter((problem) => problem !== null);
  if (problems.length > 0) return problems.join('\n');
  composerAttach(session.id, images);
  return null;
}

// Attaches the images among dropped or pasted files.
async function attachFiles(files, how) {
  const images = files.filter(isImageFile);
  if (images.length === 0) return;
  await act(`images.${how}`, String(images.length), null, async () => {
    const read = await Promise.all(images.map(imageOfFile));
    state.problem = attach(read);
  });
}

function pasted(event) {
  const files = [...(event.clipboardData?.files || [])];
  if (!files.some(isImageFile)) return;
  event.preventDefault();
  attachFiles(files, 'paste');
}

function carriesFiles(event) {
  return [...(event.dataTransfer?.types || [])].includes('Files');
}

// A file dragged over the window is taken by it, so a drop never leaves the window for the file.
function draggedOver(event) {
  if (!carriesFiles(event)) return;
  event.preventDefault();
}

function dropped(event) {
  if (!carriesFiles(event)) return;
  event.preventDefault();
  attachFiles([...event.dataTransfer.files], 'drop');
}

// The explorer's places: Pictures, the session's own folder, and the folders last attached from.
function placesOf(places, session) {
  const shown = [{ name: 'Pictures', path: places.pictures }, { name: 'This session', path: session.cwd }];
  for (const path of places.recent) {
    if (shown.some((place) => place.path === path)) continue;
    const name = path.slice(path.lastIndexOf('/') + 1);
    shown.push({ name, path });
  }
  return shown;
}

async function openExplorer() {
  const session = attachable();
  if (!session) return;
  const places = await act('explorer.open', session.id, READ_BUDGET_MS, async () => {
    const read = await ask('/api/places');
    state.explorer = fileExplorer(placesOf(read, session));
    state.explorerProblem = null;
    readQuote();
    return read;
  });
  if (!places) return;
  await enterFolder(places.start);
}

// Opens a folder, its first tile in view: where the explorer opens, or one the user picked.
async function enterFolder(path) {
  await explore('explorer.folder', path, READ_BUDGET_MS, async () => {
    const folder = await ask(`/api/folder?path=${encodeURIComponent(path)}`);
    if (!state.explorer) return;
    fileExplorerShow(state.explorer, folder);
  });
  fileExplorerReveal(document.getElementById('app'));
}

function closeExplorer() {
  act('explorer.close', '', null, async () => {
    state.explorer = null;
  });
}

async function readImage(path) {
  return ask(`/api/image?path=${encodeURIComponent(path)}`);
}

async function preview(path) {
  await explore('explorer.preview', path, null, async () => {
    const image = await readImage(path);
    const node = document.createElement('img');
    node.src = dataUrlOf(image);
    node.alt = image.name;
    state.viewer = imageViewer(node);
  });
}

function folderOf(path) {
  const end = path.lastIndexOf('/');
  if (end === 0) return '/';
  return path.slice(0, end);
}

// Attaches the marked images and closes the explorer; then the server remembers each folder they came from, and a
// failure of that shows in the window, since the explorer is gone.
async function attachMarked() {
  const paths = fileExplorerMarked(state.explorer);
  if (paths.length === 0) return;
  const attached = await explore('explorer.attach', String(paths.length), null, async () => {
    const images = await Promise.all(paths.map(readImage));
    const problem = attach(images);
    if (problem) throw new Error(problem);
    state.explorer = null;
    return true;
  });
  if (!attached) return;
  const folders = new Set(paths.map(folderOf));
  await act('explorer.remember', String(folders.size), null, async () => {
    for (const folder of folders) {
      await ask('/api/attached-from', { folder });
    }
  });
}

// Reads the thumbnails of the images the explorer shows in view, a few at once, each drawn small once read.
function readThumbs() {
  const explorer = state.explorer;
  if (!explorer) return;
  const host = document.getElementById('app');
  for (const path of fileExplorerUnseen(explorer, host).slice(0, THUMB_READS - thumbReads)) {
    thumbReads++;
    readImage(path)
      .then((image) => thumbnailOf(dataUrlOf(image)))
      .then((thumb) => fileExplorerThumb(explorer, path, thumb))
      // An image that cannot be read or drawn keeps its blank tile, and is not read again.
      .catch(() => null)
      .finally(() => {
        thumbReads--;
        draw();
      });
  }
}

// Ends the turn of a session, as Ctrl+C does in a terminal; the composer says it stops at once, until the server says the
// turn ended.
async function interrupt(session) {
  state.stopping.add(session.id);
  draw();
  await act('session.interrupt', session.id, null, async () => {
    try {
      await ask('/api/interrupt', { session: session.id });
      await readSessions();
    } catch (error) {
      state.stopping.delete(session.id);
      throw error;
    }
  });
}

function isStopping(session) {
  return state.stopping.has(session.id);
}

// The agents a session sent that still run, each by its type and what it was asked, as the server lists them.
function runningAgents(session) {
  const running = [];
  for (const sent of state.sent.get(session.id) || []) {
    if (sent.state !== 'running') continue;
    running.push({ kind: sent.type, what: sent.description });
  }
  return running;
}

// The user asked to stop a session's turn: at once, unless agents it sent still run, which the turn would stop too;
// then the window names them and asks first.
function askInterrupt(session) {
  if (runningAgents(session).length === 0) {
    interrupt(session);
    return;
  }
  act('session.interrupt.ask', session.id, null, async () => {
    state.confirming = session.id;
  });
}

// What the stop confirm of a session shows, or null when no interrupt of it waits for the user's word.
function confirmOf(session) {
  if (state.confirming !== session.id) return null;
  return { work: runningAgents(session), stopping: isStopping(session) };
}

async function confirmInterrupt(session) {
  await interrupt(session);
  state.confirming = null;
  draw();
}

function keepRunning(session) {
  act('session.interrupt.keep', session.id, null, async () => {
    state.confirming = null;
  });
}

// Folds the session's running processes out over its conversation, or away again.
function fold(session) {
  act('processes.fold', session.id, null, async () => {
    if (state.fold === session.id) {
      state.fold = null;
      return;
    }
    state.fold = session.id;
  });
}

function folded(session) {
  return state.fold === session.id;
}

function foldAway() {
  act('processes.fold.away', state.fold, null, async () => {
    state.fold = null;
  });
}

function endingKey(session, job) {
  return `${session.id} ${job.pid}`;
}

function isEnding(session, job) {
  return state.ending.has(endingKey(session, job));
}

// The user's stop of one process the session's agent started, with everything below it; the session and its turn go
// on. It is on its way until the list read again; one gone already just leaves the list.
async function endProcess(session, job) {
  const key = endingKey(session, job);
  state.ending.add(key);
  draw();
  await act('process.end', key, null, async () => {
    try {
      await ask('/api/end-process', { session: session.id, pid: job.pid });
      await readSessions();
    } finally {
      state.ending.delete(key);
    }
  });
}

// While a fold is out, the sessions read again, so its processes and how long each is idle show as they change; and
// while a reload waits for a session that runs, so the reload button lights once none does, since a process that ends
// says nothing.
function readOften() {
  if (state.closed) return;
  if (state.fold !== null) {
    timed('processes.read', state.fold, READ_BUDGET_MS, readSessions);
    return;
  }
  if (state.reload === 'running') timed('reload.read', '', READ_BUDGET_MS, readSessions);
}

function answerKey(session, request) {
  return `${session.id} ${request.id}`;
}

function isAnswering(session, request) {
  return state.answering.has(answerKey(session, request));
}

// The user's answer to a permission request: allow, or deny with his words. It is on its way until the list read again.
async function answer(session, request, allow, words) {
  const key = answerKey(session, request);
  let verdict = 'deny';
  if (allow) verdict = 'allow';
  state.answering.add(key);
  draw();
  await act('permission.answer', `${key} ${verdict}`, null, async () => {
    try {
      await ask('/api/answer', { session: session.id, ask: request.id, allow, words });
      await readSessions();
    } finally {
      state.answering.delete(key);
    }
  });
}

// The user pressed a session's Close: the window names the session and asks first.
function askClose(session) {
  act('session.close.ask', session.id, null, async () => {
    state.closing = session.id;
  });
}

function keepOpen() {
  act('session.close.keep', state.closing, null, async () => {
    state.closing = null;
  });
}

function confirmClose(session) {
  state.closing = null;
  close(session);
}

// The close confirm: a modal over the window, on its shade. A click on the shade outside the box keeps the session.
function closeConfirmView() {
  if (!state.closing) return nothing;
  const session = state.sessions.find((one) => one.id === state.closing);
  if (!session) return nothing;
  const shown = { question: `Close ${session.agent} in ${session.cwd}?`, work: [], stop: closeLabel(session), keep: 'Keep' };
  const outside = (event) => {
    if (event.target === event.currentTarget) keepOpen();
  };
  return html`
    <div class="palette" @click=${outside}>
      <div class="palette-box">${stopConfirmView(shown, { onStop: () => confirmClose(session), onKeep: keepOpen })}</div>
    </div>
  `;
}

// The user's reload onto the newer build: the server hands every session to its successor, with the one in view, and
// says moved once the successor serves, so this window goes on there on the same session. A refusal, as a session that
// started to run, shows as the window's problem.
async function reload() {
  state.reloading = true;
  draw();
  await act('reload', state.selected ?? '', null, async () => {
    try {
      await ask('/api/reload', { session: state.selected });
    } catch (error) {
      state.reloading = false;
      throw error;
    }
  });
}

async function close(session) {
  await act('session.close', session.id, null, async () => {
    await ask('/api/close', { session: session.id });
    state.conversations.delete(session.id);
    state.sent.delete(session.id);
    if (state.selected === session.id) {
      state.selected = null;
      state.agent = null;
    }
    await readSessions();
  });
}

function askingOf(id) {
  const session = state.sessions.find((one) => one.id === id);
  if (!session) return 0;
  return session.asks.length + session.questions.length;
}

// The server said a session changed: the list reads again, and what is in view of it, its conversation and agents or
// the open agent, reads what is new. A permission request or a question that came to it scrolls it to its end, when it
// was there.
async function changed(id) {
  await timed('session.changed', id, READ_BUDGET_MS, async () => {
    const wasAtEnd = atEnd();
    const asked = askingOf(id);
    await readSessions();
    if (id !== state.selected) return;
    await readShown();
    if (askingOf(id) > asked && wasAtEnd) scrollWhenDrawn(shownKey());
  });
}

// ---- Drawing ----

function draw() {
  const host = document.getElementById('app');
  render(page(), host);
  if (awayNow() !== state.away) {
    state.away = !state.away;
    render(page(), host);
  }
  if (state.viewer) imageViewerPaint(state.viewer, host);
  workspaceFramesPaint(host, framesWanted());
  focusCloseConfirm();
  quotePlace(host, state.quote);
  readThumbs();
}

// A frame for each folder whose workspace opened, and that of the session in view shows while its code does.
function framesWanted() {
  const shownFolder = viewsShown()?.code ? sessionInView().cwd : null;
  const frames = [];
  for (const [folder, workspace] of state.workspaces) {
    if (!workspace.url) continue;
    frames.push({ folder, url: workspace.url, shown: folder === shownFolder });
  }
  return frames;
}

// What the window's columns show: 'conversation', 'code' or 'beside'.
function layoutOf(shown) {
  if (shown?.conversation && shown.code) return 'beside';
  if (shown?.code) return 'code';
  return 'conversation';
}

// The close confirm takes focus on its safe answer, Keep, so the keys never reach the composer behind it.
function focusCloseConfirm() {
  if (!state.closing) return;
  if (document.querySelector('.palette :focus')) return;
  document.querySelector('.palette .button:not(.no)')?.focus();
}

function mainView() {
  const actions = {
    onAgent: openAgent, onCall: openCall, selected: state.panel?.id ?? null, kinds: state.settings, shows: state.settings.agents, onSend: say, onClose: askClose, onBack: back, onShow: show, onPick: pick, onShowQuestion: showQuestion,
    onAnswer: answer, onReply: reply, onDismiss: dismiss, answering: isAnswering, onChange: draw, away: state.away, onToEnd: toEnd, onExplore: openExplorer,
    onInterrupt: askInterrupt, stopping: isStopping, confirm: confirmOf, onConfirm: confirmInterrupt, onKeep: keepRunning,
    folded, onFold: fold, onEndProcess: endProcess, ending: isEnding,
  };
  if (state.page === 'questions') return questionsPage(state.sessions, actions);
  if (state.page === 'settings') return settingsPage(state.settings, { onChange: setKind, onShows: setShows });
  const session = state.sessions.find((one) => one.id === state.selected);
  if (!session) {
    return html`<div class="quiet"><h2>No session in view</h2><p>Pick one on the side, or start a new one.</p></div>`;
  }
  const agents = state.sent.get(session.id) || [];
  if (state.agent) {
    const view = state.views.get(state.agent.id) || null;
    return agentPage(session, state.agent, view, conversationOf(state.agent.id), agents, actions);
  }
  return sessionPage(session, conversationOf(session.id), agents, actions);
}

function folderOfView() {
  const session = state.sessions.find((one) => one.id === state.selected);
  if (session) return session.cwd;
  return state.home;
}

function explorerView() {
  if (!state.explorer) return nothing;
  return fileExplorerView(state.explorer, {
    onOpen: enterFolder, onPreview: preview, onAttach: attachMarked, onClose: closeExplorer, onChange: draw, onScroll: readThumbs,
    problem: state.explorerProblem,
  });
}

function viewerView() {
  if (!state.viewer) return nothing;
  return imageViewerView(state.viewer, {
    onZoom: (factor, x, y) => { imageViewerDo(state.viewer, 'zoom', { factor, x, y }); draw(); },
    onPan: (dx, dy) => { imageViewerDo(state.viewer, 'pan', { dx, dy }); draw(); },
  });
}

// The information panel, on the right of a session's page or an agent's: the call the user clicked, in the conversation
// it was clicked in, and nothing while the user has none open.
function panelView() {
  if (state.page !== 'session') return nothing;
  if (!state.panel) return nothing;
  const events = conversationOf(state.panel.key).events;
  return toolPanelView(events, state.panel.id, { onClose: closePanel }) ?? nothing;
}

function page() {
  if (state.closed) {
    return html`<div class="quiet"><h2>This window is closed</h2><p>${state.closed}</p></div>`;
  }
  // The session in view is the current row only while its page is the one in view.
  let current = null;
  if (state.page === 'session') current = state.selected;
  const questionsInView = state.page === 'questions';
  const shown = viewsShown();
  const layout = layoutOf(shown);
  let panel = nothing;
  if (layout !== 'code') panel = panelView();
  const beside = layout === 'beside';
  const folder = shown?.code ? sessionInView().cwd : null;
  return html`
    <div class="code" data-panel=${panel === nothing ? 'none' : 'shown'} data-view=${layout}>
      ${viewStripView(shown, state.settings.beside, { onShow: showView, onBeside: toggleBeside })}
      <nav class="code-side" aria-label="Sessions">
        <header class="pane-head"><h1 class="pane-title">Sessions</h1>${reloadView(state.reload, state.reloading, { onReload: reload })}${historyButtonView({ onOpen: showHistory })}${settingsCogView(state.page === 'settings', { onOpen: showSettings })}</header>
        ${questionsRowView(state.sessions, questionsInView, { onOpen: showQuestions })}
        ${sessionListView(state.sessions, current, { onPick: pick })}
        ${newSessionView(state.agents, folderOfView(), { onStart: start })}
        ${state.problem ? html`<p class="code-problem" role="status">${state.problem}</p>` : nothing}
      </nav>
      <main class="code-main" ?data-focus=${beside && state.focus === 'conversation'}>${mainView()}</main>
      ${workspacePaneView(folder, state.workspaces.get(folder) ?? null, beside && state.focus === 'workspace')}
      ${panel}
    </div>
    ${explorerView()}
    ${viewerView()}
    ${closeConfirmView()}
    ${historyView(state.history, state.settings, { onPick: readPast, onBack: leavePast, onClose: closeHistory })}
    ${quoteView(state.quote, quote)}
  `;
}

// ---- The window ----

function heard(event, data) {
  if (event === 'session') changed(data);
  if (event === 'started') readSessions().then(() => pick(data));
  if (event === 'build') timed('build.published', '', READ_BUDGET_MS, readSessions);
  if (event === 'theme') windowSwapTheme(draw);
  if (event === 'close') shut('brain code opened in a newer window.', true);
  if (event === 'moved') move(data);
}

// The server handed this window on to its successor on a newer build: the window goes to the successor's launch URL,
// whose page trades the launch code, and shows the session that was in view. It neither closes nor says it stopped.
function move(url) {
  if (state.closed) return;
  state.closed = 'brain code moves onto its new build.';
  windowForget(SESSION_KEY);
  location.replace(url);
}

function ended(why) {
  if (why === 'session') {
    shut('This window’s session ended. Run brain code again.');
    return;
  }
  shut('brain code’s server stopped, and every session with it. Run brain code again.', true);
}

function shut(message, closeWindow) {
  if (state.closed) return;
  state.closed = message;
  windowForget(SESSION_KEY);
  if (closeWindow) window.close();
  draw();
}

// The session in view whose turn Ctrl+C interrupts: its own page shows, and its agent works.
function interruptible() {
  if (state.agent) return null;
  const session = state.sessions.find((one) => one.id === state.selected);
  if (!session || !session.alive || !session.working) return null;
  return session;
}

// Whether the user has text selected, in the page or in a box, which Ctrl+C then copies.
function hasSelection() {
  if (window.getSelection().toString() !== '') return true;
  const box = document.activeElement;
  return typeof box?.selectionStart === 'number' && box.selectionStart !== box.selectionEnd;
}

function isCtrlC(event) {
  if (event.altKey || event.shiftKey || event.metaKey) return false;
  return event.ctrlKey && event.key.toLowerCase() === 'c';
}

// Ctrl+C asks to interrupt the turn of the session in view, through the stop confirm when agents it sent run. With
// text selected it copies, as the browser does, and with no turn working it does nothing of its own.
function interruptKeyed(event) {
  if (!isCtrlC(event)) return;
  if (hasSelection()) return;
  const session = interruptible();
  if (!session) return;
  event.preventDefault();
  if (isStopping(session) || state.confirming === session.id) return;
  askInterrupt(session);
}

// Esc keeps the agents running while the stop confirm asks, and else closes the information panel, once no overlay took
// it. It never interrupts a turn.
function escapeKeyed(event) {
  if (event.key !== 'Escape') return;
  const session = interruptible();
  if (session && state.confirming === session.id) {
    event.preventDefault();
    if (!isStopping(session)) keepRunning(session);
    return;
  }
  if (state.page !== 'session' || !state.panel) return;
  event.preventDefault();
  closePanel();
}

// A click outside the fold of running processes folds it away.
function clicked(event) {
  if (state.fold === null) return;
  if (event.target.closest('.process-fold')) return;
  foldAway();
}

// The image viewer takes its keys first, then the file explorer, whose box is modal and takes every key, then the
// history, then the close confirm, which hold them too; else Alt+C, Alt+W and Alt+V pick what the strip shows, Alt+P
// opens the explorer and Alt+Q quotes the selection, and while the running processes are folded out, Esc folds them
// away. They take keys before the box the keys are in,
// so Enter never sends the words under them.
function overlayKeyed(event) {
  if (state.viewer) {
    viewerKeyed(event);
    return;
  }
  if (state.explorer) {
    event.preventDefault();
    event.stopPropagation();
    explorerKeyed(event);
    return;
  }
  if (state.history) {
    historyKeyed(event);
    return;
  }
  if (state.closing) {
    closeKeyed(event);
    return;
  }
  if (viewKeyed(event)) {
    event.preventDefault();
    event.stopPropagation();
    return;
  }
  if (event.altKey && event.code === 'KeyP') {
    event.preventDefault();
    event.stopPropagation();
    openExplorer();
  }
  if (event.altKey && event.code === 'KeyQ' && state.quote) {
    event.preventDefault();
    event.stopPropagation();
    quote();
  }
  if (state.fold !== null) {
    foldKeyed(event);
  }
}

function viewerKeyed(event) {
  const done = imageViewerKey(state.viewer, event);
  if (done === null) return;
  event.preventDefault();
  event.stopPropagation();
  if (done === 'close') state.viewer = null;
  draw();
}

function explorerKeyed(event) {
  const host = document.getElementById('app');
  const done = fileExplorerKey(state.explorer, event, fileExplorerColumns(host));
  if (!done) return;
  if (done.do === 'close') closeExplorer();
  if (done.do === 'open') enterFolder(done.path);
  if (done.do === 'preview') preview(done.path);
  if (done.do === 'attach') attachMarked();
  if (done.do === 'changed') draw();
  if (done.do === 'moved') {
    draw();
    fileExplorerReveal(host);
  }
}

// The close confirm owns the keys while it asks: Esc keeps the session, and no key reaches the page under it.
function closeKeyed(event) {
  event.stopPropagation();
  if (event.key !== 'Escape') return;
  event.preventDefault();
  keepOpen();
}

function foldKeyed(event) {
  if (event.key !== 'Escape') return;
  event.preventDefault();
  event.stopPropagation();
  foldAway();
}

async function boot() {
  const session = await windowLaunch(SESSION_KEY);
  if (!session) {
    shut('This window has no key. Run brain code again.');
    return;
  }
  document.title = session.title;
  document.addEventListener('keydown', overlayKeyed, true);
  document.addEventListener('keydown', interruptKeyed);
  document.addEventListener('keydown', escapeKeyed);
  document.addEventListener('scroll', scrolled, true);
  document.addEventListener('selectionchange', readQuote);
  window.addEventListener('resize', readQuote);
  document.addEventListener('paste', pasted);
  document.addEventListener('dragover', draggedOver);
  document.addEventListener('drop', dropped);
  document.addEventListener('click', clicked);
  window.addEventListener('message', heardFrame);
  window.addEventListener('blur', () => setTimeout(focusMoved));
  window.addEventListener('focus', focusMoved);
  document.addEventListener('focusin', focusMoved);
  document.addEventListener('open-image', (event) => {
    state.viewer = imageViewer(event.detail.node);
    draw();
  });
  windowListen(heard, ended, () => state.closed !== null);
  await timed('window.start', '', null, async () => {
    const listed = await ask('/api/agents');
    state.agents = listed.agents;
    state.settings = await ask('/api/settings');
    await readSessions();
  });
  // A window a reload opened shows the session the one before it showed.
  if (state.opened) await pick(state.opened);
}

window.addEventListener('pagehide', () => {
  windowSendRecords(unsent, true);
  windowBye();
});
setInterval(() => windowSendRecords(unsent, false), RECORDS_SEND_MS);
setInterval(readOften, FOLD_READ_MS);
boot();
