// The probe: F3 shows the last few seconds of the workspace's records over the window, with the probe brain's window
// shows its frame times in. Each command and each piece of the server's work is a bar at its time, as tall as it took,
// over lines at the budgets: a frame, 100 ms for a file and 200 ms for the tree; one over its budget is in the no
// colour. Beside the graph the last records read as lines. While it shows, it asks the server for its records once a
// second and draws at most ten times a second; it only watches and changes nothing it measures.
'use strict';

const PROBE_TOP_MS = 250;
const PROBE_MARKS = [FRAME_MS, 100, 200];
const PROBE_LINES = 8;
const PROBE_DRAW_MS = 100;
const PROBE_READ_MS = 1000;

// The probe while it shows: the overlay, its timers, and the number of the server's last record it read.
const probing = { overlay: null, drawer: null, reader: null, seen: 0 };

async function probeRead() {
  const { ok, body } = await windowCall(`/api/records?after=${probing.seen}`).catch(() => ({ ok: false }));
  if (!ok) return;
  for (const kept of body.records) keepRecord(kept);
  probing.seen = body.last;
}

function probeLine(kept) {
  let line = `${kept.name.padEnd(18)} ${kept.source.padEnd(6)} ${kept.ms.toFixed(1).padStart(7)} ms`;
  if (kept.outcome !== 'ok') line += ` ${kept.outcome}`;
  if (kept.over) line += ` over ${Math.round(kept.budget)}`;
  return line;
}

function probeShow(ws) {
  const since = Date.now() - PROBE_SPAN_MS;
  const shift = Date.now() - performance.now();
  const recent = ws.records.filter((kept) => kept.at >= since);
  const bars = recent.map((kept) => [kept.at - shift, kept.ms, kept.over]);
  const over = recent.filter((kept) => kept.over).length;
  // The server's records arrive a second late, so the last ones go by their time.
  const last = [...ws.records].sort((a, b) => b.at - a.at).slice(0, PROBE_LINES);
  const lines = [`${recent.length} records in 5 s, ${over} over budget`, ...last.map(probeLine)];
  probeDraw(probing.overlay, bars, lines);
}

function probeToggle(ws) {
  ws.probe.open = !ws.probe.open;
  if (!ws.probe.open) {
    clearInterval(probing.drawer);
    clearInterval(probing.reader);
    probeClose(probing.overlay);
    probing.overlay = null;
    return;
  }
  probing.overlay = probeOpen(PROBE_TOP_MS, PROBE_MARKS);
  probing.drawer = setInterval(() => probeShow(ws), PROBE_DRAW_MS);
  probing.reader = setInterval(probeRead, PROBE_READ_MS);
  probeRead();
}

plugin({
  name: 'probe',
  state: { open: false },
  commands: [{ name: 'probe.toggle', title: 'Show or hide the probe', run: probeToggle }],
  keys: [{ key: 'F3', focus: '*', command: 'probe.toggle' }],
});
