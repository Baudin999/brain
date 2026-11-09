// The settings page in brain's window: a page in the one window, never a window of its own. Alt+` opens it from anywhere
// and Esc closes it. The settings themselves are global state, which state.js holds and writes: this page reads them
// through setting() and changes one through saveSetting(). The theme is no setting; the window takes it from omarchy.
//
// Every model setting holds a model and an effort, and the model selector changes them together: a small modal with the
// models in one list and the efforts in the other. Every setting offers the efforts a sitting runs at, and the page
// shows the one that runs. The judge, the dreamer and the deep dream ask what their settings name.
//
// The embedding model search ranks by meaning with is no setting saved as it is picked: it is a switch. Its picker lists
// every model brain ships; picking another says first what the switch costs, the download when the model's files are
// not on disk, how many nodes are embedded again and that it takes CPU, and Enter starts it while Esc changes nothing.
// brain serve then downloads the model and embeds every node again, and the row shows each step's progress bar. Search
// stays on the model it runs until the switch is done.
//
// Keys, the vim motions first and the arrows beside them: j and k move between settings, gg and G jump to the first and
// the last, Enter, Space or c change the focused one (the selector opens, a switch flips), h and l move between the
// dreamer's two models. In the selector j and k move within a list, h, l and Tab move between model and effort, Enter
// chooses both and Esc closes it without a change.
//
// The page and the dialog box over it render with lit-html from settingsPage, the page's state, and from the global
// state: a handler changes settingsPage and calls redrawSettings(), which renders both again, and lit-html changes only
// the parts of the page that changed. A change of the global state renders them again through the inbox's redraw().
// A render only draws. Focus and scroll move only where the code means to: a key moving to another row shows and focuses
// it, a dialog opening takes the focus and closing gives it back to the row.
// Its controls are components: the field row, the select, the toggle, the option list, the model selector and the
// embedding model picker, whose instances settingsPage.selector and settingsPage.picker hold.
import { html, render, nothing } from '/lit-html.js';
import { fieldRow } from '/components/field-row.js';
import { askedCost, embedderPicker, embedderPickerChoice, embedderPickerHelp, embedderPickerKey, embedderPickerView, embedderSwitchRow } from '/components/embedder-picker.js';
import { modelSelector, modelSelectorKey, modelSelectorView, selectedPair } from '/components/model-selector.js';
import { movedCursor, stepOfKey } from '/components/option-list.js';
import { select } from '/components/select.js';
import { toggle } from '/components/toggle.js';
import { app, offer } from '/seam.js';
import { loadSettings, saveSetting, setting, settingsKnown, settingsProblem } from '/state.js';

const { state, registered, NODE_REVIEW, MODELS, EFFORTS, $, call, toast, keys, plainKey, registerInbox, showInbox, renderMain, templateNode } = app;

const SETTINGS = ':settings';

const CLAUDE_MODELS = MODELS.map(([id, text]) => ({ id, text }));
// Gemma4 and GLM cannot be reached yet: the list shows where they will land.
const CALL_MODELS = [
  { id: 'gemma4', text: 'Gemma4 (local)', later: true },
  { id: 'glm-5.3-flash', text: 'GLM-5.3-flash', later: true },
  ...CLAUDE_MODELS.map(({ id, text }) => ({ id, text: `Claude ${text}` })),
];
const EFFORT_OPTIONS = EFFORTS.map((effort) => ({ id: effort, text: effort }));

// Each setting names its key in settings.json and the models it offers; a pair holds two choices. selector is the open
// model selector, { at, slot, instance }, or null; instance is the model selector's own state, whose cursors are what
// Enter chooses. embedder is what brain serve last said of the embedding model, { active, models, switch }, and picker is
// the open embedding model picker's own state, or null.
const settingsPage = {
  embedder: null,
  picker: null,
  from: NODE_REVIEW,
  at: 0,
  slot: 0,
  selector: null,
  g: false,
  groups: [
    {
      title: 'Agents',
      note: 'Claude only, through Claude Code. Whether a task is easy or difficult stays the open agent\'s judgment; a setting here only names the model and effort each gets, and the open agent passes them when it sends one.',
      rows: [
        { id: 'builder-easy', label: 'Builder, easy task', models: CLAUDE_MODELS },
        { id: 'builder-difficult', label: 'Builder, difficult task', models: CLAUDE_MODELS },
        { id: 'validator', label: 'Validator', models: CLAUDE_MODELS },
        { id: 'readiness', label: 'Readiness check', models: CLAUDE_MODELS },
      ],
    },
    {
      title: 'Brain\'s own calls',
      note: 'Any provider. The dreamer asks two models side by side, since their disagreement is the point. The deep dream is the open agent, so it stays Claude.',
      rows: [
        { id: 'judge', label: 'Judge', models: CALL_MODELS },
        { id: 'dreamer', label: 'Dreamer', models: CALL_MODELS, pair: true },
        { id: 'deep-dream', label: 'Deep dream, into brain', models: CLAUDE_MODELS },
      ],
    },
    {
      title: 'Chats and sittings',
      note: 'What a new chat or a new sitting starts with, until it is picked otherwise.',
      rows: [
        { id: 'chat', label: 'A new chat', models: CLAUDE_MODELS },
        { id: 'sitting', label: 'A new sitting', models: CLAUDE_MODELS },
      ],
    },
    {
      title: 'Search',
      note: 'The embedding model ranks search by meaning, on the CPU inside brain. A switch downloads the model and embeds every node again; search stays on the model it runs until that is done.',
      rows: [
        { id: 'embedder', label: 'Embedding model', embedder: true },
      ],
    },
    {
      title: 'Home',
      rows: [
        { id: 'starfield', label: 'Starry background', hint: 'A still image of faint stars and a soft nebula behind the constellation, on a dark theme.', toggle: true },
        { id: 'motion', label: 'Motion', hint: 'Sparks run along edges and the view glides. Off, or with reduced motion asked for, Home is a still sky.', toggle: true },
        { id: 'glow', label: 'Glowing stars', hint: 'A soft halo around well-connected stars, on a dark theme.', toggle: true },
        { id: 'sparks', label: 'Sparks on edges', hint: 'An edge travelled today carries a spark of light along it now and then. Needs motion. Without it, such an edge shows a little brighter.', toggle: true },
        { id: 'newstars', label: 'New stars shine', hint: 'A node born today shines a little brighter, with a soft light around it.', toggle: true },
        { id: 'fading', label: 'Fading edges', hint: 'An edge that brain faded lists, a candidate for doubt, is drawn fainter.', toggle: true },
        { id: 'glide', label: 'Gliding view', hint: 'Alt+R, a scroll zoom and a pan let go while moving ease or coast to their place instead of jumping. Needs motion.', toggle: true },
        { id: 'atlas', label: 'Atlas names', hint: 'Cluster names in small capitals with wide letter spacing, like the constellations of a star atlas, the count smaller beside them.', toggle: true },
      ],
    },
  ],
};

const settingRows = () => settingsPage.groups.flatMap((group) => group.rows);

// The value a row's control shows: a pair's choice in that slot, else the one value.
function rowValue(row, slot) {
  return row.pair ? setting(row.id)?.[slot] : setting(row.id);
}

// Once the window has its session: the settings, then the embedding model.
async function startSettings() {
  await loadSettings();
  await followSwitch();
}

function toggleSettings() {
  if (state.inbox === SETTINGS) {
    closeSettings();
    return;
  }
  settingsPage.from = state.inbox;
  showInbox(SETTINGS);
  redrawSettings();
  showCurrentRow();
}

function closeSettings() {
  dismissDialog();
  showInbox(registered.some((inbox) => inbox.name === settingsPage.from) ? settingsPage.from : NODE_REVIEW);
  $('main').focus();
}

const textOf = (options, id) => options.find((option) => option.id === id)?.text ?? id ?? '';

// Renders the dialog box and, while the page is shown, the page from settingsPage. Every change of state ends here, and
// a render only draws: it never moves the focus or the scroll. The code that moves them on purpose is below.
function redrawSettings() {
  render(dialogView(), $('selector-box'));
  $('selector').hidden = !dialogOpen();
  if (state.inbox === SETTINGS) renderMain();
}

const dialogOpen = () => !!(settingsPage.selector || settingsPage.picker);

// ---- Focus and scroll: moved only here, where a key or a dialog means to ----

const currentRow = () => document.querySelector(`[data-field="${settingRows()[settingsPage.at].id}"]`);

// The current row in view and focused, so a screen reader follows the keys.
function showCurrentRow() {
  const row = currentRow();
  row?.scrollIntoView({ block: 'nearest' });
  row?.focus();
}

// The open dialog focused, with the option under its cursor in view.
function showDialog() {
  showDialogCursor();
  $('selector-box').focus();
}

function showDialogCursor() {
  $('selector-box').querySelectorAll('.option-list-item[aria-selected="true"]').forEach((option) => option.scrollIntoView({ block: 'nearest' }));
}

// Closes the dialog and gives the focus back to the row it was opened from.
function closeDialog() {
  if (!dialogOpen()) return;
  dismissDialog();
  showCurrentRow();
}

// Closes the dialog and leaves the focus where it is.
function dismissDialog() {
  if (!dialogOpen()) return;
  settingsPage.selector = null;
  settingsPage.picker = null;
  redrawSettings();
}

function changeSetting(at, slot) {
  const row = settingRows()[at];
  settingsPage.at = at;
  settingsPage.slot = slot;
  if (!settingsKnown()) {
    toast(settingsProblem() || 'The settings are still being read.');
    return;
  }
  if (row.toggle) saveSetting(row.id, !setting(row.id));
  else if (row.embedder) openEmbedderPicker();
  else openSelector(at, slot);
}

// ---- The embedding model: a switch, with what it costs said first ----

const SWITCH_MS = 500;
const running = (view) => view?.switch && view.switch.phase !== 'failed';

// Reads the embedding model from brain serve, and while a switch runs reads it again until the switch ends.
async function followSwitch() {
  const { ok, body } = await call('/api/embedder');
  if (!ok) return;
  const was = settingsPage.embedder;
  settingsPage.embedder = body;
  if (running(was) && !body.switch) {
    toast(`Search ranks by ${body.active} now.`);
    await loadSettings();
    return;
  }
  redrawSettings();
  if (running(body)) setTimeout(followSwitch, SWITCH_MS);
}

function openEmbedderPicker() {
  const view = settingsPage.embedder;
  if (!view) {
    toast('The embedding model is still being read.');
    return;
  }
  if (running(view)) {
    toast(`The switch to ${view.switch.to} still runs.`);
    return;
  }
  settingsPage.picker = embedderPicker(view.models, view.active);
  redrawSettings();
  showDialog();
}

// Does what the picker says choosing means: starts the switch, closes, or asks what the switch costs.
async function chooseModel() {
  const picker = settingsPage.picker;
  const { step, model } = embedderPickerChoice(picker);
  if (step === 'switch') {
    startSwitch(model);
    return;
  }
  if (step === 'close') {
    closeDialog();
    return;
  }
  const { ok, body } = await call(`/api/embedder/cost?model=${encodeURIComponent(model)}`);
  if (settingsPage.picker !== picker) return;
  if (!ok) {
    toast(body.error);
    return;
  }
  askedCost(picker, body);
  redrawSettings();
}

async function startSwitch(model) {
  closeDialog();
  const { ok, body } = await call('/api/embedder', { method: 'POST', body: JSON.stringify({ model }) });
  if (!ok) {
    toast(body.error);
    return;
  }
  settingsPage.embedder = body;
  redrawSettings();
  setTimeout(followSwitch, SWITCH_MS);
}

// Hands the key to the embedding model picker, then chooses, closes or renders again as it says. Every plain key is the
// picker's while it is open.
function onEmbedderPickerKey(event) {
  const done = embedderPickerKey(settingsPage.picker, event.key);
  if (done === 'choose') chooseModel();
  else if (done === 'close') closeDialog();
  else if (done === 'moved') {
    redrawSettings();
    showDialogCursor();
  }
  return true;
}

function embedderDialog() {
  const picker = settingsPage.picker;
  const foot = html`
    <div class="picker-foot">
      ${embedderPickerHelp(picker).map((help) => html`<span>${help.keys.map((key) => keys(key))} ${help.text}</span>`)}
    </div>`;
  const view = embedderPickerView(picker, { onMoved: redrawSettings, onChoose: chooseModel });
  return dialogFrame('Embedding model', 'Search ranks by meaning with it', view, foot);
}

// The embedding model's row: the model search runs on, and while a switch runs its step and its progress bar.
function embedderControl(row, at) {
  const view = settingsPage.embedder;
  const expanded = settingsPage.at === at && !!settingsPage.picker;
  const control = select({ label: row.label, text: view?.active ?? '', expanded, focused: false, onOpen: () => changeSetting(at, 0) });
  return embedderSwitchRow(control, view);
}

// ---- The model selector: a model and its effort, chosen together ----

function openSelector(at, slot) {
  const row = settingRows()[at];
  const now = rowValue(row, slot);
  settingsPage.selector = { at, slot, instance: modelSelector(row.models, EFFORT_OPTIONS, now) };
  redrawSettings();
  showDialog();
}

// Saves the model and effort under the cursors, unless the model cannot be reached yet, and closes the selector.
function chooseSelected() {
  const { at, slot, instance } = settingsPage.selector;
  const row = settingRows()[at];
  const [model, effort] = selectedPair(instance);
  if (model.later) {
    toast(`${model.text} cannot be reached yet.`);
    return;
  }
  const choice = { model: model.id, effort: effort.id };
  closeDialog();
  const now = rowValue(row, slot);
  if (choice.model === now.model && choice.effort === now.effort) return;
  saveSetting(row.id, row.pair ? setting(row.id).map((each, index) => (index === slot ? choice : each)) : choice);
}

// The shade around the dialog box, in index.html like the command palette's: a press on it closes the dialog.
$('selector').addEventListener('mousedown', (event) => {
  if (event.target !== event.currentTarget) return;
  closeDialog();
});

function selectorView() {
  const { at, slot, instance } = settingsPage.selector;
  const row = settingRows()[at];
  const now = rowValue(row, slot);
  const title = row.pair ? `${row.label}, model ${slot + 1}` : row.label;
  const view = modelSelectorView(instance, { chosen: [now.model, now.effort], onMoved: redrawSettings, onChoose: chooseSelected });
  const foot = html`
    <div class="picker-foot">
      <span>${keys('j')}${keys('k')} move</span>
      <span>${keys('h')}${keys('l')} or ${keys('Tab')} model, effort</span>
      <span>${keys('Enter')} choose</span>
      <span>${keys('Esc')} close</span>
    </div>`;
  return dialogFrame(title, 'Model selector', view, foot);
}

// The dialog box's frame, one for both dialogs: a head with its title and a note, the dialog's own view and a footer.
function dialogFrame(title, note, view, foot) {
  return html`
    <p class="picker-head">${title}<span class="picker-head-keys">${note}</span></p>
    ${view}
    ${foot}`;
}

// What the dialog box holds: the model selector, the embedding model picker or nothing.
function dialogView() {
  if (settingsPage.selector) return selectorView();
  if (settingsPage.picker) return embedderDialog();
  return nothing;
}

// ---- The page ----

function settingControl(row, at, slot) {
  if (row.embedder) return embedderControl(row, at);
  if (row.toggle) {
    return toggle({ on: setting(row.id) === true, label: row.label, onToggle: () => changeSetting(at, 0) });
  }
  const focused = settingsPage.at === at && settingsPage.slot === slot;
  const value = rowValue(row, slot);
  return select({
    label: row.pair ? `${row.label}, model ${slot + 1}` : row.label,
    text: textOf(row.models, value?.model),
    aside: textOf(EFFORT_OPTIONS, value?.effort),
    expanded: focused && !!settingsPage.selector,
    focused: focused && row.pair === true,
    onOpen: () => changeSetting(at, slot),
  });
}

function settingRow(row, at) {
  const slots = row.pair ? [0, 1] : [0];
  const controls = slots.map((slot) => settingControl(row, at, slot));
  return fieldRow({ id: row.id, label: row.label, hint: row.hint, current: settingsPage.at === at }, controls);
}

// The page, drawn from settingsPage.
function settingsView() {
  const rows = settingRows();
  const said = settingsProblem() || 'Each change is saved as you make it, in settings.json in the data repo, and committed there.';
  return html`
    <div class="settings">
      <header class="pane-head flush set-head">
        <h1 class="pane-title">Settings</h1>
        <p>${said}</p>
      </header>
      ${settingsPage.groups.map((group) => html`
        <section class="set-group">
          <h2>${group.title}</h2>
          ${group.rows.map((row) => settingRow(row, rows.indexOf(row)))}
          ${group.note ? html`<p class="set-note">${group.note}</p>` : nothing}
        </section>`)}
    </div>`;
}

// The queue column on this page: its keys, in place of a queue of work.
function settingsKeys() {
  return html`
    <div class="sky-legend">
      <p class="sky-keys">
        <span>${keys('j')}${keys('k')} move</span>
        <span>${keys('Enter')} change</span>
        <span>${keys('h')}${keys('l')} the dreamer's two</span>
        <span>${keys('gg')}${keys('G')} first, last</span>
        <span>${keys('Esc')} close</span>
        <span>${keys('Alt', '`')} settings, from anywhere</span>
      </p>
      <p class="sky-note">The theme is not a setting. The window follows omarchy.</p>
    </div>`;
}

// Hands the key to the model selector, then chooses, closes or renders again as it says. Every plain key is the
// selector's while it is open.
function onSelectorKey(event) {
  const done = modelSelectorKey(settingsPage.selector.instance, event.key);
  if (done === 'choose') chooseSelected();
  else if (done === 'close') closeDialog();
  else if (done === 'moved') {
    redrawSettings();
    showDialogCursor();
  }
  return true;
}

function onSettingsPageKey(key, rows) {
  const row = rows[settingsPage.at];
  const move = (to) => {
    settingsPage.at = to;
    settingsPage.slot = 0;
    redrawSettings();
    showCurrentRow();
  };
  const slot = (to) => {
    if (row.pair) settingsPage.slot = to;
    redrawSettings();
  };
  const by = stepOfKey(key);
  if (by !== 0) move(movedCursor(settingsPage.at, by, rows.length));
  else if (key === 'G') move(rows.length - 1);
  else if (key === 'g') {
    if (settingsPage.g) move(0);
    settingsPage.g = !settingsPage.g;
    return true;
  } else if (key === 'Enter' || key === ' ' || key === 'c') changeSetting(settingsPage.at, settingsPage.slot);
  else if (key === 'h' || key === 'ArrowLeft') slot(0);
  else if (key === 'l' || key === 'ArrowRight') slot(1);
  else if (key === 'Escape') closeSettings();
  else return false;
  settingsPage.g = false;
  return true;
}

function onSettingsKey(event) {
  if (state.inbox !== SETTINGS || state.view !== 'review' || !plainKey(event)) return false;
  const taken = settingsPage.selector ? onSelectorKey(event)
    : settingsPage.picker ? onEmbedderPickerKey(event)
      : onSettingsPageKey(event.key, settingRows());
  if (taken) event.preventDefault();
  return taken;
}

registerInbox({
  name: SETTINGS,
  label: 'Settings',
  bottom: true,
  noChat: true,
  noWaiting: true,
  count: () => 0,
  start: startSettings,
  list: () => templateNode(settingsKeys()),
  view: settingsView,
  redraw: redrawSettings,
  keys: onSettingsKey,
  commands: () => [{ name: 'Settings', keys: ['Alt', '`'], run: toggleSettings }],
});

// app.js opens and closes the page on Alt+`, from anywhere.
offer({ toggleSettings });
