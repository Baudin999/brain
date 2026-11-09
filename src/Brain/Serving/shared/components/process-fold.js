// The process fold: the processes a session's agent started that still run, behind a chip that counts them and how
// many are idle, as "3 running, 1 idle". A click on the chip folds out a list over what is under it, so nothing under
// it moves. Each process is one line: its command cut to the line, whole on hover, a mark when it holds a build slot,
// "idle" with how long once its tree used no CPU time for a while, and a stop. With nothing running it draws nothing.
// The window that shows it folds it away on Esc or a click outside it. brain code's window shows it in a session's
// head, and the Stylebook shows it in its states.
//
// It is the caller's shape: { work: [{ pid, command, slot, idle }], open, now, stopping }, idle being when the process
// went idle, as an ISO time, or absent while it runs; now the time to count idleness from, in ms; and stopping(work)
// true while that process's stop is on its way.
import { html, nothing } from '/lit-html.js';

const MINUTE_MS = 60 * 1000;
const HOUR_MS = 60 * MINUTE_MS;

// How long a process is idle, in its largest whole unit: "idle 40s", "idle 4m", "idle 2h".
function idleWords(idle, now) {
  const quiet = Math.max(0, now - Date.parse(idle));
  if (quiet >= HOUR_MS) return `idle ${Math.floor(quiet / HOUR_MS)}h`;
  if (quiet >= MINUTE_MS) return `idle ${Math.floor(quiet / MINUTE_MS)}m`;
  return `idle ${Math.floor(quiet / 1000)}s`;
}

function chipWords(work) {
  const idle = work.filter((job) => job.idle).length;
  if (idle === 0) return `${work.length} running`;
  return `${work.length} running, ${idle} idle`;
}

function slotView(job) {
  if (!job.slot) return nothing;
  return html`<span class="process-fold-slot">build slot</span>`;
}

function stateView(job, now) {
  if (!job.idle) return html`<span class="process-fold-state" data-state="running">running</span>`;
  return html`<span class="process-fold-state" data-state="idle">${idleWords(job.idle, now)}</span>`;
}

function jobView(job, now, stopping, onStop) {
  const ending = stopping(job);
  let stop = 'Stop';
  if (ending) stop = 'Stopping';
  return html`
    <li class="process-fold-job">
      <span class="process-fold-command" title=${job.command}>${job.command}</span>
      ${slotView(job)}
      ${stateView(job, now)}
      <button class="button no" ?disabled=${ending} @click=${() => onStop(job)}>${stop}</button>
    </li>
  `;
}

function listView(work, now, stopping, onStop) {
  return html`
    <div class="process-fold-list" role="dialog" aria-label="Running processes">
      <ul class="process-fold-jobs">${work.map((job) => jobView(job, now, stopping, onStop))}</ul>
    </div>
  `;
}

export function processFoldView({ work, open, now, stopping }, { onToggle, onStop }) {
  if (work.length === 0) return nothing;
  let list = nothing;
  if (open) list = listView(work, now, stopping, onStop);
  return html`
    <div class="process-fold">
      <button class="chip" aria-expanded=${String(open)} aria-haspopup="dialog" @click=${onToggle}>${chipWords(work)}</button>
      ${list}
    </div>
  `;
}
