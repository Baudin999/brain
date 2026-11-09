// The agent flow: the agents a session sent drawn as a diagram, as brain code's window shows them beside a
// conversation and the Stylebook shows its parts. It starts at the open agent, the session itself, and a trunk runs
// down from it with a branch to each piece of work and to each agent that is part of none.
//
// A piece of work is a sequence of two lifelines, the builder's on the left and its validator's on the right. Each
// agent and each readiness check is a block on its lifeline at the time it was sent; a check is a gate, a diamond, in
// its verdict's color, and says when it asks questions. Between the lifelines, each step that passed between the agents
// is a line whose arrow points the way the information went: the builder's report and each answer to the validator,
// each round of findings back to the builder. Where the builder's worktree was opened, and where it was removed, is a
// mark on its lifeline. The work ends in the landing, with the range it moved the default branch by and its commits,
// or says where it stands; under it, the Navarone signals the work touched, counted by how they ended.
//
// A click opens what the list opens: a block the agent's own view, a line the view of the agent that wrote it at that
// step, a worktree mark or the signals the builder's view, the landing the validator's at its report. Nothing here
// keeps state: the flow is drawn from what the server says, and a new block or line moves nothing else.
//
// Each agent is the server's shape: { agent, tool, type, kind, description, state, started, ended, work, verdict,
// attached, asks, now, worktree, steps, signals }; a builder's worktree is { path, opened, removed, removedAt }.
import { html, nothing } from '/lit-html.js';
import { kindLabel, ranFor } from '/components/agent-row.js';
import { stepKey } from '/components/trail-step.js';

// The ends of a Navarone signal, in the order the count says them: what still needs someone first.
const SIGNAL_ENDS = [
  ['clash', 'waiting for a judge'],
  ['replaced', 'replaced'],
  ['open', 'not answered'],
  ['answered', 'answered'],
  ['large', 'large'],
  ['judged', 'judged'],
  ['kept', 'kept'],
  ['fixed', 'fixed'],
];

// How many of the folders of a worktree's path a mark shows; the whole path is its title.
const PATH_TAIL = 3;

// ---- What the flow is made of ----

// The time something happened at, in ms; something with no time is last.
function timeOf(at) {
  const time = Date.parse(at);
  if (Number.isNaN(time)) return Infinity;
  return time;
}

// The branches of the trunk, in the order the first agent of each was sent: each builder's work, with its checks, its
// validators and theirs; and each agent of no work, with its checks.
function flowBranches(agents) {
  const byTool = new Map(agents.map((sent) => [sent.tool, sent]));
  const checks = new Map();
  for (const sent of agents) {
    if (sent.kind !== 'ready' || !byTool.has(sent.attached)) continue;
    if (!checks.has(sent.attached)) checks.set(sent.attached, []);
    checks.get(sent.attached).push(sent);
  }
  const builders = new Map();
  for (const sent of agents) {
    if (sent.kind === 'builder') builders.set(sent.agent, { kind: 'work', builder: sent, checks: checks.get(sent.tool) || [], validators: [] });
  }
  const branches = [];
  for (const sent of agents) {
    const of = checks.get(sent.tool) || [];
    if (sent.kind === 'builder') {
      branches.push(builders.get(sent.agent));
      continue;
    }
    if (sent.kind === 'validator' && builders.has(sent.work)) {
      builders.get(sent.work).validators.push({ sent, checks: of });
      continue;
    }
    if (sent.kind === 'ready' && byTool.has(sent.attached)) continue;
    branches.push({ kind: 'lone', sent, checks: of });
  }
  return branches;
}

// The rows of a piece of work, in the order they happened: the blocks of each lifeline, the steps between them, and
// the worktree's marks. A row is { at, side, draws }: side is 'builder', 'validator' or 'both' for a step.
function workRows(work) {
  const rows = [];
  const builder = work.builder;
  for (const check of work.checks) rows.push({ at: timeOf(check.started), side: 'builder', draws: { check } });
  rows.push({ at: timeOf(builder.started), side: 'builder', draws: { agent: builder } });
  const tree = builder.worktree;
  if (tree) rows.push({ at: timeOf(tree.opened), side: 'builder', draws: { tree, opened: true } });
  for (const validator of work.validators) {
    for (const check of validator.checks) rows.push({ at: timeOf(check.started), side: 'validator', draws: { check } });
    rows.push({ at: timeOf(validator.sent.started), side: 'validator', draws: { agent: validator.sent } });
  }
  for (const step of builder.steps || []) {
    let side = 'both';
    if (step.kind === 'verdict') side = 'validator';
    rows.push({ at: timeOf(step.at), side, draws: { step } });
  }
  if (tree && tree.removed) rows.push({ at: timeOf(tree.removedAt), side: 'builder', draws: { tree, opened: false } });
  // A sort keeps the order rows of one time were pushed in: a block before what it did.
  rows.sort((a, b) => a.at - b.at);
  return rows;
}

// True for a row that is on the lifeline of side: its own blocks and marks, and every step, which joins both.
function touches(row, side) {
  return row.side === side || row.side === 'both';
}

// Where each row stands on the lifeline of side: 'start' where it begins, 'end' where it ends, 'on' between, 'off'
// outside. The lifeline of an agent still running runs to the last row.
function lifeline(rows, side, running) {
  const first = rows.findIndex((row) => row.side === side);
  let last = rows.findLastIndex((row) => touches(row, side));
  if (running) last = rows.length - 1;
  return rows.map((row, at) => {
    if (first < 0 || at < first || at > last || first === last) return 'off';
    if (at === first) return 'start';
    if (at === last && !running) return 'end';
    return 'on';
  });
}

// How many rounds of findings the work has had.
function roundsOf(work) {
  return (work.builder.steps || []).filter((step) => step.kind === 'findings').length;
}

function plural(count, word) {
  if (count === 1) return `1 ${word}`;
  return `${count} ${word}s`;
}

// Where the work stands, { end, words }: landed, in review, building, checking, failed, stopped or ended.
function workEnd(work) {
  const steps = work.builder.steps || [];
  const landing = steps.find((step) => step.landed);
  if (landing) return { end: 'landed', words: 'Landed', landing };
  const rounds = roundsOf(work);
  const validators = work.validators.map((one) => one.sent);
  const checks = [...work.checks, ...work.validators.flatMap((one) => one.checks)];
  if (validators.some((sent) => sent.state === 'running')) {
    if (rounds === 0) return { end: 'review', words: 'The validator is reviewing' };
    return { end: 'review', words: `The validator is in round ${rounds}` };
  }
  if (work.builder.state === 'running') {
    if (rounds === 0) return { end: 'building', words: 'The builder is building' };
    return { end: 'building', words: `The builder is answering round ${rounds}` };
  }
  if (checks.some((sent) => sent.state === 'running')) return { end: 'checking', words: 'A readiness check is running' };
  const all = [work.builder, ...validators];
  if (all.some((sent) => sent.state === 'failed')) return { end: 'failed', words: 'It failed before it landed' };
  if (all.some((sent) => sent.state === 'stopped')) return { end: 'stopped', words: 'It was stopped before it landed' };
  return { end: 'ended', words: 'It ended without landing' };
}

// ---- Drawing ----

function clockOf(at) {
  if (!at) return '';
  return new Date(at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// An agent's state and how long it ran, in words.
function stateWords(sent, now) {
  const ran = ranFor(sent, now);
  if (!ran) return sent.state;
  return `${sent.state} · ${ran}`;
}

// A readiness check: a gate in its verdict's color, and whether it asks questions.
function checkView(check, { open, onOpen }) {
  let verdict = check.verdict;
  if (!verdict) verdict = check.state;
  let asks = nothing;
  if (check.asks) asks = html`<span class="agent-flow-check-asks">asks questions</span>`;
  return html`
    <button class="agent-flow-check" data-verdict=${check.verdict || nothing} data-state=${check.state}
      aria-current=${check.tool === open ? 'true' : 'false'} @click=${() => onOpen(check.tool)}>
      <span class="agent-flow-gate" aria-hidden="true"></span>
      <span class="agent-flow-check-says">
        <span class="agent-flow-check-kind">Readiness check</span>
        <span class="agent-flow-check-verdict">${verdict}</span>
        ${asks}
      </span>
    </button>
  `;
}

// An agent: its kind and state, what it was sent to do, and while it runs what it does now.
function agentView(sent, { open, now, onOpen, described = true }) {
  let what = nothing;
  if (described) what = html`<span class="agent-flow-agent-what">${sent.description}</span>`;
  let doing = nothing;
  if (sent.now) doing = html`<span class="agent-flow-agent-now">${sent.now}</span>`;
  return html`
    <button class="agent-flow-agent" data-kind=${sent.kind} data-state=${sent.state}
      aria-current=${sent.tool === open ? 'true' : 'false'} @click=${() => onOpen(sent.tool)}>
      <span class="agent-flow-agent-head">
        <span class="agent-flow-agent-kind">${kindLabel(sent)}</span>
        <span class="agent-flow-agent-state">${stateWords(sent, now)}</span>
      </span>
      ${what}
      ${doing}
    </button>
  `;
}

// The last folders of a path, which tell one worktree from another.
function tailOf(path) {
  const folders = path.split('/').filter((folder) => folder.length > 0);
  if (folders.length <= PATH_TAIL) return path;
  return `…/${folders.slice(-PATH_TAIL).join('/')}`;
}

// A worktree's mark where it was opened or removed; a click opens the builder.
function treeView(tree, opened, builder, onOpen) {
  let words = 'Worktree removed';
  if (opened) words = 'Worktree opened';
  return html`
    <button class="agent-flow-tree" data-opened=${String(opened)} title=${tree.path} @click=${() => onOpen(builder.tool)}>
      <span class="agent-flow-tree-ring" aria-hidden="true"></span>
      <span class="agent-flow-tree-says">
        <span class="agent-flow-tree-words">${words}</span>
        <span class="agent-flow-tree-path">${tailOf(tree.path)}</span>
      </span>
    </button>
  `;
}

// What a step carried, in words.
function stepWords(step) {
  let commits = '';
  if (step.commits > 0) commits = ` · ${plural(step.commits, 'commit')}`;
  if (step.kind === 'report') return `Report${commits}`;
  if (step.kind === 'findings') return `Round ${step.round} · findings`;
  if (step.kind === 'answer') return `Answer to round ${step.round}${commits}`;
  if (step.landed) return 'Landed the work';
  return 'Verdict';
}

// A step between the lifelines, its arrow the way it went: to the validator, or back to the builder. A validator's
// verdict stays on its own lifeline.
function stepView(step, onOpen) {
  let way = 'forth';
  if (step.kind === 'findings') way = 'back';
  if (step.kind === 'verdict') way = 'own';
  return html`
    <button class="agent-flow-step" data-kind=${step.kind} data-way=${way} data-landed=${step.landed ? 'true' : nothing}
      @click=${() => onOpen(step.tool, stepKey(step))}>
      <span class="agent-flow-step-words">${stepWords(step)}</span>
      <span class="agent-flow-step-when">${clockOf(step.at)}</span>
    </button>
  `;
}

function rowContent(row, work, shown) {
  const draws = row.draws;
  if (draws.check) return checkView(draws.check, shown);
  if (draws.agent) return agentView(draws.agent, { ...shown, described: draws.agent.kind !== 'builder' });
  if (draws.tree) return treeView(draws.tree, draws.opened, work.builder, shown.onOpen);
  return stepView(draws.step, shown.onOpen);
}

function rowView(row, builderLine, validatorLine, work, shown) {
  let kind = 'block';
  if (row.draws.step && row.side === 'both') kind = 'step';
  return html`
    <li class="agent-flow-row" data-side=${row.side} data-row=${kind} data-builder=${builderLine} data-validator=${validatorLine}>
      ${rowContent(row, work, shown)}
    </li>
  `;
}

// The Navarone signals of the work, counted by how they ended; nothing when it touched none.
function signalsView(work, onOpen) {
  const signals = work.builder.signals;
  if (!signals || !signals.read || signals.all.length === 0) return nothing;
  const parts = [];
  for (const [ended, words] of SIGNAL_ENDS) {
    const count = signals.all.filter((signal) => signal.ended === ended).length;
    if (count > 0) parts.push(html`<span class="agent-flow-signal" data-ended=${ended}>${count} ${words}</span>`);
  }
  return html`
    <button class="agent-flow-signals" @click=${() => onOpen(work.builder.tool)}>
      <span class="agent-flow-signals-title">Navarone ${plural(signals.all.length, 'signal')}</span>
      ${parts}
    </button>
  `;
}

// Where the work ends: the landing, with its range and commits, which opens the validator's report; or where it
// stands, in words.
function endView(work, onOpen) {
  const end = workEnd(work);
  if (end.end !== 'landed') {
    return html`<p class="agent-flow-end" data-end=${end.end}><span class="agent-flow-end-words">${end.words}</span></p>`;
  }
  const landing = end.landing;
  let commits = nothing;
  if (landing.commits > 0) commits = html`<span class="agent-flow-end-commits">${plural(landing.commits, 'commit')}</span>`;
  return html`
    <button class="agent-flow-end" data-end="landed" @click=${() => onOpen(landing.tool, stepKey(landing))}>
      <span class="agent-flow-end-words">Landed on the default branch</span>
      <span class="agent-flow-end-range">${landing.landed}</span>
      ${commits}
    </button>
  `;
}

// A piece of work: what it was sent to build, its two lifelines and what passed between them, and its end.
function workView(work, shown) {
  const rows = workRows(work);
  const builderRunning = work.builder.state === 'running';
  const validatorRunning = work.validators.some((one) => one.sent.state === 'running');
  const builderLine = lifeline(rows, 'builder', builderRunning);
  const validatorLine = lifeline(rows, 'validator', validatorRunning);
  const end = workEnd(work);
  return html`
    <section class="agent-flow-work" data-end=${end.end} aria-label=${`Work: ${work.builder.description}`}>
      <header class="agent-flow-work-head">
        <h3 class="agent-flow-work-title">${work.builder.description}</h3>
        <span class="agent-flow-work-state">${end.words}</span>
      </header>
      <ol class="agent-flow-rows">${rows.map((row, at) => rowView(row, builderLine[at], validatorLine[at], work, shown))}</ol>
      ${endView(work, shown.onOpen)}
      ${signalsView(work, shown.onOpen)}
    </section>
  `;
}

// An agent of no work, with the checks of it.
function loneView(lone, shown) {
  let block = agentView(lone.sent, shown);
  if (lone.sent.kind === 'ready') block = checkView(lone.sent, shown);
  return html`
    <div class="agent-flow-lone">
      ${lone.checks.map((check) => checkView(check, shown))}
      ${block}
    </div>
  `;
}

function branchView(branch, shown) {
  if (branch.kind === 'work') return html`<li class="agent-flow-branch" data-branch="work">${workView(branch, shown)}</li>`;
  return html`<li class="agent-flow-branch" data-branch="lone">${loneView(branch, shown)}</li>`;
}

// The flow of the agents a session sent, from the open agent named root; open is the call id of the agent open, or
// null, now the time a running agent's run is counted to, and onOpen(tool, step) opens an agent, at a step by its key.
export function agentFlowView(agents, { root, open = null, now = Date.now(), onOpen }) {
  const shown = { open, now, onOpen };
  return html`
    <div class="agent-flow">
      <p class="agent-flow-root"><span class="agent-flow-root-kind">Open agent</span><span class="agent-flow-root-name">${root}</span></p>
      <ol class="agent-flow-branches">${flowBranches(agents).map((branch) => branchView(branch, shown))}</ol>
    </div>
  `;
}
