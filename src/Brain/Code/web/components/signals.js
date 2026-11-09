// The signals: the Navarone signals one piece of work touched, each the shared Navarone signal, under a line that
// counts how they ended, what still needs someone first. A clash is judged where it is today, never here. The check and
// the id sit in the page's details fold, from signalsDetails. Without Navarone, or when the work touched no signal,
// nothing is drawn.
import { html, nothing } from '/lit-html.js';
import { navaroneSignalView } from '/components/navarone-signal.js';

// The ends the summary counts, in its order: what still needs someone first, with the words it counts each in.
const SUMMARY = [
  ['clash', 'waiting for a judge'],
  ['replaced', 'replaced'],
  ['open', 'not answered'],
  ['answered', 'answered'],
  ['large', 'large changes'],
  ['judged', 'judged'],
  ['kept', 'kept'],
  ['fixed', 'fixed'],
];

function summary(signals) {
  const parts = [];
  for (const [ended, words] of SUMMARY) {
    const count = signals.filter((signal) => signal.ended === ended).length;
    if (count > 0) parts.push(`${count} ${words}`);
  }
  return parts.join(' · ');
}

// The signals of a trail, or nothing when there are none to show.
export function signalsView(signals) {
  if (!signals.read || signals.all.length === 0) return nothing;
  return html`
    <section class="code-signals" aria-label="Navarone">
      <h2 class="code-signals-title">Navarone signals</h2>
      <p class="code-signals-sum">${summary(signals.all)}</p>
      <ul class="code-signal-list">${signals.all.map(navaroneSignalView)}</ul>
    </section>
  `;
}

// Each signal's id and check, for the page's details fold.
export function signalsDetails(signals) {
  return signals.all.map((signal) => html`<p>Navarone ${signal.id} ${signal.check} on ${signal.name}</p>`);
}
