// The tool run: calls in a row with no main-flow event between them, folded into one line that names what they did, as
// "Read 4 files, ran 2 commands", and that shows it opens with a chevron. Opened, it lists each call as a tool call line.
// It keeps no state: the browser keeps the fold open, and the panel's call is the caller's.
//
// The calls are brain's shape, as the tool call takes them.
import { html } from '/lit-html.js';
import { toolCallView } from '/components/tool-call.js';

// What a kind of call did, by its tool: a word for one, and for several, each followed by what it worked on.
const DOINGS = {
  Read: ['read', 'file', 'files'],
  Edit: ['edited', 'file', 'files'],
  MultiEdit: ['edited', 'file', 'files'],
  Write: ['wrote', 'file', 'files'],
  Bash: ['ran', 'command', 'commands'],
  Grep: ['searched', 'time', 'times'],
  Glob: ['searched', 'time', 'times'],
  WebFetch: ['fetched', 'page', 'pages'],
  WebSearch: ['searched the web', 'time', 'times'],
};

const BRAIN = ['asked brain', 'question', 'questions'];

function doingOf(call) {
  if (call.brain) return BRAIN;
  return DOINGS[call.name] || [`used ${call.name}`, 'time', 'times'];
}

// The calls in words, each kind of doing once, in the order they first came: "Read 4 files, ran 2 commands".
export function runWords(calls) {
  const counts = new Map();
  for (const call of calls) {
    const doing = doingOf(call);
    const key = doing[0];
    counts.set(key, { doing, count: (counts.get(key)?.count || 0) + 1 });
  }
  const parts = [];
  for (const { doing, count } of counts.values()) {
    if (count === 1) parts.push(`${doing[0]} 1 ${doing[1]}`);
    else parts.push(`${doing[0]} ${count} ${doing[2]}`);
  }
  const words = parts.join(', ');
  return words[0].toUpperCase() + words.slice(1);
}

// The run of calls; selected is the id of the call the panel shows, or null, and onOpen(call) opens a call there.
export function toolRunView(calls, { selected = null, onOpen }) {
  const failed = calls.filter((call) => call.state === 'failed').length;
  let count = `${calls.length} calls`;
  if (failed > 0) count = `${count}, ${failed} failed`;
  return html`
    <details class="tool-run" data-failed=${String(failed > 0)}>
      <summary class="tool-run-summary">
        <span class="tool-run-words">${runWords(calls)}</span>
        <span class="tool-run-count">${count}</span>
      </summary>
      <div class="tool-run-list">${calls.map((call) => toolCallView(call, { selected: call.id === selected, onOpen }))}</div>
    </details>
  `;
}
