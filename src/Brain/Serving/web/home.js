// Home: the constellation. Every node and every edge of every graph the user has, personal, global and each project
// in projects.tsv, as one sky. brain serve places every node, the same for the same graph on every visit: personal is
// the core, global the ring around it, and each project a cluster around the ring in its own color. Nothing moves once
// it is drawn and nothing can be dragged; scroll zooms, a drag pans, Alt+R shows the whole sky again.
//
// A node's look says what it is. Information is a round star and an idea a four-pointed one; a node with only a title
// is drawn hollow; a node with no edge wears a dotted ring. A star grows with its edges, and each sits on a disc of the
// sky's own color so what passes beneath it does not cross it. Edges are quiet gray lines under the stars, as thick as
// their weight, and take their clusters' colors when hovered or when their node is selected. Each cluster's name and
// node count lie above everything. Behind it all lies one still image, a few faint stars and a soft nebula, tied to no
// cluster, on a dark theme and while the settings' starry background is on. It is a CSS background of its own element
// under the sky, from web/backdrop.png: it never moves and is never repainted. Swap the file for another to change it.
//
// Each cluster lies on a faint glow in its color, brighter at its heart. Global's is a band from the ring's inner edge,
// which brain serve sends, to its rim, so personal reads as the core with global a ring around it and the gap between
// them stays dark.
//
// On a dark theme, a star with 6 or more edges wears a soft, still halo of light in its cluster's color, the bright
// core of its galaxy: from 6 edges the more edges, the brighter and wider, up to 40, and a star with fewer has none. On
// a light theme there are no halos, since light does not read there. Each halo is bright at its star and falls off
// fast, so past a star's rim it adds at most about a sixth of its color, which keeps a title beside it legible. A halo
// dims with its star while a selection or a search dims the sky, and the settings' glow turns them off.
//
// The glow and the halos are the still light under the sky. They are painted to a canvas kept aside,
// only when the view, the sky's focus, the theme or a setting changes, and each paint of the sky draws that canvas as
// one image under its edges, so a hover repaints no glow. The sky's own canvas is drawn by the CPU and painted a
// quarter of the view past each edge; the sparks' canvas covers only the view.
//
// While the view moves, nothing is painted: a pan, a zoom and a glide move and scale the finished sky and its sparks by
// a CSS transform, on the compositor. The view settles at the end of a glide, or 120 ms after the last scroll or drag
// that moved it, and the sky is painted sharp once. A pan or a zoom out that runs past the quarter painted beyond the
// view paints the sky again, at most once every 200 ms, so a long drag never shows a bare edge.
//
// While motion and the glide setting are on, and the system does not ask for reduced motion, the view of the sky glides
// instead of jumping, and only the view moves: Alt+R eases back to the whole sky in about half a second; a scroll zoom
// eases toward its scale around the pointer, which stays over the same point of the sky; a pan let go while moving
// coasts a little and slows to a stop. Any new input takes over at once from where the view is. Without motion or glide
// the view jumps.
//
// Light that teaches, never movement of the constellation, each on its own setting. An edge last travelled today, in
// the edge log, carries a small spark in its cluster's color that runs along it from one end to the other, slowly
// and now and then, each edge at its own moment (sparks, which needs motion). The sparks are the only light that moves.
// They lie on a canvas of their own above the sky, and each display frame of a running spark clears and paints only the
// small box around each spark, never the whole canvas. Between runs nothing ticks: the sparks sleep until the next one
// starts, and stop while Home is hidden. While a spark cannot run, with motion off or reduced motion asked for, such an
// edge shows a little brighter than the quiet gray instead. A node born today shines a little brighter: a soft light in
// its cluster's color around the star, still and with no ring, so it reads apart from the ink ring of the selected, lit
// and hovered stars and from the dotted ring of a star with no edge (newstars). An edge that brain faded lists, the
// weakest ten, is drawn much fainter, like dying light (fading). The spark, the new star's light and the faded edge
// follow the sky's focus and dim with the rest while it is dimmed.
//
// With the atlas setting on, each cluster's name is set like a constellation on a star atlas: small capitals with wide
// letter spacing, the node count beside it smaller and quieter, and a soft halo of the sky's background color behind
// the letters so they read over the halos and the edges on dark and light themes. The names stay still.
// The placement of titles measures the names as they are drawn. Off, the names are plain text in the cluster's color.
//
// A large cluster reads like a galaxy: at the whole sky it shows its glow, its edges faintly and its most connected
// stars, and the rest fade in as the view zooms in. A small cluster shows every star at every zoom, and a lit or hovered
// star always shows. A cluster's name lies above the stars it shows. A title lies only where it covers no star and no
// other title, beside its star, above, below or across a corner; the selected, hovered and lit stars get theirs first,
// then the most connected stars of each part of the screen, more of them as the view zooms in.
//
// A click selects a node: it, its edges and its neighbours light and the rest dim. A second click on it opens it in the
// main pane, as a search result does, and Esc closes it. Hovering a node shows it without opening it; holding Shift pins
// that panel so the pointer can move into it and scroll it.
//
// Search is the window's own: / goes to it, and on Home it searches every graph of the sky with the engine brain lookup
// uses. Every node it finds brightens and grows, not only lookup's top hits, and the rest dim. Looking is reading: nothing
// here asks brain serve for anything that travels an edge.
//
// Home tells the window's perf, in perf.js, how it runs, and changes nothing for it: how long each paint of the sky
// takes, each display frame a spark runs, and each display frame of a glide.
// The perf log records them with the rest of the window, and F3 shows them in the window's overlay.
//
// The sky is read again each time Home is shown and each time a write lands, so it shows the graph as it is now. The
// selection and the hover name nodes by ref, so they hold across a read; a theme switch paints it in the new colors.

const SKY_MIN_ZOOM = 0.5;
const SKY_MAX_ZOOM = 40;
// At the whole sky each large cluster shows this many of its stars, the most connected first, and four times as many at
// twice the zoom; a cluster no larger shows every star. A star past that count fades out over this share more of it.
const SKY_SHOWN = 70;
const SKY_FADE = 0.6;
// From this zoom on, the screen is cut into regions about this wide, and the most connected stars of each region ask
// for a title, one more of them per region with each step of zoom.
const SKY_TITLE_ZOOM = 1.4;
const SKY_REGION_PX = 180;
const SKY_TITLE_PX = 13;
// A lit title with no free spot tries these shorter lengths before it is left out.
const SKY_SHORT_CHARS = [24, 14];
// Titles ask only the stars filed in the squares of this size their box touches.
const SKY_SQUARE_PX = 32;
const SKY_MARGIN_PX = 24;
const SKY_NAME_PX = 36;
const SKY_PICK_PX = 5;
const SKY_DRAG_PX = 4;
const SKY_LABEL_CHARS = 36;

// The sky's canvas is painted this share of the view past each edge, so a pan shows no bare edge.
// While the view moves, a pan past it paints again at most once per SKY_OUTRUN_MS, and the view settles SKY_SETTLE_MS
// after the last move.
const SKY_MARGIN = 0.25;
const SKY_OUTRUN_MS = 200;
const SKY_SETTLE_MS = 120;
// The view's glide, in milliseconds except the speed: the length of the reset to the whole sky, the time constant of a
// zoom closing on its scale, the time constant of a coast slowing, the speed in pixels per millisecond below which a
// coast stops, and how far back from the release a pan's speed is measured.
const SKY_RESET_MS = 500;
const SKY_ZOOM_EASE_MS = 90;
const SKY_COAST_MS = 320;
const SKY_COAST_MIN_PX_MS = 0.03;
const SKY_FLING_MS = 80;
// A star wears a halo from SKY_GLOW_MIN edges; it reaches full strength at SKY_GLOW_FULL edges. The halo reaches from
// SKY_GLOW_REACH to SKY_GLOW_REACH + SKY_GLOW_GROW star radii, and has an opacity of SKY_GLOW_ALPHA to SKY_GLOW_PEAK at
// its star, falling to SKY_GLOW_FALL of that a fifth of the way out, and to a tenth of it halfway. A dimmed star's halo
// shows at SKY_GLOW_DIMMED of its light.
const SKY_GLOW_MIN = 6;
const SKY_GLOW_FULL = 40;
const SKY_GLOW_REACH = 3;
const SKY_GLOW_GROW = 5;
const SKY_GLOW_ALPHA = 0.07;
const SKY_GLOW_PEAK = 0.16;
const SKY_GLOW_FALL = 0.55;
const SKY_GLOW_DIMMED = 0.2;

// A travelled-today edge is drawn this much brighter than its quiet gray, up to SKY_TRAVELLED_CAP of opacity, and a
// faded edge this share of it. A spark runs its edge at 50 pixels a second, but in no less than 1.5 and no more than 5
// seconds, once per 7 to 16 seconds, with a head of this radius and a tail this long, and shows at SKY_SPARK_DIMMED
// while the sky is dimmed.
const SKY_TRAVELLED = 2.2;
const SKY_TRAVELLED_CAP = 0.6;
const SKY_FADED = 0.3;
const SKY_SPARK_SECONDS = [1.5, 5];
const SKY_SPARK_EVERY = [7, 16];
const SKY_SPARK_HEAD = 2.6;
const SKY_SPARK_TAIL = 16;
const SKY_SPARK_DIMMED = 0.3;
// A new star's light reaches this many star radii and shows at this opacity at its middle.
const SKY_NEW_REACH = 3.2;
const SKY_NEW_LIGHT = 0.5;

const sky = {
  // What brain serve placed: { clusters, nodes, edges }, null until it loads. counts holds each cluster's node count,
  // and rank each node's place among its cluster's stars, the most connected first.
  data: null,
  counts: [],
  rank: [],
  error: null,
  // The world point in the middle of the canvas and the pixels per world unit; fit is the scale that shows it all.
  view: null,
  fit: 1,
  // The glide running, if any: { ease } to a view, { zoom } to a scale around a point, { coast } at a speed.
  glide: null,
  glideFrame: false,
  // The view the sky was last painted at and when, which the transform of a moving view starts from, and the timer that
  // paints the sky sharp once the view settles.
  painted: null,
  settle: null,
  // What the pointer is on: { node: ref }, { edge: key } or null.
  hover: null,
  // The selected node's ref, null when none is.
  selected: null,
  // Each node's place in data.nodes by ref, and each edge's by its key.
  nodeAt: new Map(),
  edgeAt: new Map(),
  // True while the hover panel is pinned by Shift.
  sticky: false,
  drag: null,
  // Each hovered node as /api/node reads it, by ref, so a node read once shows at once until the sky is read again.
  read: new Map(),
  // Where the pointer last hovered, so the panel can be placed again once a node's text arrives.
  at: [0, 0],
  // A color per cluster, read from the theme when the sky or the theme changes, the theme's ink, quiet line and canvas,
  // and the page's font.
  colors: [],
  // The same colors as "r, g, b" channels, which a halo's fade takes an opacity of.
  rgb: [],
  ink: null,
  line: null,
  back: null,
  font: null,
  // The backdrop's element, the still image under the sky.
  backdrop: null,
  // True when the theme's canvas is dark, so the backdrop shows.
  dark: false,
  // True while a paint waits for the next frame.
  painting: false,
  // The still light under the sky, the glow and the halos, on a canvas kept aside, and what it was last
  // painted for.
  still: null,
  stillKey: null,
  // The sparks' canvas, and the sparks of the last paint of the sky: where each edge lies and when its spark runs.
  // While they run, the boxes the last frame painted, and the frame or the timer that wakes them next.
  spark: null,
  sparks: [],
  sparkBoxes: [],
  sparkFrame: null,
  sparkTimer: null,
  canvas: null,
  wrap: null,
  tip: null,
};

async function loadSky() {
  const { ok, body } = await call('/api/sky');
  if (!ok) {
    sky.error = body.error;
    if (state.inbox === HOME) renderMain();
    return;
  }
  sky.data = body;
  sky.counts = body.clusters.map((cluster, index) => body.nodes.filter((node) => node.cluster === index).length);
  sky.rank = rankStars(body.nodes);
  sky.nodeAt = new Map(body.nodes.map((node, index) => [node.ref, index]));
  sky.edgeAt = new Map(body.edges.map((edge, index) => [edgeKey(edge), index]));
  sky.read.clear();
  sky.error = null;
  if (sky.selected !== null && !sky.nodeAt.has(sky.selected)) sky.selected = null;
  readSkyColors();
  if (sky.wrap && sky.hover) {
    const hover = sky.hover;
    sky.hover = null;
    if (hover.node !== undefined ? sky.nodeAt.has(hover.node) : sky.edgeAt.has(hover.edge)) setSkyHover(hover, ...sky.at);
    else {
      unpinTip();
      setSkyHover(null);
    }
  }
  if (state.inbox === HOME) renderMain();
}

// Each node's place among its cluster's stars: the most edges first, then by its place in the list, which is by ref.
function rankStars(nodes) {
  const seen = new Map();
  const rank = [];
  nodes
    .map((node, index) => index)
    .sort((a, b) => nodes[b].degree - nodes[a].degree || a - b)
    .forEach((index) => {
      rank[index] = seen.get(nodes[index].cluster) ?? 0;
      seen.set(nodes[index].cluster, rank[index] + 1);
    });
  return rank;
}

function edgeKey(edge) {
  return `${sky.data.nodes[edge.from].ref} ${edge.rel} ${sky.data.nodes[edge.to].ref}`;
}

// Home reads the sky only while it is shown: when a write lands elsewhere, the next showing reads it.
function onSkyChanged() {
  if (state.inbox === HOME) loadSky();
}

function onSkyTheme() {
  if (!sky.data) return;
  readSkyColors();
  drawSky();
}

// The canvas is made once, so the view holds while the window draws its other panes again.
function skyElements() {
  if (sky.wrap) return sky.wrap;
  sky.canvas = h('canvas', { class: 'sky-canvas', 'aria-label': 'The constellation of every graph' });
  // The sky's canvas, and the still light it draws, are drawn by the CPU. As a GPU canvas the sky lost its whole queued
  // drawing without an error on the user's GPU while about 500 MiB of it was free, and the
  // sky stayed blank. The sky is painted only when it changes, never while the view moves.
  sky.canvas.getContext('2d', { willReadFrequently: true });
  sky.still = document.createElement('canvas');
  sky.still.getContext('2d', { willReadFrequently: true });
  sky.tip = h('div', { class: 'sky-tip', hidden: true });
  sky.backdrop = h('div', { class: 'sky-backdrop', 'aria-hidden': 'true' });
  sky.spark = h('canvas', { class: 'sky-spark', 'aria-hidden': 'true' });
  sky.wrap = h('div', { class: 'sky' }, sky.backdrop, sky.canvas, sky.spark, sky.tip);
  new ResizeObserver(() => drawSky()).observe(sky.wrap);
  document.addEventListener('visibilitychange', wakeSparks);
  matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', drawSky);
  // The pointer is read on the sky itself, not on a canvas, since a canvas a zoom out scales down covers less of it.
  sky.wrap.addEventListener('wheel', onSkyWheel, { passive: false });
  sky.wrap.addEventListener('mousedown', onSkyDown);
  sky.wrap.addEventListener('mousemove', onSkyMove);
  sky.wrap.addEventListener('mouseleave', () => sky.sticky || setSkyHover(null));
  // The image viewer opens under the pointer in a pinned tip, and the tip stays pinned for it.
  sky.tip.addEventListener('mouseleave', (event) => state.imageViewer || sky.wrap.contains(event.relatedTarget) || unpinTip());
  window.addEventListener('mouseup', onSkyUp);
  return sky.wrap;
}

function renderHomeMain(main) {
  if (!sky.data) {
    fill(main, h('div', { class: 'quiet' }, h('h2', {}, 'Home'), h('p', {}, sky.error || 'Reading every graph.')));
    return;
  }
  fill(main, skyElements(), pendingHost);
  renderPending();
  drawSky();
}

// The projects with something waiting, along the bottom of Home. A click on one focuses the window on it.
const pendingHost = h('div', { class: 'pending-host' });

function renderPending() {
  render(pendingBar({ projects: pendingProjects(), onPick: setFocus }), pendingHost);
}

// ---- The view ----

function skySize() {
  return { width: sky.wrap.clientWidth, height: sky.wrap.clientHeight };
}

// The whole sky, centred, with a margin around it and room above it for the names of the topmost clusters.
function resetSky() {
  const { width, height } = skySize();
  if (!sky.data || !width || !height) return;
  let left = Infinity;
  let right = -Infinity;
  let top = Infinity;
  let bottom = -Infinity;
  for (const cluster of sky.data.clusters) {
    left = Math.min(left, cluster.x - cluster.radius);
    right = Math.max(right, cluster.x + cluster.radius);
    top = Math.min(top, cluster.y - cluster.radius);
    bottom = Math.max(bottom, cluster.y + cluster.radius);
  }
  sky.fit = Math.min((width - 2 * SKY_MARGIN_PX) / (right - left), (height - 2 * SKY_MARGIN_PX - SKY_NAME_PX) / (bottom - top));
  const whole = { x: (left + right) / 2, y: (top + bottom) / 2 - SKY_NAME_PX / 2 / sky.fit, scale: sky.fit };
  if (sky.view && moving('glide')) {
    startGlide({ ease: { from: sky.view, to: whole, start: performance.now() } });
    return;
  }
  stopGlide();
  sky.view = whole;
  drawSky();
}

// ---- Moving the finished sky ----

// The view moved: the painted sky and its sparks are moved and scaled to it by a transform, and nothing is painted,
// unless the view ran past what was painted beyond its edges and the last paint is SKY_OUTRUN_MS old. The sky is
// painted sharp once the view settles.
function moveSky() {
  if (!sky.painted) {
    drawSky();
    return;
  }
  const { width, height } = skySize();
  const margin = skyMargin(width, height);
  const from = sky.painted.view;
  const scale = sky.view.scale / from.scale;
  const dx = (width / 2) * (1 - scale) + (from.x - sky.view.x) * sky.view.scale;
  const dy = (height / 2) * (1 - scale) + (from.y - sky.view.y) * sky.view.scale;
  const coversX = dx <= scale * margin.x && scale * (width + margin.x) + dx >= width;
  const coversY = dy <= scale * margin.y && scale * (height + margin.y) + dy >= height;
  clearTimeout(sky.settle);
  sky.settle = setTimeout(settleSky, SKY_SETTLE_MS);
  if (!(coversX && coversY) && performance.now() - sky.painted.at >= SKY_OUTRUN_MS) {
    paintSky();
    return;
  }
  placeLayers(`matrix(${scale}, 0, 0, ${scale}, ${dx}, ${dy})`);
}

// A glide paints the sky itself when it ends.
function settleSky() {
  sky.settle = null;
  if (sky.glide) return;
  drawSky();
}

// The transform of the layers that move with the sky: none right after a paint.
function placeLayers(transform) {
  sky.canvas.style.transform = transform;
  sky.spark.style.transform = transform;
}

// ---- Gliding ----

function stopGlide() {
  sky.glide = null;
}

function startGlide(glide) {
  sky.glide = glide;
  if (sky.glideFrame) return;
  sky.glideFrame = true;
  requestAnimationFrame(stepGlide);
}

const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - (-2 * t + 2) ** 3 / 2);

// One display frame of the glide: move the view and the finished sky with it, ask for the next frame while it still
// moves, and paint the sky sharp once it stops.
function stepGlide(now) {
  sky.glideFrame = false;
  const glide = sky.glide;
  if (!glide) return;
  if (!sky.wrap?.isConnected || state.inbox !== HOME) {
    stopGlide();
    return;
  }
  const last = glide.last ?? now - 16;
  glide.last = now;
  const dt = Math.min(now - last, 50);
  perf.moving('glide');
  const running = glide.ease ? stepEase(glide.ease, now) : glide.zoom ? stepZoom(glide.zoom, dt) : stepCoast(glide.coast, dt);
  if (!running && sky.glide === glide) stopGlide();
  if (!sky.glide) {
    drawSky();
    return;
  }
  moveSky();
  sky.glideFrame = true;
  requestAnimationFrame(stepGlide);
}

function stepEase(ease, now) {
  const t = Math.min(1, (now - ease.start) / SKY_RESET_MS);
  const k = easeInOut(t);
  const { from, to } = ease;
  sky.view = {
    x: from.x + (to.x - from.x) * k,
    y: from.y + (to.y - from.y) * k,
    scale: from.scale * (to.scale / from.scale) ** k,
  };
  return t < 1;
}

// The scale closes on its target; the world point under the pointer stays under it.
function stepZoom(zoom, dt) {
  const left = Math.abs(Math.log(zoom.target / sky.view.scale));
  const scale = left < 0.002 ? zoom.target : zoom.target * (sky.view.scale / zoom.target) ** Math.exp(-dt / SKY_ZOOM_EASE_MS);
  zoomAround(zoom.sx, zoom.sy, scale);
  return scale !== zoom.target;
}

// The view at a scale, with the world point under a screen point still under it.
function zoomAround(sx, sy, scale) {
  const [wx, wy] = toWorld(sx, sy);
  const { width, height } = skySize();
  sky.view = { x: wx - (sx - width / 2) / scale, y: wy - (sy - height / 2) / scale, scale };
}

function stepCoast(coast, dt) {
  const decay = Math.exp(-dt / SKY_COAST_MS);
  sky.view = { ...sky.view, x: sky.view.x - (coast.vx * SKY_COAST_MS * (1 - decay)) / sky.view.scale, y: sky.view.y - (coast.vy * SKY_COAST_MS * (1 - decay)) / sky.view.scale };
  coast.vx *= decay;
  coast.vy *= decay;
  return Math.hypot(coast.vx, coast.vy) > SKY_COAST_MIN_PX_MS;
}

function toScreen(x, y) {
  const { width, height } = skySize();
  return [width / 2 + (x - sky.view.x) * sky.view.scale, height / 2 + (y - sky.view.y) * sky.view.scale];
}

function toWorld(sx, sy) {
  const { width, height } = skySize();
  return [sky.view.x + (sx - width / 2) / sky.view.scale, sky.view.y + (sy - height / 2) / sky.view.scale];
}

function zoomLevel() {
  return sky.view.scale / sky.fit;
}

function onSkyWheel(event) {
  if (!sky.view || sky.tip.contains(event.target)) return;
  event.preventDefault();
  const [sx, sy] = skyPoint(event);
  const from = sky.glide?.zoom?.target ?? sky.view.scale;
  const scale = Math.min(sky.fit * SKY_MAX_ZOOM, Math.max(sky.fit * SKY_MIN_ZOOM, from * Math.exp(-event.deltaY * 0.0015)));
  if (sky.sticky) unpinTip();
  if (moving('glide')) {
    startGlide({ zoom: { target: scale, sx, sy } });
    return;
  }
  stopGlide();
  zoomAround(sx, sy, scale);
  moveSky();
}

function onSkyDown(event) {
  if (event.button !== 0 || !sky.view || sky.tip.contains(event.target)) return;
  const [sx, sy] = skyPoint(event);
  stopGlide();
  sky.drag = { sx, sy, x: sky.view.x, y: sky.view.y, moved: false, samples: [] };
}

function onSkyMove(event) {
  if (!sky.drag && sky.tip.contains(event.target)) return;
  const [sx, sy] = skyPoint(event);
  if (sky.drag) {
    const dx = sx - sky.drag.sx;
    const dy = sy - sky.drag.sy;
    if (Math.hypot(dx, dy) > SKY_DRAG_PX) sky.drag.moved = true;
    if (sky.drag.moved) {
      sky.view = { ...sky.view, x: sky.drag.x - dx / sky.view.scale, y: sky.drag.y - dy / sky.view.scale };
      sky.drag.samples.push({ sx, sy, at: performance.now() });
      if (sky.drag.samples.length > 8) sky.drag.samples.shift();
      unpinTip();
      setSkyHover(null);
      moveSky();
      return;
    }
  }
  const hit = pickSky(sx, sy);
  // A pinned panel holds until a click, Esc, a zoom, a pan, Shift on another node, or the pointer leaving it for
  // somewhere outside the canvas.
  if (sky.sticky && !(event.shiftKey && hit?.node !== undefined && hit.node !== sky.hover?.node)) return;
  sky.sticky = false;
  setSkyHover(hit, sx, sy);
  if (event.shiftKey && hit?.node !== undefined) pinTip();
}

// A click that did not pan selects the node under it, a second click on the selected node opens it in the main pane as
// a search result opens, and a click on nothing clears the selection. Opening reads; it never travels an edge.
function onSkyUp(event) {
  const drag = sky.drag;
  sky.drag = null;
  if (drag?.moved) {
    coastFrom(drag);
    if (!sky.glide) drawSky();
    return;
  }
  if (!drag || !sky.wrap.contains(event.target) || sky.tip.contains(event.target)) return;
  const [sx, sy] = skyPoint(event);
  const hit = pickSky(sx, sy);
  unpinTip();
  if (hit?.node !== undefined && hit.node === sky.selected) {
    openMain(hit.node);
    return;
  }
  sky.selected = hit?.node ?? null;
  drawSky();
}

// A pan let go while moving coasts at the speed of its last moments up to the release, when glide is on. A pan that
// stopped before the release coasts not at all.
function coastFrom(drag) {
  const now = performance.now();
  const recent = drag.samples.filter((sample) => now - sample.at <= SKY_FLING_MS);
  if (!moving('glide') || recent.length < 2) return;
  const first = recent[0];
  const last = recent[recent.length - 1];
  const span = now - first.at;
  if (span <= 0 || now - last.at > SKY_FLING_MS / 2) return;
  const vx = (last.sx - first.sx) / span;
  const vy = (last.sy - first.sy) / span;
  if (Math.hypot(vx, vy) > SKY_COAST_MIN_PX_MS) startGlide({ coast: { vx, vy } });
}

function skyPoint(event) {
  const box = sky.wrap.getBoundingClientRect();
  return [event.clientX - box.left, event.clientY - box.top];
}

// ---- What the pointer is on ----

// The node under the point by its ref, else the edge by its key, else nothing. A star that has not faded in yet is not
// there to point at.
function pickSky(sx, sy) {
  if (!sky.data || !sky.view) return null;
  let best = null;
  let bestDistance = Infinity;
  const shown = starsShown(skyFocus(), zoomShown());
  sky.data.nodes.forEach((node, index) => {
    if (shown[index] < 0.5) return;
    const [x, y] = toScreen(node.x, node.y);
    const distance = Math.hypot(x - sx, y - sy);
    if (distance <= starRadius(node) + SKY_PICK_PX && distance < bestDistance) {
      best = { node: node.ref };
      bestDistance = distance;
    }
  });
  if (best) return best;
  sky.data.edges.forEach((edge) => {
    const from = sky.data.nodes[edge.from];
    const to = sky.data.nodes[edge.to];
    const distance = toSegment(sx, sy, toScreen(from.x, from.y), toScreen(to.x, to.y));
    if (distance <= SKY_PICK_PX && distance < bestDistance) {
      best = { edge: edgeKey(edge) };
      bestDistance = distance;
    }
  });
  return best;
}

function toSegment(px, py, [ax, ay], [bx, by]) {
  const dx = bx - ax;
  const dy = by - ay;
  const length = dx * dx + dy * dy;
  const t = length ? Math.max(0, Math.min(1, ((px - ax) * dx + (py - ay) * dy) / length)) : 0;
  return Math.hypot(px - (ax + t * dx), py - (ay + t * dy));
}

function setSkyHover(hit, sx, sy) {
  const same = JSON.stringify(hit) === JSON.stringify(sky.hover);
  sky.hover = hit;
  sky.wrap.style.cursor = hit?.node !== undefined ? 'pointer' : sky.drag?.moved ? 'grabbing' : 'default';
  if (!hit) sky.tip.hidden = true;
  else if (!same) {
    showTip(hit.node !== undefined ? nodeTip(hit.node) : edgeTip(sky.data.edges[sky.edgeAt.get(hit.edge)]));
    placeTip(sx, sy);
  } else if (!sky.sticky) placeTip(sx, sy);
  if (!same) drawSky();
}

function showTip(content) {
  fill(sky.tip, ...content);
  sky.tip.scrollTop = 0;
  sky.tip.hidden = false;
}

// Beside the pointer, inside the sky.
function placeTip(sx, sy) {
  sky.at = [sx, sy];
  const { width, height } = skySize();
  sky.tip.style.left = `${Math.max(8, Math.min(sx + 14, width - sky.tip.offsetWidth - 8))}px`;
  sky.tip.style.top = `${Math.max(8, Math.min(sy + 14, height - sky.tip.offsetHeight - 8))}px`;
}

function pinTip() {
  if (sky.hover?.node === undefined || sky.sticky) return;
  sky.sticky = true;
  sky.tip.classList.add('sticky');
}

function unpinTip() {
  if (!sky.sticky) return;
  sky.sticky = false;
  sky.tip.classList.remove('sticky');
  setSkyHover(null);
}

// The hovered node as it reads: title, kind and scope at once, then its body and why once /api/node answers.
function nodeTip(ref) {
  const node = sky.data.nodes[sky.nodeAt.get(ref)];
  const read = sky.read.get(ref);
  if (!read) {
    call(`/api/node?ref=${encodeURIComponent(ref)}`).then(({ ok, body }) => {
      if (!ok) {
        toast(body.error);
        return;
      }
      if (!sky.nodeAt.has(ref)) return;
      sky.read.set(ref, body);
      if (sky.hover?.node === ref) {
        showTip(nodeTip(ref));
        if (!sky.sticky) placeTip(...sky.at);
      }
    });
  }
  const cluster = sky.data.clusters[node.cluster];
  return [
    h('p', { class: 'sky-tip-title' }, node.title || node.ref),
    h('p', { class: 'sky-tip-meta' }, h('span', {}, nodeKind(node)), graphChip({ name: node.scope, hue: cluster.hue })),
    read?.body ? markdownView(read.body) : null,
    read?.why ? h('section', { class: 'sky-tip-why' }, h('h2', {}, 'Why'), markdownView(read.why)) : null,
    read?.body || read?.why ? h('p', { class: 'sky-tip-hint' }, keys('Shift'), ' pins this to scroll it') : null,
  ];
}

function nodeKind(node) {
  const kind = node.kind || 'information';
  const notes = [node.bare && 'only a title', node.degree === 0 ? 'no edge' : `${node.degree} ${node.degree === 1 ? 'edge' : 'edges'}`].filter(Boolean);
  return `${kind}, ${notes.join(', ')}`;
}

function edgeTip(edge) {
  const from = sky.data.nodes[edge.from];
  const to = sky.data.nodes[edge.to];
  return [
    h('p', { class: 'sky-tip-title' }, `${edge.rel}, weight ${edge.weight.toFixed(2)}`),
    h('p', { class: 'sky-tip-meta' }, `${from.title || from.ref} to ${to.title || to.ref}`),
  ];
}

// ---- Drawing ----

// A search lights its hits in place of the selection, so what was done last is what is lit.
function onSkyHits() {
  if (state.query.trim()) sky.selected = null;
  drawSky();
}

// What is lit, by place in data.nodes and data.edges: the selected node, its neighbours and its edges; else the search
// hits; null when neither. grown holds the nodes that grow: the selected one, or every hit.
function skyFocus() {
  const selected = selectedAt();
  if (selected !== null) {
    const nodes = new Set([selected]);
    const edges = new Set();
    sky.data.edges.forEach((edge, index) => {
      if (edge.from !== selected && edge.to !== selected) return;
      edges.add(index);
      nodes.add(edge.from).add(edge.to);
    });
    return { nodes, edges, grown: new Set([selected]) };
  }
  if (!state.query.trim()) return null;
  const refs = new Set(state.hits.map((hit) => hit.ref));
  const nodes = new Set();
  sky.data.nodes.forEach((node, index) => refs.has(node.ref) && nodes.add(index));
  return { nodes, edges: new Set(), grown: nodes };
}

// The selected node's place in data.nodes, null when none is selected.
function selectedAt() {
  return sky.selected === null ? null : sky.nodeAt.get(sky.selected) ?? null;
}

// A star's radius on screen: it grows with the node's edges, and a little as the view zooms in.
function starRadius(node) {
  return (1.5 + 0.9 * Math.sqrt(node.degree)) * Math.pow(zoomLevel(), 0.45);
}

// How much of each star the zoom shows, from 0 to 1: every star of a small cluster, and of a large one its most
// connected first, more as the view zooms in.
function zoomShown() {
  const shown = SKY_SHOWN * zoomLevel() ** 2;
  return sky.data.nodes.map((node, index) =>
    sky.counts[node.cluster] <= SKY_SHOWN ? 1 : Math.max(0, Math.min(1, (1 + SKY_FADE - sky.rank[index] / shown) / SKY_FADE)),
  );
}

// How much of each star shows: as the zoom shows it, and the hovered star and the lit ones always.
function starsShown(focus, zoomed) {
  const hovered = sky.nodeAt.get(sky.hover?.node);
  return zoomed.map((part, index) => (index === hovered || focus?.nodes.has(index) ? 1 : part));
}

// A star as drawn on screen: its radius, its body (an idea's points, a dotted ring), whether it wears the ring of the
// hovered or selected, and its rim, the outermost of all it draws.
function starShape(node, index, focus) {
  const radius = starRadius(node) * (focus?.grown.has(index) ? 1.5 : 1);
  const body = node.degree === 0 ? radius + 3 : radius * (node.kind === 'idea' ? 1.6 : 1);
  const selected = selectedAt();
  const ringed = index === sky.nodeAt.get(sky.hover?.node) || index === selected || (focus?.grown.has(index) && selected === null);
  return { radius, body, ringed, rim: ringed ? body + 5 : body + 1.5 };
}

// Each cluster's color from the theme: a project's the color of its chip, which components.css gives it; personal and
// global quiet. The page resolves each, so the canvas gets a plain color whatever the theme writes. It runs when the sky
// is read and when the theme changes, not on every paint.
function readSkyColors() {
  const probe = h('span', { hidden: true });
  document.body.append(probe);
  const resolve = (color) => {
    probe.style.color = color;
    return getComputedStyle(probe).color;
  };
  const chip = (hue) => {
    const one = graphChip({ name: '', hue });
    probe.append(one);
    const color = getComputedStyle(one).color;
    one.remove();
    return color;
  };
  sky.colors = sky.data.clusters.map((cluster) =>
    cluster.hue === null ? resolve(cluster.tier === 'personal' ? 'var(--ink-soft)' : 'var(--muted)') : chip(cluster.hue),
  );
  sky.stillKey = null;
  sky.ink = resolve('var(--ink)');
  sky.line = resolve('var(--muted)');
  sky.back = resolve('var(--canvas)');
  // A color as one painted pixel, so any color syntax the theme uses reads the same. The clusters' colors are kept as
  // their channels too, "r, g, b", for the halos' fades.
  const pixel = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
  const channels = (color) => {
    pixel.fillStyle = color;
    pixel.fillRect(0, 0, 1, 1);
    return pixel.getImageData(0, 0, 1, 1).data;
  };
  sky.rgb = sky.colors.map((color) => channels(color).slice(0, 3).join(', '));
  const [red, green, blue] = channels(sky.back);
  sky.dark = 0.2126 * red + 0.7152 * green + 0.0722 * blue < 128;
  sky.font = getComputedStyle(document.body).fontFamily;
  probe.remove();
}

// Asks for a paint on the next frame; every ask before it comes is the same paint.
function drawSky() {
  if (sky.painting) return;
  sky.painting = true;
  requestAnimationFrame(() => {
    sky.painting = false;
    paintSky();
  });
}

// The perf's mark before and after a paint of the sky that shows; nothing else is added to it.
function paintSky() {
  if (!sky.data || !sky.wrap?.isConnected) return;
  const began = performance.now();
  paintLayers();
  perf.paint(performance.now() - began);
}

function paintLayers() {
  const { width, height } = skySize();
  if (!width || !height) return;
  if (!sky.view) {
    resetSky();
    return;
  }
  clearTimeout(sky.settle);
  sky.settle = null;
  const margin = skyMargin(width, height);
  const g = canvasContext(sky.canvas, width, height, margin);
  const focus = skyFocus();
  const zoomed = zoomShown();
  const shown = starsShown(focus, zoomed);
  const shapes = sky.data.nodes.map((node, index) => starShape(node, index, focus));
  // Bottom to top: the backdrop on its own element, then on the sky's canvas the still light of the glow and the halos,
  // the edges, the stars, the stars' titles, the clusters' names, and the sparks on their own canvas above.
  sky.backdrop.hidden = !backdropShown();
  drawStill(g, width, height, margin, focus, zoomed);
  drawEdges(g, focus, shown);
  drawStars(g, focus, shown, shapes);
  const names = clusterNames(g, zoomed);
  drawTitles(g, focus, shown, shapes, names);
  drawNames(g, names);
  sky.painted = { view: sky.view, at: performance.now() };
  placeLayers('');
  sky.sparks = skySparks(focus, shown, width, height);
  startSparks(width, height);
}

// How far past each edge of the view the layers that move with the sky are painted.
function skyMargin(width, height) {
  return { x: Math.round(width * SKY_MARGIN), y: Math.round(height * SKY_MARGIN) };
}

// A canvas sized to the view and the margin past each of its edges, placed so the view lies where the sky shows, and
// cleared, with its context in CSS pixels of the view.
function canvasContext(canvas, width, height, margin) {
  const ratio = window.devicePixelRatio || 1;
  const wide = width + 2 * margin.x;
  const high = height + 2 * margin.y;
  if (canvas.width !== Math.round(wide * ratio) || canvas.height !== Math.round(high * ratio)) {
    canvas.width = Math.round(wide * ratio);
    canvas.height = Math.round(high * ratio);
    canvas.style.left = `${-margin.x}px`;
    canvas.style.top = `${-margin.y}px`;
    canvas.style.width = `${wide}px`;
    canvas.style.height = `${high}px`;
    canvas.style.transformOrigin = `${margin.x}px ${margin.y}px`;
  }
  const g = canvas.getContext('2d');
  g.setTransform(ratio, 0, 0, ratio, margin.x * ratio, margin.y * ratio);
  g.clearRect(-margin.x, -margin.y, wide, high);
  return g;
}

// Whether the backdrop shows, and whether an effect of motion runs: motion is on and the system has not asked for less.
const backdropShown = () => setting('starfield') === true && sky.dark;
const moving = (effect) =>
  setting('motion') === true && setting(effect) === true && !matchMedia('(prefers-reduced-motion: reduce)').matches;

// The still light under the sky: painted to its own canvas only when what it shows changed, then drawn as one image.
function drawStill(g, width, height, margin, focus, zoomed) {
  const lit = focus ? [...focus.nodes].join(' ') : null;
  const key = JSON.stringify([sky.view, width, height, window.devicePixelRatio, setting('glow'), lit]);
  if (key !== sky.stillKey) {
    sky.stillKey = key;
    const still = canvasContext(sky.still, width, height, margin);
    drawHalos(still, skyHalos(focus, zoomed, width, height, margin));
    drawClusters(still);
  }
  g.save();
  g.setTransform(1, 0, 0, 1, 0, 0);
  g.drawImage(sky.still, 0, 0);
  g.restore();
}

// The halos of the stars the zoom shows and the lit ones that are well connected, on and around the painted area: where
// each lies, its radius, and its light with the sky's focus and the zoom taken in.
function skyHalos(focus, zoomed, width, height, margin) {
  if (setting('glow') !== true || !sky.dark) return [];
  const halos = [];
  sky.data.nodes.forEach((node, index) => {
    const shown = focus?.nodes.has(index) ? 1 : zoomed[index];
    if (node.degree < SKY_GLOW_MIN || !shown) return;
    const strength = Math.min(1, (node.degree - SKY_GLOW_MIN) / (SKY_GLOW_FULL - SKY_GLOW_MIN));
    const radius = starRadius(node) * (SKY_GLOW_REACH + SKY_GLOW_GROW * strength);
    const [x, y] = toScreen(node.x, node.y);
    if (x < -margin.x - radius || y < -margin.y - radius || x > width + margin.x + radius || y > height + margin.y + radius) return;
    const peak = SKY_GLOW_ALPHA + (SKY_GLOW_PEAK - SKY_GLOW_ALPHA) * strength;
    const focused = !focus || focus.nodes.has(index) ? 1 : SKY_GLOW_DIMMED;
    halos.push({ x, y, radius, rgb: sky.rgb[node.cluster], light: peak * shown * focused });
  });
  return halos;
}

// The halos, each a radial fade from its star's color, their light adding up.
function drawHalos(g, halos) {
  g.globalCompositeOperation = 'lighter';
  for (const halo of halos) {
    const fade = g.createRadialGradient(halo.x, halo.y, 0, halo.x, halo.y, halo.radius);
    const stops = [[0, 1], [0.2, SKY_GLOW_FALL], [0.5, 0.1], [1, 0]];
    stops.forEach(([at, share]) => fade.addColorStop(at, `rgba(${halo.rgb}, ${share})`));
    g.globalAlpha = halo.light;
    g.fillStyle = fade;
    g.beginPath();
    g.arc(halo.x, halo.y, halo.radius, 0, Math.PI * 2);
    g.fill();
  }
  g.globalCompositeOperation = 'source-over';
  g.globalAlpha = 1;
}

// The sparks of the edges last travelled today whose ends show and that cross the view: where each edge lies, its ends'
// colors, how bright with the sky's focus and the zoom taken in, and how long its spark runs and how often, with a
// phase, from its key. None unless the sparks setting and motion are on.
function skySparks(focus, shown, width, height) {
  if (!moving('sparks')) return [];
  const sparks = [];
  sky.data.edges.forEach((edge, index) => {
    const visible = Math.min(shown[edge.from], shown[edge.to]);
    if (!edge.travelled || !visible) return;
    const from = sky.data.nodes[edge.from];
    const to = sky.data.nodes[edge.to];
    const [ax, ay] = toScreen(from.x, from.y);
    const [bx, by] = toScreen(to.x, to.y);
    if ((ax < 0 && bx < 0) || (ay < 0 && by < 0) || (ax > width && bx > width) || (ay > height && by > height)) return;
    const random = seededRandom(nameSeed(edgeKey(edge)));
    const length = Math.hypot(bx - ax, by - ay);
    if (!length) return;
    sparks.push({
      ax,
      ay,
      bx,
      by,
      length,
      rgb: [sky.rgb[from.cluster], sky.rgb[to.cluster]],
      light: visible * (!focus || focus.edges.has(index) ? 1 : SKY_SPARK_DIMMED),
      run: Math.min(SKY_SPARK_SECONDS[1], Math.max(SKY_SPARK_SECONDS[0], length / 50)),
      every: SKY_SPARK_EVERY[0] + random() * (SKY_SPARK_EVERY[1] - SKY_SPARK_EVERY[0]),
      phase: random(),
    });
  });
  return sparks;
}

// The sparks run while Home shows with the tab visible, motion and the sparks setting are on, and an edge carries one.
function sparksRun() {
  return sky.sparks.length > 0 && moving('sparks') && state.inbox === HOME && sky.wrap?.isConnected && !document.hidden;
}

// A paint of the sky clears the sparks' canvas and starts them again from its sparks. The canvas covers the view and
// no margin, and while no edge carries a spark it has no pixels at all.
function startSparks(width, height) {
  cancelAnimationFrame(sky.sparkFrame);
  clearTimeout(sky.sparkTimer);
  sky.sparkFrame = null;
  sky.sparkTimer = null;
  sky.sparkBoxes = [];
  const none = { x: 0, y: 0 };
  if (sky.sparks.length === 0) {
    canvasContext(sky.spark, 0, 0, none);
    return;
  }
  canvasContext(sky.spark, width, height, none);
  stepSparks();
}

// The tab shown again wakes the sparks, unless a frame or a timer already will.
function wakeSparks() {
  if (sky.sparkFrame !== null || sky.sparkTimer !== null) return;
  stepSparks();
}

// One frame of the sparks: the boxes the last frame painted are cleared and each running spark is painted in its own.
// While one runs the next display frame comes; while none does, a timer wakes them when the next one starts.
function stepSparks() {
  sky.sparkFrame = null;
  sky.sparkTimer = null;
  if (!sparksRun()) return;
  const g = sky.spark.getContext('2d');
  for (const box of sky.sparkBoxes) g.clearRect(box.left, box.top, box.width, box.height);
  sky.sparkBoxes = [];
  const seconds = performance.now() / 1000;
  let wait = Infinity;
  for (const spark of sky.sparks) {
    const cycle = ((seconds / spark.every + spark.phase) % 1) * spark.every;
    if (cycle >= spark.run) {
      wait = Math.min(wait, spark.every - cycle);
      continue;
    }
    sky.sparkBoxes.push(paintSpark(g, spark, cycle / spark.run));
  }
  if (sky.sparkBoxes.length > 0) {
    perf.spark();
    sky.sparkFrame = requestAnimationFrame(stepSparks);
    return;
  }
  sky.sparkTimer = setTimeout(stepSparks, wait * 1000);
}

// A running spark at its share of the way along its edge: a bright head with a short tail behind it, fading in and out
// at the ends. The color changes at the middle of an edge between two clusters. On a dark theme the light adds up. It
// gives the box it painted.
function paintSpark(g, spark, at) {
  const rgb = spark.rgb[at < 0.5 ? 0 : 1];
  const x = spark.ax + (spark.bx - spark.ax) * at;
  const y = spark.ay + (spark.by - spark.ay) * at;
  const tail = Math.min(SKY_SPARK_TAIL, spark.length * at) / spark.length;
  const tx = x - (spark.bx - spark.ax) * tail;
  const ty = y - (spark.by - spark.ay) * tail;
  const reach = SKY_SPARK_HEAD * 2.5;
  g.globalCompositeOperation = sky.dark ? 'lighter' : 'source-over';
  const trail = g.createLinearGradient(x, y, tx, ty);
  trail.addColorStop(0, `rgba(${rgb}, 1)`);
  trail.addColorStop(1, `rgba(${rgb}, 0)`);
  g.globalAlpha = spark.light * Math.sin(at * Math.PI) * (sky.dark ? 0.9 : 1);
  g.strokeStyle = trail;
  g.lineWidth = 1.6;
  g.beginPath();
  g.moveTo(x, y);
  g.lineTo(tx, ty);
  g.stroke();
  const head = g.createRadialGradient(x, y, 0, x, y, reach);
  head.addColorStop(0, sky.dark ? 'rgba(255, 255, 255, 1)' : `rgba(${rgb}, 1)`);
  head.addColorStop(0.4, `rgba(${rgb}, 0.7)`);
  head.addColorStop(1, `rgba(${rgb}, 0)`);
  g.fillStyle = head;
  g.beginPath();
  g.arc(x, y, reach, 0, Math.PI * 2);
  g.fill();
  const left = Math.floor(Math.min(x, tx) - reach - 2);
  const top = Math.floor(Math.min(y, ty) - reach - 2);
  const right = Math.ceil(Math.max(x, tx) + reach + 2);
  const bottom = Math.ceil(Math.max(y, ty) + reach + 2);
  return { left, top, width: right - left, height: bottom - top };
}

// Numbers from 0 to 1 that repeat for the same seed: mulberry32.
function seededRandom(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// A seed from a name: FNV-1a over its letters.
function nameSeed(name) {
  return [...name].reduce((hash, letter) => Math.imul(hash ^ letter.charCodeAt(0), 16777619), 2166136261);
}

// A faint glow under each cluster, so the clusters read before their stars do, brighter at a cluster's heart where its
// stars lie densest. The ring's glow is a band from its inner edge to its rim, so the gap between the core and the ring
// stays dark.
function drawClusters(g) {
  sky.data.clusters.forEach((cluster, index) => {
    const [x, y] = toScreen(cluster.x, cluster.y);
    const radius = cluster.radius * sky.view.scale;
    g.globalAlpha = 0.07;
    if (cluster.tier === 'global') {
      const inner = cluster.inner * sky.view.scale;
      g.lineWidth = Math.max(2, radius - inner);
      g.strokeStyle = sky.colors[index];
      g.beginPath();
      g.arc(x, y, (radius + inner) / 2, 0, Math.PI * 2);
      g.stroke();
    } else {
      for (const reach of [1.15, 0.45]) {
        const glow = g.createRadialGradient(x, y, 0, x, y, radius * reach);
        glow.addColorStop(0, sky.colors[index]);
        glow.addColorStop(1, 'transparent');
        g.fillStyle = glow;
        g.beginPath();
        g.arc(x, y, radius * reach, 0, Math.PI * 2);
        g.fill();
      }
    }
  });
  g.globalAlpha = 1;
}

// Quiet gray lines first, fainter where an end has not faded in, then the lit ones over them in their clusters' colors:
// the hovered edge, the hovered node's edges and the selected node's edges.
function drawEdges(g, focus, shown) {
  const thicken = Math.min(1.6, Math.sqrt(zoomLevel()));
  const hovered = sky.nodeAt.get(sky.hover?.node);
  const edgeHovered = sky.edgeAt.get(sky.hover?.edge);
  const lit = (edge, index) => index === edgeHovered || edge.from === hovered || edge.to === hovered || focus?.edges.has(index);
  const line = (edge) => {
    const from = sky.data.nodes[edge.from];
    const to = sky.data.nodes[edge.to];
    const [ax, ay] = toScreen(from.x, from.y);
    const [bx, by] = toScreen(to.x, to.y);
    g.lineWidth = Math.min(3, 0.5 + 0.7 * Math.sqrt(edge.weight)) * thicken;
    return { from, to, ax, ay, bx, by };
  };
  g.strokeStyle = sky.line;
  sky.data.edges.forEach((edge, index) => {
    if (lit(edge, index)) return;
    const { ax, ay, bx, by } = line(edge);
    const quiet = 0.08 + 0.2 * Math.min(1, edge.weight / 1.5);
    const marked = edge.faded && setting('fading') === true ? SKY_FADED * quiet : edge.travelled && setting('sparks') === true && !moving('sparks') ? Math.min(SKY_TRAVELLED_CAP, SKY_TRAVELLED * quiet) : quiet;
    g.globalAlpha = marked * (focus ? 0.4 : 1) * (0.35 + 0.65 * Math.min(shown[edge.from], shown[edge.to]));
    g.beginPath();
    g.moveTo(ax, ay);
    g.lineTo(bx, by);
    g.stroke();
  });
  sky.data.edges.forEach((edge, index) => {
    if (!lit(edge, index)) return;
    const { from, to, ax, ay, bx, by } = line(edge);
    g.globalAlpha = 0.95;
    g.lineWidth += 0.6;
    if (from.cluster === to.cluster) g.strokeStyle = sky.colors[from.cluster];
    else {
      const bridge = g.createLinearGradient(ax, ay, bx, by);
      bridge.addColorStop(0, sky.colors[from.cluster]);
      bridge.addColorStop(1, sky.colors[to.cluster]);
      g.strokeStyle = bridge;
    }
    g.beginPath();
    g.moveTo(ax, ay);
    g.lineTo(bx, by);
    g.stroke();
  });
  g.globalAlpha = 1;
}

// Each star that shows on a disc of the sky's own color, so the edges beneath it stop at its rim.
function drawStars(g, focus, shown, shapes) {
  const selected = selectedAt();
  sky.data.nodes.forEach((node, index) => {
    if (!shown[index]) return;
    const [x, y] = toScreen(node.x, node.y);
    const { radius, body, ringed } = shapes[index];
    const color = sky.colors[node.cluster];
    const dim = !focus || focus.nodes.has(index) ? 1 : 0.25;
    if (node.born && setting('newstars') === true) {
      const light = g.createRadialGradient(x, y, 0, x, y, body * SKY_NEW_REACH);
      light.addColorStop(0, `rgba(${sky.rgb[node.cluster]}, 1)`);
      light.addColorStop(1, `rgba(${sky.rgb[node.cluster]}, 0)`);
      g.globalAlpha = SKY_NEW_LIGHT * dim * shown[index];
      g.fillStyle = light;
      g.beginPath();
      g.arc(x, y, body * SKY_NEW_REACH, 0, Math.PI * 2);
      g.fill();
    }
    g.globalAlpha = shown[index];
    g.fillStyle = sky.back;
    g.beginPath();
    g.arc(x, y, body + 1.5, 0, Math.PI * 2);
    g.fill();
    g.globalAlpha = dim * shown[index];
    if (node.degree === 0) {
      g.strokeStyle = color;
      g.lineWidth = 1;
      g.setLineDash([1.5, 2]);
      g.beginPath();
      g.arc(x, y, radius + 3, 0, Math.PI * 2);
      g.stroke();
      g.setLineDash([]);
    }
    g.beginPath();
    if (node.kind === 'idea') sparkle(g, x, y, radius * 1.6);
    else g.arc(x, y, radius, 0, Math.PI * 2);
    if (node.bare) {
      g.strokeStyle = color;
      g.lineWidth = 1.3;
      g.stroke();
    } else {
      g.fillStyle = color;
      g.fill();
    }
    if (ringed) {
      g.globalAlpha = 1;
      g.strokeStyle = sky.ink;
      g.lineWidth = selected === index ? 2 : 1.2;
      g.beginPath();
      g.arc(x, y, body + 4, 0, Math.PI * 2);
      g.stroke();
    }
  });
  g.globalAlpha = 1;
}

// A four-pointed star, an idea's shape.
function sparkle(g, x, y, radius) {
  const waist = radius * 0.32;
  g.moveTo(x, y - radius);
  g.quadraticCurveTo(x + waist * 0.3, y - waist * 0.3, x + radius, y);
  g.quadraticCurveTo(x + waist * 0.3, y + waist * 0.3, x, y + radius);
  g.quadraticCurveTo(x - waist * 0.3, y + waist * 0.3, x - radius, y);
  g.quadraticCurveTo(x - waist * 0.3, y - waist * 0.3, x, y - radius);
  g.closePath();
}

// The titles, each where its box covers no star, no other title and no cluster name: right of its star, else left,
// above, below, or across one of its corners, else it is left out. The selected star, the hovered one and the lit ones
// are placed first, so their titles push the rest aside, and one with no free spot tries a shorter title before it is
// left out. The selected star's title is never left out: with no free spot it takes the one covering the fewest stars
// and is drawn over them. The rest follow region by region of the screen, the most connected of each region first.
// Nothing is erased beneath a title: only the quiet edges pass under it.
function drawTitles(g, focus, shown, shapes, names) {
  const { width, height } = skySize();
  const taken = names.map((name) => name.box);
  const stars = starSquares(shown, shapes);
  g.textBaseline = 'middle';
  g.textAlign = 'left';
  g.font = '11px ' + sky.font;
  const selected = selectedAt();
  const hovered = sky.nodeAt.get(sky.hover?.node);
  const found = state.hits.map((hit) => hit.ref);
  const lit = (index) => (index === selected ? 0 : index === hovered ? 1 : focus?.nodes.has(index) ? 2 : 3);
  const rank = (index) => (lit(index) === 2 && selected === null ? found.indexOf(sky.data.nodes[index].ref) : -sky.data.nodes[index].degree);
  const inside = (box) => box.left >= 0 && box.top >= 0 && box.left + box.width <= width && box.top + box.height <= height;
  const free = (box, index) => inside(box) && !taken.some((other) => overlaps(box, other)) && !stars.covers(box, index);
  const draw = (box, title, alpha) => {
    taken.push(box);
    g.globalAlpha = alpha;
    g.fillStyle = sky.ink;
    g.fillText(title, box.left, box.top + SKY_TITLE_PX / 2);
  };
  const litOrder = sky.data.nodes
    .map((node, index) => index)
    .filter((index) => lit(index) < 3)
    .sort((a, b) => lit(a) - lit(b) || rank(a) - rank(b) || sky.rank[a] - sky.rank[b]);
  for (const index of litOrder) {
    const node = sky.data.nodes[index];
    const [x, y] = toScreen(node.x, node.y);
    if (x < 0 || y < 0 || x > width || y > height) continue;
    const full = node.title || node.ref;
    const placed = [SKY_LABEL_CHARS, ...SKY_SHORT_CHARS].some((chars) => {
      const title = clipTitle(full, chars);
      const box = titleSpots(g, title, x, y, shapes[index].rim).find((spot) => free(spot, index));
      if (box) draw(box, title, 1);
      return box;
    });
    if (placed || index !== selected) continue;
    const title = clipTitle(full, SKY_LABEL_CHARS);
    const spots = titleSpots(g, title, x, y, shapes[index].rim);
    const clear = spots.filter((spot) => inside(spot) && !taken.some((other) => overlaps(spot, other)));
    const fewest = (clear.length ? clear : spots).map((spot) => [stars.count(spot, index), spot]).sort((a, b) => a[0] - b[0])[0][1];
    draw(fewest, title, 1);
  }
  for (const index of regionOrder(shown, (index) => lit(index) === 3)) {
    const node = sky.data.nodes[index];
    const [x, y] = toScreen(node.x, node.y);
    const title = clipTitle(node.title || node.ref, SKY_LABEL_CHARS);
    const box = titleSpots(g, title, x, y, shapes[index].rim).find((spot) => free(spot, index));
    if (box) draw(box, title, focus ? 0.35 : 0.85);
  }
  g.globalAlpha = 1;
}

function clipTitle(full, chars) {
  return full.length > chars ? `${full.slice(0, chars - 1)}…` : full;
}

// The eight boxes a title may take around a star of this rim: right, left, above, below, then across each corner.
function titleSpots(g, title, x, y, rim) {
  const wide = g.measureText(title).width;
  const high = SKY_TITLE_PX;
  const corner = rim * Math.SQRT1_2 + 1;
  return [
    { left: x + rim + 3, top: y - high / 2 },
    { left: x - rim - 3 - wide, top: y - high / 2 },
    { left: x - wide / 2, top: y - rim - 1 - high },
    { left: x - wide / 2, top: y + rim + 1 },
    { left: x + corner, top: y - corner - high },
    { left: x + corner, top: y + corner },
    { left: x - corner - wide, top: y - corner - high },
    { left: x - corner - wide, top: y + corner },
  ].map((spot) => ({ ...spot, width: wide, height: high }));
}

// The unlit stars that ask for a title, region by region: the screen is cut into squares of the sky about
// SKY_REGION_PX wide, fixed in the sky so a pan keeps them, and each region gives its most connected star first, then its
// next, one more per region with each step of zoom.
function regionOrder(shown, unlit) {
  const zoom = zoomLevel();
  if (zoom < SKY_TITLE_ZOOM) return [];
  const { width, height } = skySize();
  const size = 2 ** Math.round(Math.log2(SKY_REGION_PX / sky.view.scale));
  const regions = new Map();
  sky.data.nodes.forEach((node, index) => {
    if (shown[index] !== 1 || !unlit(index)) return;
    const [x, y] = toScreen(node.x, node.y);
    if (x < 0 || y < 0 || x > width || y > height) return;
    const key = `${Math.floor(node.x / size)} ${Math.floor(node.y / size)}`;
    if (!regions.has(key)) regions.set(key, []);
    regions.get(key).push(index);
  });
  const asked = Math.ceil(zoom);
  const place = new Map();
  for (const members of regions.values()) {
    members.sort((a, b) => sky.data.nodes[b].degree - sky.data.nodes[a].degree || sky.rank[a] - sky.rank[b]);
    members.slice(0, asked).forEach((index, at) => place.set(index, at));
  }
  return [...place.keys()].sort((a, b) => place.get(a) - place.get(b) || sky.data.nodes[b].degree - sky.data.nodes[a].degree || a - b);
}

function overlaps(a, b) {
  return a.left < b.left + b.width && b.left < a.left + a.width && a.top < b.top + b.height && b.top < a.top + a.height;
}

// The stars that show on screen, each as the circle of its rim, filed by the squares it touches, so a title's box asks
// only the stars near it.
function starSquares(shown, shapes) {
  const { width, height } = skySize();
  const squares = new Map();
  const square = (x, y) => `${x} ${y}`;
  sky.data.nodes.forEach((node, index) => {
    if (!shown[index]) return;
    const [x, y] = toScreen(node.x, node.y);
    const r = shapes[index].rim;
    if (x + r < 0 || y + r < 0 || x - r > width || y - r > height) return;
    for (let sx = Math.floor((x - r) / SKY_SQUARE_PX); sx <= Math.floor((x + r) / SKY_SQUARE_PX); sx++) {
      for (let sy = Math.floor((y - r) / SKY_SQUARE_PX); sy <= Math.floor((y + r) / SKY_SQUARE_PX); sy++) {
        const key = square(sx, sy);
        if (!squares.has(key)) squares.set(key, []);
        squares.get(key).push({ index, x, y, r });
      }
    }
  });
  return {
    // True when the box covers any star but its own.
    covers(box, own) {
      return this.count(box, own, 1) > 0;
    },
    // How many stars but its own the box covers, counting no further than most.
    count(box, own, most = Infinity) {
      const seen = new Set();
      for (let sx = Math.floor(box.left / SKY_SQUARE_PX); sx <= Math.floor((box.left + box.width) / SKY_SQUARE_PX); sx++) {
        for (let sy = Math.floor(box.top / SKY_SQUARE_PX); sy <= Math.floor((box.top + box.height) / SKY_SQUARE_PX); sy++) {
          for (const star of squares.get(square(sx, sy)) ?? []) {
            if (star.index === own || seen.has(star.index)) continue;
            const nx = Math.max(box.left, Math.min(star.x, box.left + box.width));
            const ny = Math.max(box.top, Math.min(star.y, box.top + box.height));
            if (Math.hypot(star.x - nx, star.y - ny) < star.r) seen.add(star.index);
            if (seen.size >= most) return seen.size;
          }
        }
      }
      return seen.size;
    },
  };
}

// How a cluster's name is set. With the atlas setting on, it is small capitals with wide letter spacing and the count
// beside it, smaller and quieter; off, plain text in one font as before.
const ATLAS_NAME = { size: 11, spacing: 2.4 };
const ATLAS_COUNT = { size: 10, spacing: 0.5, gap: 7, alpha: 0.7 };

// Sets the canvas to a part of a name: the name, or its count when `count` is true.
function nameFont(g, count) {
  const atlas = setting('atlas') === true;
  g.font = (atlas ? `${count ? 400 : 600} ${count ? ATLAS_COUNT.size : ATLAS_NAME.size}px ` : '600 12px ') + sky.font;
  g.fontVariantCaps = atlas && !count ? 'small-caps' : 'normal';
  g.letterSpacing = atlas ? `${count ? ATLAS_COUNT.spacing : ATLAS_NAME.spacing}px` : '0px';
}

// Each cluster's name and node count, centred above the stars the zoom shows of it, with the box it takes, measured as
// drawNames sets them. A star counts as far out as it lies times how much of it shows, so the name rises smoothly as a
// halo fades in and never jumps.
function clusterNames(g, zoomed) {
  const atlas = setting('atlas') === true;
  const reach = sky.data.clusters.map(() => 0);
  sky.data.nodes.forEach((node, index) => {
    const cluster = sky.data.clusters[node.cluster];
    reach[node.cluster] = Math.max(reach[node.cluster], Math.hypot(node.x - cluster.x, node.y - cluster.y) * zoomed[index]);
  });
  return sky.data.clusters.flatMap((cluster, index) => {
    if (!sky.counts[index]) return [];
    const [x, top] = toScreen(cluster.x, cluster.y - reach[index]);
    const count = `(${sky.counts[index]})`;
    g.save();
    nameFont(g, false);
    const nameWidth = g.measureText(cluster.name).width;
    nameFont(g, true);
    const gap = atlas ? ATLAS_COUNT.gap : g.measureText(' ').width;
    const wide = nameWidth + gap + g.measureText(count).width;
    g.restore();
    const y = top - 20;
    const pad = atlas ? 5 : 2;
    return [{ title: cluster.name, count, nameWidth, gap, x: x - wide / 2, y, color: sky.colors[index], box: { left: x - wide / 2 - pad, top: y - 9, width: wide + 2 * pad, height: 18 } }];
  });
}

// The cluster names over everything else, no title beneath them. With atlas on, a soft halo of the sky's background
// color lies behind the letters so they read over the halos and the edges.
function drawNames(g, names) {
  const atlas = setting('atlas') === true;
  g.save();
  g.textBaseline = 'middle';
  g.textAlign = 'left';
  g.lineJoin = 'round';
  g.lineWidth = 4;
  g.strokeStyle = sky.back;
  const part = (name, count) => {
    const text = count ? name.count : name.title;
    const x = count ? name.x + name.nameWidth + name.gap : name.x;
    nameFont(g, count);
    if (atlas) {
      g.globalAlpha = 0.85;
      g.strokeText(text, x, name.y);
    }
    g.globalAlpha = atlas && count ? ATLAS_COUNT.alpha : 1;
    g.fillStyle = name.color;
    g.fillText(text, x, name.y);
  };
  for (const name of names) {
    part(name, false);
    part(name, true);
  }
  g.restore();
}

// ---- Home in the window ----

// The queue column on Home: what the shapes mean, and the keys.
function homeList() {
  if (!sky.data) return h('p', { class: 'queue-empty' }, sky.error || 'Reading every graph.');
  return h(
    'div',
    { class: 'sky-legend' },
    h(
      'dl',
      { class: 'sky-cues' },
      h('dt', {}, '●'), h('dd', {}, 'information'),
      h('dt', {}, '✦'), h('dd', {}, 'idea'),
      h('dt', {}, '○'), h('dd', {}, 'only a title'),
      h('dt', {}, '◌'), h('dd', {}, 'no edge'),
      h('dt', {}, '✺'), h('dd', {}, 'born today'),
    ),
    h('p', { class: 'sky-note' }, 'A star grows with its edges. An edge is as thick as its weight, and lights when hovered or when its node is selected. A spark runs an edge travelled today, and a very faint edge is fading.'),
    h(
      'p',
      { class: 'sky-keys' },
      h('span', {}, 'Select: click'),
      h('span', {}, 'Open: click it again'),
      h('span', {}, 'Pin the hover ', keys('Shift')),
      h('span', {}, 'Zoom: scroll'),
      h('span', {}, 'Pan: drag'),
      h('span', {}, 'Whole sky ', keys('Alt', 'R')),
      h('span', {}, 'Search ', keys('/')),
      h('span', {}, 'Chat ', keys('Alt', 'A')),
      h('span', {}, 'Perf ', keys('F3')),
    ),
  );
}

// Alt+R shows the whole sky; Shift pins the hovered node's panel; Esc lets a pinned panel go.
function onHomeKey(event) {
  if (state.inbox !== HOME || state.view !== 'review') return false;
  if (!sky.data || event.ctrlKey || event.metaKey) return false;
  if (event.altKey && event.code === 'KeyR') {
    event.preventDefault();
    resetSky();
    return true;
  }
  // Shift pins only as a plain key, so typing a capital letter in the search box pins nothing.
  if (event.key === 'Shift' && plainKey(event)) {
    pinTip();
    return false;
  }
  if (event.key === 'Escape' && sky.sticky) {
    event.preventDefault();
    unpinTip();
    return true;
  }
  return false;
}

// The node a chat starts with: the one pinned with Shift, else the selected one; none is a chat with no context.
async function homeContext() {
  const ref = sky.sticky && sky.hover?.node !== undefined ? sky.hover.node : sky.selected;
  if (ref === null || ref === undefined) return null;
  const node = await fetchNode(ref);
  return node.error ? null : `The user has this node selected on Home and the chat will probably be about it.\n\n${nodeText(node)}`;
}

function homeCommands() {
  if (state.inbox !== HOME) return [];
  return [{ name: 'Show the whole sky', keys: ['Alt', 'R'], run: resetSky }];
}

registerInbox({
  name: HOME,
  label: 'Home',
  top: true,
  ownSide: true,
  searchPath: '/api/sky/search',
  // The sky is to read; nothing in it waits for the user.
  count: () => 0,
  start: loadSky,
  shown: loadSky,
  changed: onSkyChanged,
  themed: onSkyTheme,
  redraw: drawSky,
  waitingChanged: renderPending,
  list: homeList,
  render: renderHomeMain,
  keys: onHomeKey,
  commands: homeCommands,
  context: homeContext,
  onHits: onSkyHits,
});
