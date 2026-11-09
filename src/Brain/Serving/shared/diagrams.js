// Mermaid diagrams in markdown, drawn by brain, in brain's window and in a workspace alike. Only the markdown component
// uses this module, so a diagram is drawn wherever markdown is.
//
// A mermaid fence stays its source, as code in a figure, until drawDiagrams draws it. The page's policy allows no inline
// style, and mermaid writes its look as a <style> in the svg and as style attributes. So the svg is parsed apart from
// the page, its stylesheet becomes a constructed one, and each style attribute is set through the element's style, both
// of which the policy allows. Mermaid itself loads only once the first diagram is in view, and each diagram is drawn
// once per source and theme.
import { resolved, resolvedSize } from '/tokens.js';
import { blockAttrs, escapeHtml, markdown } from '/markdown.js';

const diagramCache = new Map();
let mermaidLoaded = null;
let mermaidTheme = null;
let diagramCount = 0;

function loadMermaid() {
  if (!mermaidLoaded) {
    mermaidLoaded = new Promise((resolve, reject) => {
      const script = document.createElement('script');
      script.src = '/mermaid.js';
      script.addEventListener('load', () => resolve(window.mermaid));
      script.addEventListener('error', () => reject(new Error('brain could not load mermaid')));
      document.head.append(script);
    });
  }
  return mermaidLoaded;
}

function themeVariables() {
  const font = getComputedStyle(document.documentElement).getPropertyValue('--sans').trim();
  const ink = resolved('--ink');
  const soft = resolved('--ink-soft');
  const paper = resolved('--paper');
  const canvas = resolved('--canvas');
  const rule = resolved('--rule-strong');
  const muted = resolved('--muted');
  const select = resolved('--select');
  return {
    fontFamily: font,
    fontSize: resolvedSize('--text-2'),
    background: paper,
    primaryColor: canvas,
    primaryTextColor: ink,
    primaryBorderColor: rule,
    secondaryColor: select,
    tertiaryColor: paper,
    lineColor: muted,
    textColor: ink,
    edgeLabelBackground: paper,
    clusterBkg: canvas,
    clusterBorder: rule,
    actorBkg: canvas,
    actorBorder: rule,
    actorTextColor: ink,
    actorLineColor: rule,
    signalColor: soft,
    signalTextColor: ink,
    labelBoxBkgColor: canvas,
    labelBoxBorderColor: rule,
    labelTextColor: ink,
    loopTextColor: soft,
    noteBkgColor: select,
    noteBorderColor: rule,
    noteTextColor: ink,
    activationBkgColor: select,
    activationBorderColor: rule,
    sequenceNumberColor: paper,
  };
}

async function mermaidReady() {
  const mermaid = await loadMermaid();
  const theme = JSON.stringify(themeVariables());
  if (theme !== mermaidTheme) {
    // Compact spacing: a trail's column is a reading measure, not a canvas, so a diagram drawn tight keeps its words at
    // reading size there.
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: 'base',
      themeVariables: JSON.parse(theme),
      flowchart: { htmlLabels: true, nodeSpacing: 28, rankSpacing: 36, padding: 8, diagramPadding: 4 },
      sequence: { width: 96, height: 34, actorMargin: 24, boxMargin: 6, messageMargin: 26, noteMargin: 6, diagramMarginX: 4, diagramMarginY: 4 },
    });
    mermaidTheme = theme;
    diagramCache.clear();
  }
  return mermaid;
}

// Draws every diagram under root that is not drawn yet: from the cache at once, or once mermaid drew it. The promise
// settles once every one of them is in place.
export function drawDiagrams(root) {
  return drawFigures([...root.querySelectorAll('figure.diagram:not([data-drawn])')]);
}

// A figure keeps its source, so it can be drawn again. Once all are in place, the first figure says so to the page
// around it with a diagrams-drawn event, for a page whose layout depends on the height of a diagram.
function drawFigures(figures) {
  const placing = [];
  for (const figure of figures) {
    figure.diagramSource ??= figure.querySelector('code').textContent;
    const source = figure.diagramSource;
    const done = diagramCache.get(source);
    if (done) {
      placeDiagram(figure, done);
      continue;
    }
    // Marked at once, so a draw of the page while mermaid works does not start the same diagram again.
    figure.dataset.drawn = '';
    placing.push(drawDiagram(source).then((drawn) => placeDiagram(figure, drawn)));
  }
  return Promise.all(placing).then(() => figures[0]?.dispatchEvent(new CustomEvent('diagrams-drawn', { bubbles: true })));
}

function placeDiagram(figure, done) {
  figure.dataset.drawn = '';
  figure.classList.toggle('diagram-broken', Boolean(done.error));
  if (!done.error) {
    figure.replaceChildren(done.svg.cloneNode(true));
    return;
  }
  const caption = document.createElement('figcaption');
  caption.textContent = `This diagram does not parse: ${done.error}`;
  const code = document.createElement('code');
  code.textContent = figure.diagramSource;
  const pre = document.createElement('pre');
  pre.append(code);
  figure.replaceChildren(caption, pre);
}

async function drawDiagram(source) {
  try {
    const mermaid = await mermaidReady();
    if (diagramCache.has(source)) return diagramCache.get(source);
    const id = `diagram-${++diagramCount}`;
    const { svg } = await mermaid.render(id, source);
    const done = { svg: adoptDiagram(svg, id) };
    diagramCache.set(source, done);
    return done;
  } catch (error) {
    const done = { error: String(error?.message || error).split('\n')[0] };
    diagramCache.set(source, done);
    return done;
  }
}

// The svg mermaid drew, as an element of this page whose look the policy lets through.
function adoptDiagram(text, id) {
  const parsed = new DOMParser().parseFromString(`<!doctype html><body>${text}`, 'text/html');
  const svg = parsed.body.querySelector('svg');
  if (!svg) throw new Error('mermaid drew no svg');
  const sheet = new CSSStyleSheet();
  sheet.replaceSync([...svg.querySelectorAll('style')].map((style) => style.textContent).join('\n'));
  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
  for (const style of svg.querySelectorAll('style')) style.remove();
  const looks = [];
  for (const el of [svg, ...svg.querySelectorAll('[style]')]) {
    if (!el.hasAttribute('style')) continue;
    looks.push(el.getAttribute('style'));
    el.removeAttribute('style');
    el.setAttribute('data-look', String(looks.length - 1));
  }
  const adopted = document.importNode(svg, true);
  adopted.id = id;
  for (const el of [adopted, ...adopted.querySelectorAll('[data-look]')]) {
    if (!el.hasAttribute('data-look')) continue;
    el.style.cssText = looks[Number(el.getAttribute('data-look'))];
    el.removeAttribute('data-look');
  }
  // At its own size where it fits, and scaled down to its box where it does not.
  const natural = parseFloat(adopted.style.maxWidth);
  if (natural) {
    adopted.removeAttribute('width');
    adopted.style.width = `${natural}px`;
    adopted.style.maxWidth = '100%';
  }
  return adopted;
}

// A theme switch draws every diagram on the page again, in the theme's colors, where it stands.
function redrawDiagrams() {
  mermaidTheme = null;
  diagramCache.clear();
  drawFigures([...document.querySelectorAll('figure.diagram')].filter((figure) => figure.diagramSource !== undefined));
}

// The window swaps /theme.css when the Omarchy theme switches; once the new sheet loads, the diagrams draw again.
new MutationObserver((changes) => {
  for (const change of changes) {
    for (const added of change.addedNodes) {
      if (added instanceof HTMLLinkElement && added.matches('[data-theme]')) added.addEventListener('load', redrawDiagrams);
    }
  }
}).observe(document.head, { childList: true });
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', redrawDiagrams);

// Markdown with each mermaid fence kept as its source in a figure that drawDiagrams draws. mark, as markdown takes it,
// counts the lines of the whole text: each part between the fences shifts it by the lines before the part.
// A backtick in a pattern is written \x60, so the gates that read this file do not take it for a template.
export function markdownWithDiagrams(text, links = null, mark = null) {
  const parts = text.replace(/\r\n?/g, '\n').split(/^\x60{3}mermaid[ \t]*\n([\s\S]*?)^\x60{3}[ \t]*$/m);
  const html = [];
  let line = 0;
  parts.forEach((part, index) => {
    const at = line;
    const breaks = part.split('\n').length - 1;
    if (index % 2 === 0) {
      line += breaks;
      let shifted = null;
      if (mark) shifted = (lead, from, to) => mark(lead + at, from + at, to + at);
      if (part.trim()) html.push(markdown(part, links, shifted));
      return;
    }
    // The fence spans its opening line, the diagram's lines and its closing line. The part before it marks lines
    // removed just before it, so the figure takes no lead.
    line += 1 + breaks;
    const to = line + 1;
    const attrs = blockAttrs(mark, to, at, to, 'diagram');
    html.push(`<figure${attrs}><pre><code>${escapeHtml(part.replace(/\n$/, ''))}</code></pre></figure>`);
  });
  return html.join('');
}
