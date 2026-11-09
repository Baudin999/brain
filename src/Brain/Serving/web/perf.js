// The window's perf: how every view of the window runs, recorded all the time and sent to brain serve's perf log, so a
// before and an after can be compared. Each second the window shows makes one row: the view shown, what moved in it,
// input, a glide of Home's view or a spark running on Home, the frames presented, the frame time p50, p95 and max, the
// dropped frames, those over 1.5 times the display's frame, taken as the 10th percentile of the last 120 frames, the
// long animation frames Chromium reports with their worst, the view's own paint time p50 and max where it has one, as
// Home's paint of the sky, the display frames the sparks painted, and which of Home's motion settings are on. brain
// serve adds the GPU's VRAM used and load to each row as it writes it.
//
// Measuring adds no load the window would not have. Frame times come from requestAnimationFrame, and its loop runs only
// while something moves: from the first input or animation until about a second after the last. While nothing moves no
// loop runs and the row says so with no frames. Long animation frames come from a PerformanceObserver, which costs
// nothing. A row is made once a second, while the window is visible, and the rows go to brain serve in one batch every
// ten seconds and when the window closes, never a request per row.
//
// F3 shows or hides the probe, the overlay probe.js draws, over every view: a small graph of the frame times of the last
// 5 seconds, live, with lines at 16.7 and 33 ms, and beside it the last second's numbers and the GPU's VRAM and load. It
// draws on a canvas at most 10 times a second, only when a frame or a row came since it last drew, and asks brain serve
// for the GPU once a second while it shows. It only watches and changes nothing it measures.
'use strict';

const PERF_ROW_MS = 1000;
const PERF_SEND_MS = 10000;
const PERF_IDLE_MS = 1000;
const PERF_DROP_RATIO = 1.5;
// The smallest frame interval believed, so a catch-up frame does not make every ordinary one a drop.
const PERF_MIN_FRAME_MS = 4;
const PERF_DISPLAY_FRAMES = 120;
// The most rows kept while brain serve cannot take them, and the most sent at once.
const PERF_KEPT_ROWS = 120;
// The frame time at the graph's top, and the two lines it marks: a frame at 60 and at 30 frames a second.
const PERF_GRAPH_TOP_MS = 50;
const PERF_GRAPH_LINES_MS = [1000 / 60, 1000 / 30];
const PERF_DRAW_MS = 100;
const PERF_GPU_MS = 1000;
const PERF_MOTION = ['sparks', 'glide'];

// A second of the window as it is measured: frame intervals, paints, long frames and repaints, and what moved.
function perfSecond() {
  return { presented: 0, frames: [], paints: [], longs: [], sparks: 0, moving: new Set() };
}

const perf = {
  second: perfSecond(),
  rows: [],
  // The last row made, which the overlay shows.
  shown: null,
  // While the frame loop runs: the time it stops unless something moves again, and the last frame's time.
  running: false,
  until: 0,
  last: 0,
  // Chromium also pauses the frames of a window that is covered, on another workspace or on a sleeping machine, and
  // tells the page nothing: the timers run on, but no frame comes. A row's second that passes with the frame loop
  // running and no frame marks such a pause, from pausedAt until the next frame. Its first frame then only restarts the
  // measurement, as after a hidden window, and the long animation frame that spans the pause is no long frame either.
  pausedAt: 0,
  pause: null,
  // The last frame intervals, for the display's frame, and the last 5 seconds of them as [time, interval] for the graph.
  display: [],
  recent: [],
  // The overlay while it shows, the probe, and its timers.
  overlay: null,
  drawer: null,
  gpuReader: null,
  gpu: null,
  changed: false,

  // Something moves: input, a glide or a spark. The frame loop runs until a second after the last.
  moving(what) {
    this.second.moving.add(what);
    this.until = performance.now() + PERF_IDLE_MS;
    if (this.running) return;
    this.running = true;
    this.last = 0;
    requestAnimationFrame((now) => this.step(now));
  },

  // A view's own paint took this long.
  paint(ms) {
    this.second.paints.push(ms);
  },

  // The sparks painted a display frame.
  spark() {
    this.second.sparks += 1;
    this.moving('spark');
  },

  step(now) {
    this.second.presented += 1;
    if (this.pausedAt) {
      this.pause = { from: this.pausedAt, to: now };
      this.pausedAt = 0;
      this.last = 0;
    }
    if (this.last) {
      const interval = now - this.last;
      this.second.frames.push(interval);
      this.display.push(interval);
      if (this.display.length > PERF_DISPLAY_FRAMES) this.display.shift();
      this.recent.push([now, interval]);
      while (this.recent[0][0] < now - PROBE_SPAN_MS) this.recent.shift();
      this.changed = true;
    }
    this.last = now;
    if (performance.now() > this.until) {
      this.running = false;
      return;
    }
    requestAnimationFrame((next) => this.step(next));
  },

  // Once a second: the second that ended becomes a row, unless the window was hidden, when it shows nothing.
  tick() {
    if (state.closed) return;
    const second = this.second;
    this.second = perfSecond();
    if (this.running && second.presented === 0 && !this.pausedAt) this.pausedAt = performance.now();
    if (document.hidden) return;
    this.shown = this.row(second);
    this.rows.push(this.shown);
    if (this.rows.length > PERF_KEPT_ROWS) this.rows.shift();
    this.changed = true;
  },

  row(second) {
    const frames = [...second.frames].sort((a, b) => a - b);
    const paints = [...second.paints].sort((a, b) => a - b);
    const display = Math.max(PERF_MIN_FRAME_MS, perfPercentile([...this.display].sort((a, b) => a - b), 0.1));
    const dropped = frames.filter((frame) => frame > display * PERF_DROP_RATIO).length;
    return {
      at: Date.now(),
      view: perfView(),
      moving: [...second.moving].join('+'),
      frames: second.presented,
      frameP50: perfMs(frames, 0.5),
      frameP95: perfMs(frames, 0.95),
      frameMax: perfMs(frames, 1),
      dropped,
      longs: second.longs.length,
      longWorst: perfMs([...second.longs].sort((a, b) => a - b), 1),
      paintP50: perfMs(paints, 0.5),
      paintMax: perfMs(paints, 1),
      spark: second.sparks,
      motion: perfMotion(),
    };
  },

  // The rows so far, in one batch. While the window closes the batch goes with keepalive, so it outlives the page.
  async send(closing) {
    if (!state.token || state.closed || this.rows.length === 0) return;
    const body = JSON.stringify({ rows: this.rows.splice(0, this.rows.length) });
    if (closing) {
      fetch('/api/perf', {
        method: 'POST',
        keepalive: true,
        headers: { [SESSION]: state.token, 'Content-Type': 'application/json' },
        body,
      });
      return;
    }
    // A batch brain serve did not take is dropped: the log loses those seconds, and the window goes on.
    await call('/api/perf', { method: 'POST', body }).catch(() => null);
  },

  toggle() {
    if (this.overlay) {
      this.close();
      return;
    }
    this.open();
  },

  open() {
    this.overlay = probeOpen(PERF_GRAPH_TOP_MS, PERF_GRAPH_LINES_MS);
    this.changed = true;
    this.draw();
    this.readGpu();
    this.drawer = setInterval(() => this.draw(), PERF_DRAW_MS);
    this.gpuReader = setInterval(() => this.readGpu(), PERF_GPU_MS);
  },

  close() {
    clearInterval(this.drawer);
    clearInterval(this.gpuReader);
    probeClose(this.overlay);
    this.overlay = null;
  },

  async readGpu() {
    if (!state.token || state.closed) return;
    const { ok, body } = await call('/api/perf/gpu').catch(() => ({ ok: false }));
    if (!ok) return;
    this.gpu = body;
    this.changed = true;
  },

  // Each frame a bar at its time, as tall as its interval, late ones over 33 ms, beside the last second's numbers.
  draw() {
    if (!this.overlay || !this.changed) return;
    this.changed = false;
    const bars = this.recent.map(([at, interval]) => [at, interval, interval > PERF_GRAPH_LINES_MS[1]]);
    probeDraw(this.overlay, bars, this.report());
  },

  // The last second's numbers and the GPU's, as the lines beside the graph.
  report() {
    const row = this.shown;
    const lines = [];
    if (row) {
      lines.push(
        `${row.view}  ${row.moving || 'still'}`,
        `frames ${row.frames}  dropped ${row.dropped}`,
        `frame ms p50 ${perfText(row.frameP50)}  p95 ${perfText(row.frameP95)}  max ${perfText(row.frameMax)}`,
        `long frames ${row.longs}  worst ${perfText(row.longWorst)} ms`,
        `paint ms p50 ${perfText(row.paintP50)}  max ${perfText(row.paintMax)}`,
        `spark frames ${row.spark}`,
      );
    }
    const vram = this.gpu?.vramMb ?? null;
    if (vram === null) {
      lines.push('GPU: no reading');
    } else {
      lines.push(`GPU ${vram} MB  ${this.gpu.loadPercent} %`);
    }
    return lines;
  },
};

// The view shown, by the name its inbox registered under, and a node opened in it.
function perfView() {
  if (state.view === 'node') return `${state.inbox} node`;
  return state.inbox;
}

// Which of Home's motion settings are on, joined by +; empty while motion is off.
function perfMotion() {
  if (setting('motion') !== true) return '';
  const on = ['motion', ...PERF_MOTION.filter((effect) => setting(effect) === true)];
  if (matchMedia('(prefers-reduced-motion: reduce)').matches) on.push('reduced');
  return on.join('+');
}

// The value a share of the way through an ascending list; 0 for an empty one.
function perfPercentile(sorted, share) {
  if (sorted.length === 0) return 0;
  return sorted[Math.min(sorted.length - 1, Math.floor(share * sorted.length))];
}

// A share of an ascending list of times, null for an empty one, so the row leaves it empty.
function perfMs(sorted, share) {
  if (sorted.length === 0) return null;
  return perfPercentile(sorted, share);
}

function perfText(ms) {
  if (ms === null) return '-';
  return ms.toFixed(1);
}

function onPerfKey(event) {
  if (event.key !== 'F3' || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
  event.preventDefault();
  perf.toggle();
}

// Whether a long animation frame ran across the last pause, so its length is the pause and not work.
function perfSpansPause(entry) {
  const pause = perf.pause;
  if (!pause) return false;
  return entry.startTime <= pause.from && entry.startTime + entry.duration >= pause.to - PERF_ROW_MS;
}

function onPerfInput() {
  perf.moving('input');
}

if (PerformanceObserver.supportedEntryTypes?.includes('long-animation-frame')) {
  new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      if (perfSpansPause(entry)) continue;
      perf.second.longs.push(entry.duration);
    }
  }).observe({ type: 'long-animation-frame', buffered: false });
}
for (const type of ['pointermove', 'pointerdown', 'wheel', 'keydown']) {
  window.addEventListener(type, onPerfInput, { capture: true, passive: true });
}
document.addEventListener('keydown', onPerfKey, { capture: true });
window.addEventListener('pagehide', () => perf.send(true));
// Chromium pauses the frames of a hidden window; the first frame after it shows again only restarts the measurement, so
// the time it was hidden never reads as one long frame.
document.addEventListener('visibilitychange', () => {
  perf.last = 0;
});
// A row reads Home's settings, which a module defines after this file, so the rows start once every script ran.
document.addEventListener('DOMContentLoaded', () => {
  setInterval(() => perf.tick(), PERF_ROW_MS);
  setInterval(() => perf.send(false), PERF_SEND_MS);
});
