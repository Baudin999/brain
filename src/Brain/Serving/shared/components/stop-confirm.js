// The stop confirm: what an action would stop, named before the user says so, as every place that stops work running
// under something asks first. It asks its question, lists each piece of work by its kind and what it does, with a mark
// on one that holds a build slot, and offers the stop and the keep; Esc keeps, which the window that shows it handles.
// Once the user said stop, it is stopping and takes no second answer. brain code's window asks it before an interrupt
// would stop the agents a session sent and before it closes a session, brain's window before closing a chat stops what
// it started, and the Stylebook shows it in its states.
//
// It is the caller's shape: { question, work: [{ kind, what, slot }], stop, keep, stopping }, stop and keep being the
// buttons' words, and slot true for work that holds a build slot.
import { html, nothing } from '/lit-html.js';

function slotView(work) {
  if (!work.slot) return nothing;
  return html` <span class="stop-confirm-slot">holds a build slot</span>`;
}

function workView(work) {
  return html`<li><span class="stop-confirm-kind">${work.kind}</span> <span class="stop-confirm-what">${work.what}</span>${slotView(work)}</li>`;
}

export function stopConfirmView({ question, work, stop, keep, stopping = false }, { onStop, onKeep }) {
  let state = 'asking';
  if (stopping) state = 'stopping';
  return html`
    <div class="stop-confirm" role="alertdialog" aria-label=${question} data-state=${state}>
      <p class="stop-confirm-question">${question}</p>
      <ul class="stop-confirm-list">${work.map(workView)}</ul>
      <div class="stop-confirm-answer">
        <button class="button no" ?disabled=${stopping} @click=${onStop}>${stop}</button>
        <button class="button" ?disabled=${stopping} @click=${onKeep}>${keep} <kbd>Esc</kbd></button>
      </div>
    </div>
  `;
}
