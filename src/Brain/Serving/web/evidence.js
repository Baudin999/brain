// Evidence: what brain prevented, told as a story at the bottom of the strip. Each line says what would have happened
// without one part of brain, a fixed sentence brain serve fills from the usage log, and under it every caught event it
// counted, which is its proof; a Navarone problem's case opens its story in the Navarone tab. The cost closes it, an
// estimate that says so. The lines come from the same metrics brain stats prints, over all time or the days picked in
// the queue column, counted again each time the tab is shown and each time the days change. Every line starts folded
// and unfolds to its latest 3 cases; a button opens a details page with all of them, and Esc goes back. The usage log
// keeps no project, so the project focus does not filter it. It reads and changes nothing.

const EVIDENCE = ':evidence';
// An unfolded line shows this many cases, the latest by time; its details page lists them all.
const CASES_SHOWN = 3;
// A validator round unfolds to this many non-empty lines of what the validator wrote.
const ROUND_LINES = 3;
const evidence = {
  // The report brain serve sent: { heading, unreadable, sections }, null while it loads.
  report: null,
  // The days counted, yyyy-mm-dd or '' for open.
  since: '',
  until: '',
  // The titles of the lines whose cases are unfolded; every line starts folded.
  open: new Set(),
  // The title of the line whose details page is in the main pane, null for the lines.
  detail: null,
};

async function loadEvidence() {
  const query = new URLSearchParams();
  if (evidence.since) query.set('since', evidence.since);
  if (evidence.until) query.set('until', evidence.until);
  const { ok, body } = await call(`/api/evidence?${query}`);
  if (!ok) {
    toast(body.error);
    return;
  }
  evidence.report = body;
  if (state.inbox === EVIDENCE) renderMain();
}

function countDays(since, until) {
  evidence.since = since;
  evidence.until = until;
  renderQueue();
  loadEvidence();
}

function dayInput(label, value, change) {
  return h(
    'label',
    { class: 'evidence-day' },
    h('span', {}, label),
    h('input', { class: 'field', type: 'date', value, onchange: (event) => change(event.target.value) }),
  );
}

function evidencePeriod() {
  return h(
    'div',
    { class: 'evidence-period' },
    dayInput('Since', evidence.since, (since) => countDays(since, evidence.until)),
    dayInput('Until', evidence.until, (until) => countDays(evidence.since, until)),
    h('span', { class: 'evidence-period-acts' }, h('button', { class: 'link', disabled: !evidence.since && !evidence.until, onclick: () => countDays('', '') }, 'All time')),
  );
}

// A validator's round shows the start of what it wrote, its first lines; the details page shows all the text brain
// recorded. A round recorded before the text was kept says so.
function roundText(text, full) {
  if (!text) return 'Nothing was recorded of what the validator wrote in this round.';
  return full ? text : text.split('\n').filter((line) => line.trim()).slice(0, ROUND_LINES).join('\n');
}

function caseRounds(rounds, full) {
  return rounds.map((text, index) =>
    h('div', { class: 'evidence-round' }, h('span', { class: 'evidence-detail' }, `Round ${index + 1}`), h('pre', { class: 'evidence-text' }, roundText(text, full))),
  );
}

// One case: its subject and detail, the validator's rounds when it has them, and for a Navarone problem a link to its
// story in the Navarone tab.
function caseItem(one, full) {
  const parts = [h('span', { class: 'evidence-subject' }, one.subject), h('span', { class: 'evidence-detail' }, one.detail)];
  if (one.rounds) parts.push(caseRounds(one.rounds, full));
  if (one.problem) parts.push(h('span', {}, h('button', { class: 'link', onclick: () => openNavaroneStory(one.problem) }, 'Open its story in Navarone')));
  return h('li', {}, ...parts);
}

function caseItems(cases, full = false) {
  return h('ol', { class: 'evidence-cases' }, cases.map((one) => caseItem(one, full)));
}

function caseCount(cases) {
  return cases.length === 1 ? '1 case' : `${cases.length} cases`;
}

// The cases arrive in the order they happened, so the latest are the last ones; the newest shows first.
function latestFirst(cases) {
  return [...cases].reverse();
}

function evidenceCases(section) {
  const cases = section.cases;
  if (!cases.length) return h('p', { class: 'evidence-none' }, 'No case in this period.');
  const shown = cases.length > CASES_SHOWN ? `latest ${CASES_SHOWN} of ${cases.length} cases` : caseCount(cases);
  const toggle = (event) => {
    if (event.target.open) evidence.open.add(section.title);
    else evidence.open.delete(section.title);
  };
  // The button sits outside the details, so a folded line opens its page in one step.
  return [
    h(
      'details',
      { class: 'evidence-proof', open: evidence.open.has(section.title), ontoggle: toggle },
      h('summary', { class: 'link subtle' }, shown),
      caseItems(latestFirst(cases).slice(0, CASES_SHOWN)),
    ),
    h('p', { class: 'evidence-open' }, h('button', { class: 'link', onclick: () => showEvidenceDetail(section.title) }, 'Open all the cases')),
  ];
}

function evidenceLine(section) {
  if (!section.cases) return h('p', { class: 'evidence-cost' }, section.story);
  const parts = [h('p', { class: 'evidence-story' }, section.story)];
  if (section.note) parts.push(h('p', { class: 'evidence-note' }, section.note));
  parts.push(evidenceCases(section));
  return h('section', { class: 'evidence-line' }, ...parts);
}

// A line of evidence for a chat: its story and its cases, every one when the details page is open.
function evidenceText(section, all) {
  const cases = latestFirst(section.cases || []).slice(0, all ? undefined : CASES_SHOWN);
  const rounds = (one) => (one.rounds || []).map((text, index) => `  Round ${index + 1}: ${roundText(text, true)}`);
  return [section.story, ...cases.map((one) => [`- ${one.subject}: ${one.detail}`, ...rounds(one)].join('\n'))].join('\n');
}

// What the tab shows for a chat: the details page when it is open, else the lines unfolded to their cases; none when
// nothing is open.
function evidenceContext() {
  const report = evidence.report;
  const detail = report?.sections.find((section) => section.cases && section.title === evidence.detail);
  const open = detail ? [detail] : report?.sections.filter((section) => section.cases && evidence.open.has(section.title)) || [];
  if (!open.length) return null;
  return [`The user has this evidence open, ${report.heading}.`, ...open.map((section) => evidenceText(section, Boolean(detail)))].join('\n\n');
}

function showEvidenceDetail(title) {
  evidence.detail = title;
  renderMain();
  $('main').focus();
}

function closeEvidenceDetail() {
  evidence.detail = null;
  renderMain();
  $('main').focus();
}

function evidenceDetail(report, section) {
  return h(
    'div',
    { class: 'page' },
    h('button', { class: 'link subtle back', onclick: closeEvidenceDetail }, keys('Esc'), 'Back to Evidence'),
    h('p', { class: 'doc-meta' }, report.heading),
    h('p', { class: 'evidence-story' }, section.story),
    section.note ? h('p', { class: 'evidence-note' }, section.note) : null,
    h('p', { class: 'doc-meta' }, `${caseCount(section.cases)}, latest first`),
    section.cases.length ? caseItems(latestFirst(section.cases), true) : h('p', { class: 'evidence-none' }, 'No case in this period.'),
  );
}

function renderEvidenceMain(main) {
  const report = evidence.report;
  if (!report) {
    fill(main, h('div', { class: 'quiet' }, h('h2', {}, 'Evidence'), h('p', {}, 'Counting the usage log.')));
    return;
  }
  const detail = report.sections.find((section) => section.cases && section.title === evidence.detail);
  if (detail) {
    fill(main, evidenceDetail(report, detail));
    return;
  }
  fill(main,
    h(
      'div',
      { class: 'page' },
      h('p', { class: 'doc-meta' }, report.heading),
      report.unreadable ? h('p', { class: 'doc-meta' }, `${report.unreadable} lines of the usage log could not be read and are left out.`) : null,
      h('h1', { class: 'doc-title' }, 'What brain prevented'),
      report.sections.filter((section) => section.story).map(evidenceLine),
    ),
  );
}

registerInbox({
  name: EVIDENCE,
  label: 'Evidence',
  ownSide: true,
  bottom: true,
  // Evidence is a record to read, not work that waits for the user.
  count: () => 0,
  start: loadEvidence,
  shown: loadEvidence,
  list: evidencePeriod,
  render: renderEvidenceMain,
  context: evidenceContext,
});
