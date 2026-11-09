// The composer: the box the user writes to a session in, and its send button. Enter sends and Shift+Enter starts a new
// line, as in a terminal's Claude Code. What the session is doing shows beside the button, in the composer's words.
// While the session answers, the words still go: claude reads them once its turn ends, as a terminal queues them; and a
// stop button, as Ctrl+C, interrupts the turn. While agents the session sent still run, which the interrupt would stop
// too, the stop confirm over the box names them and asks first. Once the user stopped the turn, the composer says so
// until the turn ended.
//
// The images attached to the words wait over the box as thumbnails, each with its remove button, and go with the words,
// or alone when there are none. They come through the window's one attach path, composerAttach(): a drop, a paste, or
// the file explorer, which Alt+P or the images button opens.
//
// A quote comes in through composerQuote(), the same way, at the cursor.
//
// Component state: each session's unsent draft and its attached images, by its id, so switching sessions keeps what was
// being written.
import { html, nothing } from '/lit-html.js';
import { stopConfirmView } from '/components/stop-confirm.js';
import { imageThumbView } from '/components/image-thumb.js';
import { sessionState } from '/session-state.js';
import { dataUrlOf } from '/attach.js';

const STOP_QUESTION = 'Stopping this turn stops the agents it sent:';

const drafts = new Map();
const attached = new Map();
// Where the cursor was in each session's box when it lost the focus: the browser may move it while the user selects
// text elsewhere.
const carets = new Map();

const STATUS_WORDS = { ended: 'ended', asking: 'asks you', working: 'working', waiting: 'waiting for you', stopping: 'stopping' };

// Attaches images, each { name, mediaType, data }, to the unsent words of the session id.
export function composerAttach(id, images) {
  const waiting = attached.get(id) || [];
  attached.set(id, [...waiting, ...images]);
}

// The text as markdown quote lines, one "> " per line; a blank line keeps the quote going with a bare ">".
function quoteLines(text) {
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  return lines.map((line) => (line.trim() === '' ? '>' : `> ${line}`)).join('\n');
}

// The cursor of box as [from, to]: where it is while the box has the focus, else where it was when it lost it; the
// end of the words when it never had one.
function caretOf(id, box) {
  const end = box.value.length;
  if (document.activeElement === box) return [box.selectionStart, box.selectionEnd];
  const [from, to] = carets.get(id) || [end, end];
  return [Math.min(from, end), Math.min(to, end)];
}

// Puts text into the unsent words of the session id as quote lines, at the box's cursor and on a line of their own; the
// cursor then sits on a fresh line below them, with the box focused. False when the session in view has no box to take
// them.
export function composerQuote(id, text) {
  const box = document.querySelector('.code-composer textarea');
  if (!box || box.disabled) return false;
  const [from, to] = caretOf(id, box);
  const before = box.value.slice(0, from);
  const lead = before === '' || before.endsWith('\n') ? '' : '\n';
  const put = `${lead}${quoteLines(text)}\n`;
  box.value = before + put + box.value.slice(to);
  drafts.set(id, box.value);
  box.focus();
  box.setSelectionRange(before.length + put.length, before.length + put.length);
  return true;
}

function removed(id, image, onChange) {
  const left = (attached.get(id) || []).filter((one) => one !== image);
  attached.set(id, left);
  onChange();
}

// Sends the draft in box with the images attached to it; once the session took them, the draft, the images and the box
// are empty.
async function send(id, box, onSend) {
  const words = box.value.trim();
  const images = attached.get(id) || [];
  if (words.length === 0 && images.length === 0) return;
  const sent = await onSend(words, images);
  if (!sent) return;
  drafts.delete(id);
  carets.delete(id);
  attached.delete(id);
  box.value = '';
}

function attachedView(id, onChange) {
  const images = attached.get(id) || [];
  if (images.length === 0) return nothing;
  const thumbs = images.map((image) => imageThumbView(dataUrlOf(image), { name: image.name, onRemove: () => removed(id, image, onChange) }));
  return html`<div class="code-attached" aria-label="Attached images">${thumbs}</div>`;
}

// Remembers where the cursor was as the box loses the focus.
function left(event, id) {
  carets.set(id, [event.target.selectionStart, event.target.selectionEnd]);
}

function keyed(event, id, onSend) {
  if (event.key !== 'Enter' || event.shiftKey) return;
  event.preventDefault();
  send(id, event.target, onSend);
}

function clicked(event, id, onSend) {
  const box = event.currentTarget.closest('.code-composer').querySelector('textarea');
  send(id, box, onSend);
}

// The composer's word for the session: stopping once the user interrupted its turn, until the turn ended.
function statusOf(session, stopping) {
  const status = sessionState(session);
  if (status === 'ended' || !stopping) return status;
  return 'stopping';
}

// The stop button, while the session's agent works.
function stopView(session, stopping, onInterrupt) {
  if (!session.alive || !session.working) return nothing;
  return html`<button class="button no" ?disabled=${stopping} @click=${() => onInterrupt(session)}>Stop <kbd>Ctrl+C</kbd></button>`;
}

// The stop confirm, while an interrupt waits for the user's word.
function confirmView(session, asked, onConfirm, onKeep) {
  if (!asked) return nothing;
  const shown = { question: STOP_QUESTION, work: asked.work, stop: 'Stop them and the turn', keep: 'Keep running', stopping: asked.stopping };
  return stopConfirmView(shown, { onStop: () => onConfirm(session), onKeep: () => onKeep(session) });
}

export function composerView(session, { onSend, onInterrupt, stopping, confirm, onConfirm, onKeep, onExplore, onChange }) {
  const id = session.id;
  const halting = stopping(session);
  const status = statusOf(session, halting);
  return html`
    <div class="code-composer">
      ${confirmView(session, confirm(session), onConfirm, onKeep)}
      ${attachedView(id, onChange)}
      <textarea class="field code-words" placeholder="Write to ${session.agent}" ?disabled=${!session.alive}
        .value=${drafts.get(id) || ''} @input=${(event) => drafts.set(id, event.target.value)}
        @blur=${(event) => left(event, id)} @keydown=${(event) => keyed(event, id, onSend)}></textarea>
      <div class="code-composer-bar">
        <span class="code-status" data-state=${status}>${STATUS_WORDS[status]}</span>
        <span class="code-composer-actions">
          ${stopView(session, halting, onInterrupt)}
          <button class="button" ?disabled=${!session.alive} @click=${onExplore}>Images <kbd>Alt P</kbd></button>
          <button class="button" ?disabled=${!session.alive} @click=${(event) => clicked(event, id, onSend)}>Send <kbd>Enter</kbd></button>
        </span>
      </div>
    </div>
  `;
}
