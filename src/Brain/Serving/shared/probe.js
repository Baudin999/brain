// The probe: a small overlay over the page, like a game engine's, that shows how the page runs. brain's window shows its
// frame times in it, from perf.js, and a workspace the commands it ran and the work its server did. It draws bars over
// the last 5 seconds, each at its time and as tall as its milliseconds, over a line at each mark the caller names, and
// beside them the caller's lines of text. It only watches and changes nothing it measures.
'use strict';

const PROBE_SPAN_MS = 5000;

// Opens the probe over the page: top is the milliseconds at the graph's top, marks the milliseconds it rules a line at.
function probeOpen(top, marks) {
  const graph = document.createElement('canvas');
  graph.className = 'probe-graph';
  graph.setAttribute('aria-hidden', 'true');
  const text = document.createElement('pre');
  text.className = 'probe-text';
  const overlay = document.createElement('div');
  overlay.className = 'probe';
  overlay.append(graph, text);
  document.body.append(overlay);
  const ratio = window.devicePixelRatio || 1;
  graph.width = Math.round(graph.clientWidth * ratio);
  graph.height = Math.round(graph.clientHeight * ratio);
  const colors = { ink: resolved('--ink'), line: resolved('--muted'), late: resolved('--no') };
  return { overlay, graph, text, top, marks, colors };
}

function probeClose(probe) {
  probe.overlay.remove();
}

// Draws bars, each [at, ms, late] with at on performance.now()'s clock, a late one in the no color, and the lines of text.
function probeDraw(probe, bars, lines) {
  const g = probe.graph.getContext('2d');
  const { width, height } = probe.graph;
  const now = performance.now();
  g.clearRect(0, 0, width, height);
  g.fillStyle = probe.colors.line;
  for (const ms of probe.marks) {
    const y = Math.round(height - (ms / probe.top) * height);
    g.fillRect(0, y, width, 1);
  }
  const bar = Math.max(1, Math.round(width / 300));
  for (const [at, ms, late] of bars) {
    const x = Math.round(width - ((now - at) / PROBE_SPAN_MS) * width);
    const tall = Math.min(height, Math.max(1, Math.round((ms / probe.top) * height)));
    g.fillStyle = probe.colors.ink;
    if (late) g.fillStyle = probe.colors.late;
    g.fillRect(x - bar, height - tall, bar, tall);
  }
  probe.text.textContent = lines.join('\n');
}
