// The workspace pane: the folder's code of the session in view, its workspace, which the workspace's own server serves
// and draws in a frame. brain code draws none of the tree, the reader or git; it only shows the frame, and says why
// when there is none: the workspace starts, or it could not, or its server stopped.
//
// Each folder whose workspace was shown keeps its frame while a session runs in it, hidden while another shows, so the
// file open, the tree and the git pane stay as the user left them, and sessions in the same folder share one. The
// frames are not drawn with lit-html, which would make a frame anew, and its workspace with it, each time the pane's
// place in the page changed: workspaceFramesPaint keeps them in the pane once it is drawn.
import { html, nothing } from '/lit-html.js';

// What the pane says for the folder in view while its frame does not show: workspace is { url } or { problem }, or
// null before the workspace was asked for.
export function workspacePaneView(folder, workspace, focused) {
  let said = nothing;
  if (folder && !workspace) said = html`<div class="quiet"><h2>The workspace starts</h2><p>${folder}</p></div>`;
  if (workspace?.problem) said = html`<div class="quiet"><h2>No workspace</h2><p>${workspace.problem}</p></div>`;
  return html`<section class="code-workspace" aria-label="Code" ?data-focus=${focused}>${said}</section>`;
}

// Keeps a frame in the pane for each of frames, { folder, url, shown }, and no other: a new folder's frame opens on its
// url, and one whose folder is gone goes, and its workspace with it.
export function workspaceFramesPaint(host, frames) {
  const pane = host.querySelector('.code-workspace');
  if (!pane) return;
  const wanted = new Map(frames.map((frame) => [frame.folder, frame]));
  const kept = new Map();
  for (const frame of pane.querySelectorAll('iframe')) {
    if (wanted.get(frame.dataset.folder)?.url === frame.dataset.url) {
      kept.set(frame.dataset.folder, frame);
      continue;
    }
    frame.remove();
  }
  for (const wish of frames) {
    let frame = kept.get(wish.folder);
    if (!frame) frame = openFrame(pane, wish);
    frame.hidden = !wish.shown;
  }
}

function openFrame(pane, wish) {
  const frame = document.createElement('iframe');
  frame.className = 'code-workspace-frame';
  frame.title = `The workspace of ${wish.folder}`;
  frame.dataset.folder = wish.folder;
  frame.dataset.url = wish.url;
  frame.src = wish.url;
  pane.append(frame);
  return frame;
}

// The frame that shows the workspace of folder, or null.
export function workspaceFrameOf(host, folder) {
  for (const frame of host.querySelectorAll('.code-workspace iframe')) {
    if (frame.dataset.folder === folder) return frame;
  }
  return null;
}

// The folder whose frame posted a message, when it came from that frame's own origin; else null, and the message is
// no workspace's.
export function workspaceFolderOf(host, event) {
  for (const frame of host.querySelectorAll('.code-workspace iframe')) {
    if (frame.contentWindow !== event.source) continue;
    if (new URL(frame.dataset.url).origin !== event.origin) return null;
    return frame.dataset.folder;
  }
  return null;
}
