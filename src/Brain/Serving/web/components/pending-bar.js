// The pending bar: the projects that have something waiting, each as a button with its chip and the node reviews and
// disputes that wait for it. A project with nothing waiting is not given to it, and with none it draws nothing. Each
// project is { name, chip, reviews, disputes }, the chip a node the caller made; a press runs onPick(name).
import { html, nothing } from '/lit-html.js';
import { waitingCounts } from '/components/waiting-counts.js';

export function pendingBar({ projects, onPick }) {
  if (projects.length === 0) {
    return nothing;
  }
  return html`
    <nav class="pending-bar" aria-label="Waiting for you">
      ${projects.map((project) => html`
        <button class="row compact pending-bar-item" title=${`Focus on ${project.name}`} @click=${() => onPick(project.name)}>
          ${project.chip}
          ${waitingCounts(project)}
        </button>`)}
    </nav>`;
}
