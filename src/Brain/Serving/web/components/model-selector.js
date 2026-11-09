// The model selector: a model and its effort, chosen together, in two option lists side by side. It keeps its own state,
// and the page that shows it holds the instance: modelSelector() makes one, the handlers here change it, and the page
// renders it again with modelSelectorView(), so it draws through the page's one render path.
//
// An instance is { lists, column, cursor }: lists holds the models and the efforts, each option { id, text, later },
// where later is a model that cannot be reached yet. column is the list the keys are in, 0 the models and 1 the
// efforts, and cursor holds each list's cursor, which Enter chooses.
//
// Keys: j and k move within a list, h, l and Tab move between model and effort, Enter or Space chooses both and Esc
// closes. The arrows work beside the vim motions.
import { html } from '/lit-html.js';
import { movedCursor, optionList, stepOfKey } from '/components/option-list.js';

// A selector over models and efforts, its cursors on now's model and effort, or on the first of each.
export function modelSelector(models, efforts, now) {
  const index = (options, id) => Math.max(0, options.findIndex((option) => option.id === id));
  return { lists: [models, efforts], column: 0, cursor: [index(models, now.model), index(efforts, now.effort)] };
}

// The model and the effort under the cursors.
export function selectedPair(selector) {
  return selector.lists.map((options, column) => options[selector.cursor[column]]);
}

// Moves the cursors as the key says. Returns choose for Enter or Space, close for Esc, moved when the cursors moved and
// null for any other key; the page acts on choose and close, and renders again on moved.
export function modelSelectorKey(selector, key) {
  const by = stepOfKey(key);
  if (by !== 0) return step(selector, by);
  if (key === 'h' || key === 'ArrowLeft') return toColumn(selector, 0);
  if (key === 'l' || key === 'ArrowRight') return toColumn(selector, 1);
  if (key === 'Tab') return toColumn(selector, 1 - selector.column);
  if (key === 'Enter' || key === ' ') return 'choose';
  if (key === 'Escape') return 'close';
  return null;
}

function step(selector, by) {
  const { column } = selector;
  selector.cursor[column] = movedCursor(selector.cursor[column], by, selector.lists[column].length);
  return 'moved';
}

function toColumn(selector, column) {
  selector.column = column;
  return 'moved';
}

// The two lists under their titles. chosen holds the model's and the effort's id in use now, which carry a check. A
// press moves the cursor there and calls onMoved, so the page renders again; a double click runs onChoose.
export function modelSelectorView(selector, { chosen, onMoved, onChoose }) {
  return html`
    <div class="model-selector">
      ${columnView(selector, 0, 'Model', chosen[0], onMoved, onChoose)}
      ${columnView(selector, 1, 'Effort', chosen[1], onMoved, onChoose)}
    </div>`;
}

// One list under its title. The list the keys are in carries data-at, and its cursor is the current row.
function columnView(selector, column, title, chosen, onMoved, onChoose) {
  const here = selector.column === column;
  const options = selector.lists[column].map((option) => ({
    text: option.text,
    hint: option.later ? 'not yet' : '',
    disabled: option.later,
    chosen: option.id === chosen,
  }));
  const point = (index) => {
    selector.column = column;
    selector.cursor[column] = index;
    onMoved();
  };
  return html`
    <div class="model-selector-column" ?data-at=${here}>
      <p class="model-selector-title" id="model-selector-title-${column}">${title}</p>
      ${optionList({ labelledBy: `model-selector-title-${column}`, options, cursor: selector.cursor[column], current: here, onPoint: point, onChoose })}
    </div>`;
}
