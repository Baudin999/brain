// The image viewer in the workspace: an image laid over the window, opened by a click on a diagram in the reader. The
// workspace holds the one instance in its state under viewer, and every change to it is a command: open, close, reset,
// zoom and pan. While it is open it holds the keys, so Esc and Alt+R reach nothing else, and the focus goes back to
// where it was when it closes. Its keys are the viewer's own, imageViewerKeys, in the keymap, and v in the reader opens
// the first diagram of the file.
'use strict';

function viewerOpened(ws) {
  if (!ws.viewer.open) throw new Refusal('no image is open');
  return ws.viewer.open;
}

function viewerView(ws) {
  if (!ws.viewer.open) return nothing;
  return imageViewerView(ws.viewer.open, {
    onZoom: (factor, x, y) => execute('viewer.zoom', { factor, x, y }, FROM.click),
    onPan: (dx, dy) => execute('viewer.pan', { dx, dy }, FROM.click),
  });
}

function viewerPainted(host) {
  if (workspace.viewer.open) imageViewerPaint(workspace.viewer.open, host);
}

plugin({
  name: 'viewer',
  state: { open: null, before: 'reader' },
  commands: [
    {
      name: 'viewer.open',
      title: 'Open an image in the viewer',
      run: (ws, { node }) => {
        if (!node) throw new Refusal('no diagram in the file');
        ws.viewer.before = ws.focus;
        ws.viewer.open = imageViewer(node);
        ws.focus = 'viewer';
      },
    },
    {
      name: 'viewer.close',
      title: 'Close the image viewer',
      run: (ws) => {
        viewerOpened(ws);
        ws.viewer.open = null;
        ws.focus = ws.viewer.before;
      },
    },
    { name: 'viewer.reset', title: 'Show the whole image', run: (ws) => imageViewerDo(viewerOpened(ws), 'reset', {}) },
    { name: 'viewer.zoom', title: 'Zoom the image', run: (ws, args) => imageViewerDo(viewerOpened(ws), 'zoom', args) },
    { name: 'viewer.pan', title: 'Pan the image', run: (ws, args) => imageViewerDo(viewerOpened(ws), 'pan', args) },
  ],
  keys: [
    // The first diagram of the file in the reader; a click opens the one it hit.
    { key: 'v', focus: 'reader', command: 'viewer.open', args: () => ({ node: document.querySelector('#reader figure.diagram svg') }) },
    ...imageViewerKeys.map((binding) => ({ key: binding.key, focus: 'viewer', command: `viewer.${binding.command}`, args: () => binding.args })),
  ],
  views: [{ slot: 'overlay', draw: viewerView, painted: viewerPainted }],
});

// A click on a diagram in the reader opens it.
document.addEventListener('open-image', (event) => execute('viewer.open', { node: event.detail.node }, FROM.click));
