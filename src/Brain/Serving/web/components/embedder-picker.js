// The embedding model picker: every model brain ships with a cursor on one, and, once Enter asks, what a switch to that
// model costs. It keeps its own state, and the page that shows it holds the instance: embedderPicker() makes one, the
// handlers here change it, and the page renders it again with embedderPickerView(), so it draws through the page's one
// render path. The page does what needs the network: it asks brain serve for the cost and starts the switch.
//
// An instance is { models, active, cursor, cost }: models holds each { name, megabytes }, active is the model search
// runs on, cursor is the index Enter acts on and cost is what a switch to the model under the cursor costs, or null
// until it is asked.
//
// Keys: j and k move the cursor, Enter or Space chooses a model, which asks what its switch costs, and Enter on the
// cost starts the switch. Esc closes. The arrows work beside the vim motions. embedderPickerHelp() lists them for the
// footer of the dialog, which the page draws around the picker with the dialog's head.
//
// embedderSwitchRow() draws the switch's progress under the setting's row, while a switch runs or after it failed.
import { html } from '/lit-html.js';
import { movedCursor, optionList, stepOfKey } from '/components/option-list.js';

// A picker over the models, its cursor on the model search runs on.
export function embedderPicker(models, active) {
  return { models, active, cursor: Math.max(0, models.findIndex((model) => model.name === active)), cost: null };
}

// What choosing means now: close on the model search runs on, switch on a cost already shown, else ask, what a switch
// to the model under the cursor costs. Each carries the model.
export function embedderPickerChoice(picker) {
  if (picker.cost) {
    return { step: 'switch', model: picker.cost.model };
  }
  const model = picker.models[picker.cursor].name;
  return { step: model === picker.active ? 'close' : 'ask', model };
}

// The keys the picker answers now, each { keys, text }, for the dialog's footer.
export function embedderPickerHelp(picker) {
  if (picker.cost) {
    return [{ keys: ['Enter'], text: 'switch' }, { keys: ['Esc'], text: 'cancel, nothing changes' }];
  }
  return [{ keys: ['j', 'k'], text: 'move' }, { keys: ['Enter'], text: 'choose' }, { keys: ['Esc'], text: 'close' }];
}

// Records what the switch costs, which the picker then shows in place of its list.
export function askedCost(picker, cost) {
  picker.cost = cost;
}

// Moves the cursor as the key says. Returns choose for Enter or Space, close for Esc, moved when the cursor moved and
// null for any other key; the page acts on choose and close, and renders again on moved. Once the cost is shown only
// Enter and Esc act.
export function embedderPickerKey(picker, key) {
  if (key === 'Escape') {
    return 'close';
  }
  if (key === 'Enter') {
    return 'choose';
  }
  if (picker.cost) {
    return null;
  }
  if (key === ' ') {
    return 'choose';
  }
  const by = stepOfKey(key);
  if (by === 0) {
    return null;
  }
  picker.cursor = movedCursor(picker.cursor, by, picker.models.length);
  return 'moved';
}

// The list of models, or what a switch costs, under the dialog's head. A press moves the cursor there and calls
// onMoved, so the page renders again; a double click runs onChoose.
export function embedderPickerView(picker, { onMoved, onChoose }) {
  if (picker.cost) {
    return html`<div class="embedder-picker">${costView(picker)}</div>`;
  }
  const options = picker.models.map((model) => ({ text: model.name, hint: `${model.megabytes} MB`, chosen: model.name === picker.active }));
  const point = (index) => {
    picker.cursor = index;
    onMoved();
  };
  return html`
    <div class="embedder-picker">
      <div class="embedder-picker-list">
        ${optionList({ label: 'Embedding models', options, cursor: picker.cursor, current: false, onPoint: point, onChoose })}
      </div>
    </div>`;
}

// What a switch to the model costs, in words, before the user says yes.
function costView({ cost, active }) {
  const download = cost.download > 0
    ? `It downloads about ${cost.download} MB, each file checked against its pinned SHA-256.`
    : 'Its files are on disk already, so nothing downloads.';
  return html`
    <div class="embedder-picker-cost" role="alertdialog" aria-label="Switch to ${cost.model}">
      <p class="embedder-picker-ask">Switch search to ${cost.model}?</p>
      <ul>
        <li>${download}</li>
        <li>It embeds all ${cost.nodes} nodes again with ${cost.model}: every embedding is generated again.</li>
        <li>It takes CPU while it runs, on at most two threads.</li>
        <li>Search stays on ${active} until it is done, and ${active}'s embeddings go then.</li>
      </ul>
    </div>`;
}

// The setting's control with the switch under it: the step it is on and a progress bar, or why it stopped. view is what
// brain serve said of the embedding model, { active, switch }. With no switch it is the control alone.
export function embedderSwitchRow(control, view) {
  const now = view?.switch;
  if (!now) {
    return control;
  }
  if (now.phase === 'failed') {
    return html`
      <div class="embedder-picker-state">
        ${control}
        <p class="embedder-picker-failed">The switch to ${now.to} stopped: ${now.error}. Search stays on ${view.active}.</p>
      </div>`;
  }
  const megabytes = (bytes) => Math.round(bytes / 1e6);
  const said = now.phase === 'download'
    ? `Downloading ${now.to}: ${megabytes(now.done)} of ${megabytes(now.total)} MB`
    : `Embedding every node with ${now.to}: ${now.done} of ${now.total}`;
  return html`
    <div class="embedder-picker-state">
      ${control}
      <label class="embedder-picker-step">${said}<progress max=${String(Math.max(1, now.total))} value=${String(now.done)}></progress></label>
    </div>`;
}
