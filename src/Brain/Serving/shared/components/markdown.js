// The markdown component: the one way both windows draw markdown. It returns an element, the prose of text, with every
// mermaid fence in it drawn by the component itself, so no page draws diagrams after a render. A click on a drawn
// diagram sends an open-image event, bubbling, with the diagram in its detail, for the window to open in its image
// viewer. A page whose layout depends on a diagram's height listens for the diagrams-drawn event the element sends,
// bubbling, once its diagrams are in place.
//
// Options: links turns a link's target into the path of a file the page opens, or null for a target that is no file;
// mark says how a block of source lines is marked, as markdown.js takes them; className adds a class to the prose.
import { drawDiagrams, markdownWithDiagrams } from '/diagrams.js';

export function markdownView(text, { links = null, mark = null, className = '' } = {}) {
  const prose = document.createElement('div');
  prose.className = `prose ${className}`.trim();
  prose.innerHTML = markdownWithDiagrams(text, links, mark);
  prose.addEventListener('click', onProseClick);
  drawDiagrams(prose);
  return prose;
}

function onProseClick(event) {
  const drawn = event.target.closest('figure.diagram svg');
  if (!drawn) {
    return;
  }
  drawn.dispatchEvent(new CustomEvent('open-image', { bubbles: true, detail: { node: drawn } }));
}
