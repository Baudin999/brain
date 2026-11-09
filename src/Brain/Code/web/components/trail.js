// The trail: one piece of work told top to bottom as a story of rounds, each step the shared trail step. The builder's
// report comes first; then each round, the findings a validator sent the builder and the builder's answer with the
// commits it made; then each validator's own report, and the landing with the commits it put on the default branch.
// The words of every step are drawn as their agent wrote them, as markdown, through the one markdownView; each step's
// round is the server's. Hashes and ids are not drawn here: the page keeps them in its one details fold, from
// trailDetails.
//
// Component state: the drawn markdown of each step's words, kept by the step, so a live read draws only what is new.
import { html, nothing } from '/lit-html.js';
import { markdownView } from '/components/markdown.js';
import { trailStepView } from '/components/trail-step.js';

const drawn = new Map();

// The drawn markdown of text, kept under key while its text stays the same.
export function proseOf(key, text) {
  const known = drawn.get(key);
  if (known && known.text === text) return known.prose;
  const prose = markdownView(text);
  drawn.set(key, { text, prose });
  return prose;
}

// How far the work came, in one sentence: what leads the trail.
export function trailLead(trail) {
  const rounds = trail.steps.filter((step) => step.kind === 'findings').length;
  let counted = `${rounds} rounds`;
  if (rounds === 1) counted = '1 round';
  if (trail.steps.some((step) => step.landed)) return `The work landed after ${counted} of findings.`;
  if (rounds > 0) return `The validator sent ${counted} of findings; the work has not landed.`;
  if (trail.steps.length > 0) return 'The builder reported; no validator has sent findings yet.';
  return 'The builder has not reported yet.';
}

// The steps of a trail, in the order they happened.
export function trailView(trail) {
  if (trail.steps.length === 0) return nothing;
  const steps = trail.steps.map((step) => trailStepView(step, proseOf(`${step.agent}|${step.kind}|${step.at}`, step.text)));
  return html`<ol class="code-trail">${steps}</ol>`;
}

// The hashes of the trail's commits and the range of its landing, for the page's details fold.
export function trailDetails(trail) {
  const commits = trail.steps.flatMap((step) => step.commits);
  const ranges = trail.steps.filter((step) => step.landed).map((step) => step.landed);
  return html`
    <p>Builder ${trail.builder}</p>
    ${ranges.map((range) => html`<p>Landed ${range}</p>`)}
    ${commits.map((commit) => html`<div class="git-commit"><span class="git-commit-hash">${commit.hash}</span><span class="git-commit-subject">${commit.subject}</span></div>`)}
  `;
}
