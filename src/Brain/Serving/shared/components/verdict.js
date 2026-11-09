// The verdict: a readiness check's answer, READY or NOT READY, large in the yes or the no color, with the questions it
// asks under it, drawn as the markdown component draws them. brain code's agent page draws it, and the Stylebook
// shows both.
import { html, nothing } from '/lit-html.js';

// The verdict word and its questions, already drawn as prose, or null when it asks none.
export function verdictView(verdict, questions) {
  return html`
    <section class="verdict" data-verdict=${verdict}>
      <p class="verdict-word">${verdict}</p>
      ${questions || nothing}
    </section>
  `;
}
