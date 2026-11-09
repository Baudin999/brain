// The Navarone signal: one signal a piece of work touched and how it ended, as brain code's trail tells it and the
// Stylebook shows each end. What leads is the end in words: fixed, answered with a reason the validator kept,
// replaced with the validator's finding, or a clash, where the two disagree, waiting for a judge; then the code it is
// on, and the voices in full: the builder's reason or the large change's text, the validator's finding, the user's
// judgement. A clash is never judged here.
//
// The signal is the server's shape: { id, check, name, ended, reason, finding, judgement, words }.
import { html, nothing } from '/lit-html.js';

const ENDS = {
  open: 'Not answered yet',
  fixed: 'Fixed',
  answered: 'Answered with a reason the validator has not read yet',
  kept: 'Answered with a reason the validator kept',
  replaced: 'Replaced with the validator’s finding',
  clash: 'The builder and the validator disagree: waiting for a judge',
  judged: 'Judged by the user',
  large: 'A large change, brought to the user',
};

function voice(who, words) {
  if (!words) return nothing;
  return html`<div class="navarone-signal-voice"><p class="navarone-signal-who">${who}</p><p class="navarone-signal-words">${words}</p></div>`;
}

function judgedView(signal) {
  if (signal.ended !== 'judged') return nothing;
  let words = `He chose ${signal.judgement}.`;
  if (signal.words) words = `He chose ${signal.judgement}: ${signal.words}`;
  return voice('The user', words);
}

function reasonWho(signal) {
  if (signal.ended === 'large') return 'The large change';
  return 'The builder’s reason';
}

export function navaroneSignalView(signal) {
  return html`
    <li class="navarone-signal" data-ended=${signal.ended}>
      <p class="navarone-signal-end">${ENDS[signal.ended] || signal.ended}</p>
      <p class="navarone-signal-on">${signal.name}</p>
      ${voice(reasonWho(signal), signal.reason)}
      ${voice('The validator’s finding', signal.finding)}
      ${judgedView(signal)}
    </li>
  `;
}
