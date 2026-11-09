// The image viewer: one view of an image laid over the window. Scroll zooms around the pointer, a drag pans, Alt+R
// resets the view to the image whole and Esc closes. It is the one way to work with an image, in brain's window and
// in a workspace alike.
//
// It keeps its own state, and each window holds the one instance in its own state: imageViewer(source) makes one from a
// copy of an svg or an img, imageViewerDo() changes its view, and the window renders it again with imageViewerView()
// and then paints it with imageViewerPaint(), which puts the zoom and pan on the stage: the page's policy allows a
// style set through the element's style and none in a template. The view hands a wheel or a drag to the window as
// onZoom(factor, x, y) and onPan(dx, dy), which the window runs on the instance, as its own command or call.
//
// imageViewerKeys is the keys it answers while it is open, as data: key names as a workspace's keymap writes them, and
// the command each runs with its arguments. imageViewerKey() runs the one an event names on the instance, for a window
// that routes keys by hand; it returns close for Esc, moved when the view changed and null for any other key.
import { html, nothing } from '/lit-html.js';

const MOST_ZOOM = 40;
const LEAST_ZOOM = 0.2;
const ZOOM_SPEED = 0.0015;
const ZOOM_STEP = 1.25;
const PAN_STEP = 48;

export const imageViewerKeys = [
  { key: 'Escape', command: 'close', text: 'close', args: {} },
  { key: 'Alt+r', command: 'reset', text: 'reset', args: {} },
  { key: '+', command: 'zoom', text: 'zoom in', args: { factor: ZOOM_STEP, x: 0, y: 0 } },
  { key: '=', command: 'zoom', text: 'zoom in', args: { factor: ZOOM_STEP, x: 0, y: 0 } },
  { key: '-', command: 'zoom', text: 'zoom out', args: { factor: 1 / ZOOM_STEP, x: 0, y: 0 } },
  { key: 'ArrowLeft', command: 'pan', text: 'pan', args: { dx: PAN_STEP, dy: 0 } },
  { key: 'ArrowRight', command: 'pan', text: 'pan', args: { dx: -PAN_STEP, dy: 0 } },
  { key: 'ArrowUp', command: 'pan', text: 'pan', args: { dx: 0, dy: PAN_STEP } },
  { key: 'ArrowDown', command: 'pan', text: 'pan', args: { dx: 0, dy: -PAN_STEP } },
];

// A viewer of a copy of source, the whole image, centred, at its fitted size.
export function imageViewer(source) {
  const node = source.cloneNode(true);
  node.removeAttribute('width');
  node.removeAttribute('height');
  node.removeAttribute('style');
  return { node, scale: 1, x: 0, y: 0, drag: null };
}

// Changes the view: reset, zoom by a factor around a point given from the middle of the frame, or pan by a distance.
export function imageViewerDo(viewer, command, args) {
  if (command === 'reset') {
    viewer.scale = 1;
    viewer.x = 0;
    viewer.y = 0;
    return;
  }
  if (command === 'pan') {
    viewer.x += args.dx;
    viewer.y += args.dy;
    return;
  }
  const scale = Math.min(MOST_ZOOM, Math.max(LEAST_ZOOM, viewer.scale * args.factor));
  const grown = scale / viewer.scale;
  viewer.x = args.x - (args.x - viewer.x) * grown;
  viewer.y = args.y - (args.y - viewer.y) * grown;
  viewer.scale = scale;
}

// The name of a key as imageViewerKeys writes it. Alt+R by the key's place, since Alt can change the letter a layout
// types.
function nameOf(event) {
  if (event.altKey && event.code === 'KeyR') {
    return 'Alt+r';
  }
  if (event.altKey) {
    return `Alt+${event.key}`;
  }
  return event.key;
}

export function imageViewerKey(viewer, event) {
  const found = imageViewerKeys.find((binding) => binding.key === nameOf(event));
  if (!found) {
    return null;
  }
  if (found.command === 'close') {
    return 'close';
  }
  imageViewerDo(viewer, found.command, found.args);
  return 'moved';
}

// The viewer's template: a shade over the window that takes the pointer, and the box inset from its edges. Framed, it
// sits inside its container, for the Stylebook, and is no dialog; open over the window it is the modal dialog.
export function imageViewerView(viewer, { onZoom = null, onPan = null, framed = false } = {}) {
  const role = framed ? nothing : 'dialog';
  const modal = framed ? nothing : 'true';
  return html`
    <div class="image-viewer" role=${role} aria-modal=${modal} aria-label="Image viewer" data-framed=${framed ? 'true' : nothing}
      @wheel=${(event) => zoomed(onZoom, event)} @pointerdown=${(event) => dragStarted(viewer, event)}
      @pointermove=${(event) => dragged(viewer, onPan, event)} @pointerup=${(event) => dragEnded(viewer, event)}
      @pointercancel=${(event) => dragEnded(viewer, event)}>
      <div class="image-viewer-box">
        <div class="image-viewer-stage">${viewer.node}</div>
        <p class="image-viewer-help"><kbd class="hint">Scroll</kbd> zoom <kbd class="hint">Drag</kbd> pan <kbd class="hint">Alt R</kbd> reset <kbd class="hint">Esc</kbd> close</p>
      </div>
    </div>`;
}

// Puts the viewer's zoom and pan on the stage of the viewer drawn under root.
export function imageViewerPaint(viewer, root) {
  const stage = root.querySelector('.image-viewer-stage');
  stage.style.transform = `translate(${viewer.x}px, ${viewer.y}px) scale(${viewer.scale})`;
}

// The wheel zooms around the pointer, which is read from the middle of the frame, the point the stage scales around.
function zoomed(onZoom, event) {
  if (!onZoom) {
    return;
  }
  event.preventDefault();
  const box = event.currentTarget.getBoundingClientRect();
  const factor = Math.exp(-event.deltaY * ZOOM_SPEED);
  const x = event.clientX - box.left - box.width / 2;
  const y = event.clientY - box.top - box.height / 2;
  onZoom(factor, x, y);
}

function dragStarted(viewer, event) {
  if (event.button !== 0) {
    return;
  }
  try {
    event.currentTarget.setPointerCapture(event.pointerId);
  } catch {
    // A pointer that is not active, as a script sends, cannot be captured, and its drag works without.
  }
  event.currentTarget.dataset.dragging = 'true';
  viewer.drag = { x: event.clientX, y: event.clientY };
}

function dragged(viewer, onPan, event) {
  if (!viewer.drag || !onPan) {
    return;
  }
  const dx = event.clientX - viewer.drag.x;
  const dy = event.clientY - viewer.drag.y;
  viewer.drag = { x: event.clientX, y: event.clientY };
  onPan(dx, dy);
}

function dragEnded(viewer, event) {
  viewer.drag = null;
  delete event.currentTarget.dataset.dragging;
}
