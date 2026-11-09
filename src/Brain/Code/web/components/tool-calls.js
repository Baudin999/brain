// The tool calls of a conversation as the window holds them: a call's shape, drawn as a line or a run in the
// conversation, and the information panel that shows one call whole. The panel shows the call the user clicked, found by
// its id among the events read of the conversation it is in.
import { infoPanelView } from '/components/info-panel.js';
import { toolDetailView } from '/components/tool-detail.js';
import { toolCallKind, toolCallName } from '/components/tool-call.js';

// What a result is now: running until it came, then done or failed.
function stateOf(result) {
  if (!result) return 'running';
  if (result.failed) return 'failed';
  return 'done';
}

// A tool event and its result event, or null while it runs, as one call.
export function callOf(event, result) {
  return { id: event.id, name: event.name, subject: event.subject, brain: event.brain || null, input: event.input || {}, state: stateOf(result) };
}

// The panel: the call with the id given in the events of the conversation, or null when there is none; onClose closes it.
export function toolPanelView(events, id, { onClose }) {
  const event = events.find((one) => one.kind === 'tool' && one.id === id);
  if (!event) return null;
  const result = events.find((one) => one.kind === 'result' && one.id === id) || null;
  const call = callOf(event, result);
  return infoPanelView({ title: call.subject || toolCallName(call), kind: toolCallKind(call) }, toolDetailView(call, result), { onClose });
}
