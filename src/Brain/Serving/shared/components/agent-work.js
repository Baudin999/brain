// The pieces of work of the agents a session sent, the one grouping brain code's agents panel draws, as the list and as
// the flow alike. A builder heads one piece of work, which the validators the server says check it join; a readiness
// check joins the piece of the agent the server says it checked, and stands alone when none was sent after it. Any
// other agent is a piece of its own. A piece holds its agents in the order they were sent, and the pieces come in the
// order the first agent of each was sent.
//
// Each agent is the server's shape, of which this reads { agent, tool, kind, work, attached }.

// The key of the piece an agent is in: its builder's work, the piece of the agent a check checked, or its own.
function keyOf(sent, builders, byTool) {
  if (sent.kind === 'ready' && byTool.has(sent.attached)) return keyOf(byTool.get(sent.attached), builders, byTool);
  if (sent.work && builders.has(sent.work)) return `work|${sent.work}`;
  return sent.tool;
}

// The pieces of work of agents, each a list of its agents.
export function piecesOf(agents) {
  const builders = new Set(agents.filter((sent) => sent.kind === 'builder').map((sent) => sent.agent));
  const byTool = new Map(agents.map((sent) => [sent.tool, sent]));
  const pieces = new Map();
  for (const sent of agents) {
    const key = keyOf(sent, builders, byTool);
    if (!pieces.has(key)) pieces.set(key, []);
    pieces.get(key).push(sent);
  }
  return [...pieces.values()];
}
