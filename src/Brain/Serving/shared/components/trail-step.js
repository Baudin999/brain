// The trail step: one step of the story of a piece of work in brain code's window, and in its kinds in the Stylebook.
// It is the builder's report, a round's findings the validator sent the builder, the builder's answer to a round, or a
// validator's report, which says so when it landed the work. Its round is the server's, never counted here. The words
// come drawn, as the markdown component draws them; under them, the subjects of the commits the step made, or put on
// the default branch when it landed.
//
// The step is the server's shape: { kind, agent, at, text, commits, landed, round }.
import { html, nothing } from '/lit-html.js';

// The step's title in words, with its round.
function titleOf(step) {
  if (step.kind === 'report') return 'The builder’s report';
  if (step.kind === 'findings') return `Round ${step.round}: the validator’s findings, sent to the builder`;
  if (step.kind === 'answer') return `Round ${step.round}: the builder’s answer`;
  if (step.landed) return 'Landed: the validator’s report';
  return 'The validator’s report';
}

function timeOf(at) {
  if (!at) return '';
  return new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function commitsView(step) {
  if (step.commits.length === 0) return nothing;
  let label = 'Committed';
  if (step.landed) label = 'Now on the default branch';
  return html`
    <p class="trail-step-label">${label}</p>
    <ul class="trail-step-commits">${step.commits.map((commit) => html`<li>${commit.subject}</li>`)}</ul>
  `;
}

// One step, with its words already drawn as prose.
export function trailStepView(step, prose) {
  return html`
    <li class="trail-step" data-kind=${step.kind} data-landed=${step.landed ? 'true' : nothing}>
      <header class="trail-step-head">
        <h3 class="trail-step-title">${titleOf(step)}</h3>
        <span class="trail-step-when">${timeOf(step.at)}</span>
      </header>
      ${prose}
      ${commitsView(step)}
    </li>
  `;
}
