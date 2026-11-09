// The conversation component: a session's events, or an agent's it sent, drawn in the order they were said. The
// conversation has two flows. The main flow is what the user and the agent say: the user's words and the agent's text,
// markdown through the one markdownView, so mermaid is drawn too; a call that sends a message, to another agent or as
// the agent's report, and a message another agent sent, as a validator's findings, both as the shared agent message;
// an agent the session sent, the shared agent row, in the state the server says, which opens its own view. The second
// flow is what the user inspects: the tool calls, the brain queries among them. Calls in a row with no main-flow event
// between them fold into one tool run, or stand as one tool call line when there is one; a click on a call asks the
// information panel to show it, and the call's input and output are never drawn here. A kind the user turned off in
// the settings is not drawn at all. The questions a session asked the
// user stand among the events, where they were asked. The events are brain's own shape of them, from the server, never
// text the component scrapes. The text the agent is writing now follows the events, drawn through the same markdownView
// as it grows; once it is an event, that event takes the very element it was drawn in, so the text neither jumps nor
// shows twice. A turn the user interrupted is a quiet line. The images the user's words carry are thumbnails under
// them, which a click opens in the image viewer; those a tool's result carries show in the information panel.
//
// Component state: the drawn markdown of each event, kept so a redraw never draws it, or its diagrams, again, and the
// live text last drawn with its element.
import { html, nothing } from '/lit-html.js';
import { markdownView } from '/components/markdown.js';
import { agentMessageView } from '/components/agent-message.js';
import { agentRowView } from '/components/agent-row.js';
import { toolCallView } from '/components/tool-call.js';
import { toolRunView } from '/components/tool-run.js';
import { callOf } from '/components/tool-calls.js';
import { imageThumbView } from '/components/image-thumb.js';

// The tools whose call sends a message: to another agent, or the agent's report to the one that sent it.
const MESSAGE_TOOLS = new Set(['SendMessage', 'SubagentHandback']);

const drawn = new WeakMap();
const live = { text: null, element: null };

function prose(event) {
  if (!drawn.has(event)) drawn.set(event, markdownView(event.text));
  return drawn.get(event);
}

// The agent's text: the element the live text was drawn in, when this is what it became.
function said(event) {
  if (!drawn.has(event) && event.text === live.text) {
    drawn.set(event, live.element);
    live.text = null;
    live.element = null;
  }
  return prose(event);
}

// The text the agent is writing now, drawn again only when it grew.
function liveView(text) {
  if (!text) return nothing;
  if (live.text !== text) {
    live.text = text;
    live.element = markdownView(text);
  }
  return html`<div class="code-said">${live.element}</div>`;
}

// The result of each tool call by the call's id.
function resultsOf(events) {
  const results = new Map();
  for (const event of events) {
    if (event.kind === 'result') results.set(event.id, event);
  }
  return results;
}

// The message a call sent, or nothing for a call that sends none.
function messageView(event) {
  if (!event.text) return nothing;
  return agentMessageView(null, prose(event));
}

// The images the user's words carry, as thumbnails, or nothing for words with none.
function imagesView(images) {
  if (!images || images.length === 0) return nothing;
  return html`<div class="code-images">${images.map((src) => imageThumbView(src))}</div>`;
}

// The user's words, or none when he only sent images.
function wordsView(event) {
  if (!event.text) return nothing;
  return prose(event);
}

// Whether a call of the second flow is drawn: the settings have its kind on.
function shown(event, kinds) {
  if (event.brain) return kinds.brain;
  return kinds.tools;
}

// The agent an Agent call sent, as the server lists it, or, before its meta is written, as the call names it.
function sentOf(event, agents) {
  const listed = agents.find((sent) => sent.tool === event.id);
  if (listed) return listed;
  return { tool: event.id, type: event.name, kind: 'other', description: event.subject || '', state: 'running' };
}

function eventView(event, agents, onAgent) {
  switch (event.kind) {
    case 'user':
      return html`<div class="user-message">${wordsView(event)}${imagesView(event.images)}</div>`;
    case 'text':
      return html`<div class="code-said">${said(event)}</div>`;
    case 'agent':
      return agentRowView(sentOf(event, agents), { onOpen: onAgent });
    case 'message':
      return agentMessageView(event.name, prose(event));
    case 'interrupted':
      return html`<p class="code-interrupted">Interrupted</p>`;
    default:
      return nothing;
  }
}

// Whether an event is a call of the second flow: a tool call that sends no message.
function isCall(event) {
  return event.kind === 'tool' && !MESSAGE_TOOLS.has(event.name);
}

// The events and the questions in the order they came: the questions of each brain ask after the events its session
// had when they were asked, and after every event when the window has read fewer than that.
function placed(events, questions) {
  const items = [];
  let next = 0;
  for (let index = 0; index < events.length; index++) {
    while (next < questions.length && questions[next].at <= index) {
      items.push({ asked: questions[next] });
      next++;
    }
    items.push({ event: events[index] });
  }
  while (next < questions.length) {
    items.push({ asked: questions[next] });
    next++;
  }
  return items;
}

// The items as blocks: the calls in a row, of a kind that is drawn, as one run, and every other item as it is. A call of
// a kind that is off is no block, and a result is none, so neither breaks a run; any main-flow item ends it.
function blocksOf(items, results, kinds) {
  const blocks = [];
  let run = null;
  for (const item of items) {
    const event = item.event;
    if (event?.kind === 'result') continue;
    if (event && isCall(event) && !shown(event, kinds)) continue;
    if (event && isCall(event)) {
      if (!run) {
        run = [];
        blocks.push({ run });
      }
      run.push(callOf(event, results.get(event.id)));
      continue;
    }
    run = null;
    blocks.push(item);
  }
  return blocks;
}

// A conversation's events as a list, with the agents the session sent, as the server lists them, and the text its
// agent is writing now after them; onAgent(tool) opens the agent an Agent call sent, by the call's id. The questions a
// session asked the user are drawn among the events by questionView(asked). kinds says which kinds of the second flow
// are drawn, { tools, brain }; selected is the id of the call the panel shows, and onCall(call) asks the panel to show a call.
export function conversationView({ events, live: writing }, agents, { onAgent, onCall, selected = null, kinds, questions = [], questionView = null }) {
  const blocks = blocksOf(placed(events, questions), resultsOf(events), kinds);
  const view = (block) => blockView(block, agents, { onAgent, onCall, selected, questionView });
  return html`<div class="code-events">${blocks.map(view)}${liveView(writing)}</div>`;
}

function blockView(block, agents, { onAgent, onCall, selected, questionView }) {
  if (block.asked) return questionView(block.asked);
  if (block.run) return runView(block.run, { onCall, selected });
  if (block.event.kind === 'tool') return messageView(block.event);
  return eventView(block.event, agents, onAgent);
}

function runView(calls, { onCall, selected }) {
  if (calls.length === 1) return toolCallView(calls[0], { selected: calls[0].id === selected, onOpen: onCall });
  return toolRunView(calls, { selected, onOpen: onCall });
}

function keepFocus(event) {
  event.preventDefault();
}

// The button over the bottom of a conversation that takes it to its newest event; it shows only while the conversation
// is away from its end. It stays in the page when hidden, so showing or hiding it moves no scroll. A press with the pointer
// never takes focus, so the composer keeps it; a button the keys reached and pressed loses focus to the page as it
// hides, since the view is then at its end.
export function toEndView(away, onToEnd) {
  return html`<button class="icon float code-to-end" ?hidden=${!away} aria-label="Scroll to the newest event" title="Scroll to the newest event" @mousedown=${keepFocus} @click=${onToEnd}><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M8 3v9M4 8.5l4 4 4-4"/></svg></button>`;
}
