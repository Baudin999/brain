// The question: what an agent asks the user through brain ask, drawn in brain code's window where it was asked and on
// its page of open questions, and in its states in the Stylebook. One ask holds one or more questions and takes one
// answer for all of them. Each question's text is markdown, through the one markdownView. Its options are choices, the
// recommended one marked, with the agent's reason under them; one choice is pressed at a time, or several when the
// question is multiple. A field always takes the user's own words, beside an option or alone; with no options the
// recommendation reads as the agent's own answer. Answer sends every question's answer at once, and only once each
// question has one; Close shuts the ask unanswered, so the user types his own message instead, and the agent hears he
// closed it. Open, it is answerable; answering, its answer is on its way; answered, it shows what he chose and said;
// closed, he closed it unanswered; ended, its session ended before he answered.
//
// The ask is the server's shape of it: { id, wait, questions: [{ text, options: [{ label, description }], recommend,
// why, multiple, own }], state, replies: [{ chosen, words }] }. Component state: the choices and words given so far for
// each ask, by the key its window gives it, kept across redraws, and each question's drawn markdown.
import { html, nothing } from '/lit-html.js';
import { markdownView } from '/components/markdown.js';

const given = new Map();
const drawn = new Map();

// The answers given so far to the ask of key, one { chosen, words } for each of its questions.
function answersOf(key, asked) {
  if (!given.has(key)) given.set(key, asked.questions.map(() => ({ chosen: [], words: '' })));
  return given.get(key);
}

function prose(key, index, text) {
  const at = `${key} ${index}`;
  if (!drawn.has(at)) drawn.set(at, markdownView(text));
  return drawn.get(at);
}

function hasOptions(question) {
  return Array.isArray(question.options) && question.options.length > 0;
}

function isAnswered(answer) {
  if (answer.chosen.length > 0) return true;
  return answer.words.trim().length > 0;
}

// Presses the option label, or lets it go when it was pressed; a question that is not multiple keeps one pressed.
function toggled(answer, question, label, onChange) {
  if (answer.chosen.includes(label)) {
    answer.chosen = answer.chosen.filter((one) => one !== label);
  } else if (question.multiple) {
    answer.chosen = [...answer.chosen, label];
  } else {
    answer.chosen = [label];
  }
  onChange();
}

function typed(answer, event, onChange) {
  answer.words = event.target.value;
  onChange();
}

function choiceView(question, option, answer, open, onChange) {
  const pressed = answer.chosen.includes(option.label);
  const recommended = option.label === question.recommend;
  return html`
    <button class="question-choice" type="button" aria-pressed=${pressed ? 'true' : 'false'} ?disabled=${!open}
      @click=${() => toggled(answer, question, option.label, onChange)}>
      <span class="question-choice-label">${option.label}</span>
      ${recommended ? html`<span class="question-choice-mark">recommended</span>` : nothing}
      ${option.description ? html`<span class="question-choice-description">${option.description}</span>` : nothing}
    </button>
  `;
}

function choicesView(question, answer, open, onChange) {
  if (!hasOptions(question)) return nothing;
  return html`<div class="question-choices" role="group">${question.options.map((option) => choiceView(question, option, answer, open, onChange))}</div>`;
}

// The agent's recommendation and why: beside the options, its reason; with none, its own answer and its reason.
function whyView(question) {
  if (hasOptions(question)) {
    return html`<p class="question-why"><span class="question-why-head">Why ${question.recommend}:</span> ${question.why}</p>`;
  }
  return html`<p class="question-why"><span class="question-why-head">The agent would answer:</span> ${question.recommend}. ${question.why}</p>`;
}

function wordsView(question, answer, open, onChange) {
  if (!open) {
    if (!answer.words) return nothing;
    return html`<p class="question-words-given">${answer.words}</p>`;
  }
  let placeholder = 'Your own words, if the options miss it';
  if (!hasOptions(question)) placeholder = 'Your answer';
  return html`<textarea class="field question-words" placeholder=${placeholder} .value=${answer.words}
    @input=${(event) => typed(answer, event, onChange)}></textarea>`;
}

function itemView(key, question, index, answer, open, onChange) {
  return html`
    <section class="question-item">
      <div class="question-text">${prose(key, index, question.text)}</div>
      ${choicesView(question, answer, open, onChange)}
      ${whyView(question)}
      ${wordsView(question, answer, open, onChange)}
    </section>
  `;
}

// Sends the answers; once the server took them, the answers given so far go, since the ask now shows his own.
async function sent(key, answers, onAnswer) {
  const replies = answers.map((answer) => ({ chosen: answer.chosen, words: answer.words.trim() }));
  const took = await onAnswer(replies);
  if (took) given.delete(key);
}

// Closes the ask unanswered; the answers given so far go with it.
async function closed(key, onClose) {
  const took = await onClose();
  if (took) given.delete(key);
}

function footView(state, key, answers, { onAnswer, onClose }) {
  if (state === 'answered') return html`<p class="question-note">Answered</p>`;
  if (state === 'closed') return html`<p class="question-note">Closed without an answer</p>`;
  if (state === 'ended') return html`<p class="question-note">The session ended before you answered</p>`;
  const complete = answers.every(isAnswered);
  let note = 'Answer every question first';
  if (complete) note = '';
  return html`
    <div class="question-foot">
      <button class="button yes" ?disabled=${!complete || state === 'answering'} @click=${() => sent(key, answers, onAnswer)}>Answer</button>
      <button class="button" ?disabled=${state === 'answering'} @click=${() => closed(key, onClose)}>Close</button>
      ${note ? html`<span class="question-note">${note}</span>` : nothing}
    </div>
  `;
}

// The answers to draw: the user's own once the ask is answered, else those given so far.
function shownAnswers(key, asked) {
  if (asked.state !== 'answered') return answersOf(key, asked);
  return asked.replies.map((reply) => ({ chosen: reply.chosen || [], words: reply.words || '' }));
}

// One ask as a question. key names it among every ask the window draws; answering marks one whose answer is on its
// way. onChange() redraws once a choice or a word changed, and onAnswer(replies) sends the answers and resolves to
// whether the server took them. onClose() closes the ask unanswered and resolves to whether the server took that.
export function questionView(asked, { key, answering = false, onChange, onAnswer, onClose }) {
  let state = asked.state;
  if (state === 'open' && answering) state = 'answering';
  const open = state === 'open';
  const answers = shownAnswers(key, asked);
  let kind = 'At your leisure: the agent goes on meanwhile';
  if (asked.wait) kind = 'The agent waits for your answer';
  return html`
    <article class="question" data-state=${state} data-question=${asked.id} aria-label="Question">
      <header class="question-head">${kind}</header>
      ${asked.questions.map((question, index) => itemView(key, question, index, answers[index], open, onChange))}
      ${footView(state, key, answers, { onAnswer, onClose })}
    </article>
  `;
}
