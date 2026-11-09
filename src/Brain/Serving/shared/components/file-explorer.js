// The file explorer: brain's own way to pick images from the disk, a box laid over the window in brain's styling. It
// shows folders and images only. The images are a grid of thumbnails after the folders; a folder opens with a click or
// Enter, and Backspace or the up button leaves it. A click or Space marks an image, several at once, in any folder; P
// or a double click opens the image under the cursor in the image viewer; Enter attaches the marked ones, or the image under the cursor
// when none is marked; Esc closes. A side panel lists its places: a name and a full path each.
//
// It keeps its own state, and the window holds the one instance in its own state: fileExplorer(places) makes one,
// fileExplorerShow() puts a folder in it as the window read it, and fileExplorerThumb() a thumbnail the window made. It
// reads nothing itself: the view hands what the user does to the window as onOpen(path), onPreview(path), onAttach()
// and onClose(), and onChange() when only its own state changed, as a mark. fileExplorerKey() answers a key on the
// instance and says what the window is to do: close, attach, preview, open with the path, moved when the cursor moved,
// changed when a mark did, or null for a key it does not take. fileExplorerUnseen() names the images drawn in view whose
// thumbnail the window has still to make, fileExplorerColumns() how many tiles a row of the grid holds, and
// fileExplorerReveal() brings the tile under the cursor into view, which the window does when the keys moved it.
import { html, nothing } from '/lit-html.js';

const FOLDER_GLYPH = html`<svg class="file-explorer-glyph" viewBox="0 0 24 24" aria-hidden="true"><path d="M3 6.5A1.5 1.5 0 0 1 4.5 5h4.6l2 2h8.4A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z"/></svg>`;

// A new explorer, with its places, each { name, path }, and no folder in it yet.
export function fileExplorer(places) {
  return { places, path: null, parent: null, entries: [], cursor: 0, marked: new Set(), thumbs: new Map(), asked: new Set() };
}

function joined(folder, name) {
  if (folder.endsWith('/')) return folder + name;
  return `${folder}/${name}`;
}

// Puts the folder the window read in the explorer: its folders, then its images, the cursor on the first. The marks
// stay, so images marked in another folder go along.
export function fileExplorerShow(explorer, folder) {
  const folders = folder.folders.map((name) => ({ kind: 'folder', name, path: joined(folder.path, name) }));
  const images = folder.images.map((name) => ({ kind: 'image', name, path: joined(folder.path, name) }));
  explorer.path = folder.path;
  explorer.parent = folder.parent;
  explorer.entries = [...folders, ...images];
  explorer.cursor = 0;
}

export function fileExplorerThumb(explorer, path, src) {
  explorer.thumbs.set(path, src);
}

// The marked images, by their paths, in the order they were marked; the image under the cursor when none is.
export function fileExplorerMarked(explorer) {
  if (explorer.marked.size > 0) return [...explorer.marked];
  const at = explorer.entries[explorer.cursor];
  if (at?.kind === 'image') return [at.path];
  return [];
}

function toggle(explorer, path) {
  if (explorer.marked.has(path)) {
    explorer.marked.delete(path);
    return;
  }
  explorer.marked.add(path);
}

function move(explorer, by) {
  const last = explorer.entries.length - 1;
  explorer.cursor = Math.min(last, Math.max(0, explorer.cursor + by));
}

// What a key does in the explorer, on its instance, with the grid columns wide; see the head of this file.
export function fileExplorerKey(explorer, event, columns) {
  const at = explorer.entries[explorer.cursor];
  switch (event.key) {
    case 'Escape':
      return { do: 'close' };
    case 'Enter':
      if (at?.kind === 'folder' && explorer.marked.size === 0) return { do: 'open', path: at.path };
      return { do: 'attach' };
    case 'Backspace':
      if (!explorer.parent) return { do: 'changed' };
      return { do: 'open', path: explorer.parent };
    case ' ':
      if (at?.kind === 'image') toggle(explorer, at.path);
      return { do: 'changed' };
    case 'p':
    case 'P':
      if (at?.kind !== 'image') return { do: 'changed' };
      return { do: 'preview', path: at.path };
    case 'ArrowLeft':
      move(explorer, -1);
      return { do: 'moved' };
    case 'ArrowRight':
      move(explorer, 1);
      return { do: 'moved' };
    case 'ArrowUp':
      move(explorer, -columns);
      return { do: 'moved' };
    case 'ArrowDown':
      move(explorer, columns);
      return { do: 'moved' };
    default:
      return null;
  }
}

// How many tiles a row of the grid drawn under root holds: those level with the first.
export function fileExplorerColumns(root) {
  const tiles = [...root.querySelectorAll('.file-explorer-tile')];
  if (tiles.length === 0) return 1;
  const top = tiles[0].offsetTop;
  const level = tiles.filter((tile) => tile.offsetTop === top);
  return level.length;
}

// The paths of the images drawn in view under root that have no thumbnail and were not asked for, now marked asked.
export function fileExplorerUnseen(explorer, root) {
  const grid = root.querySelector('.file-explorer-grid');
  if (!grid) return [];
  const view = grid.getBoundingClientRect();
  const unseen = [];
  for (const tile of grid.querySelectorAll('.file-explorer-tile[data-kind="image"]')) {
    const path = tile.dataset.path;
    if (explorer.thumbs.has(path) || explorer.asked.has(path)) continue;
    const box = tile.getBoundingClientRect();
    if (box.bottom < view.top || box.top > view.bottom) continue;
    explorer.asked.add(path);
    unseen.push(path);
  }
  return unseen;
}

export function fileExplorerReveal(root) {
  const current = root.querySelector('.file-explorer-tile[aria-current="true"]');
  if (current) current.scrollIntoView({ block: 'nearest' });
}

function placeView(explorer, place, onOpen) {
  const current = place.path === explorer.path;
  return html`
    <li>
      <button class="row compact file-explorer-place" aria-current=${current ? 'true' : 'false'} title=${place.path} @click=${() => onOpen(place.path)}>
        <span class="file-explorer-place-name">${place.name}</span>
        <span class="file-explorer-place-path">${place.path}</span>
      </button>
    </li>
  `;
}

function pictureView(explorer, entry) {
  const thumb = explorer.thumbs.get(entry.path);
  if (!thumb) return html`<span class="file-explorer-wait" aria-hidden="true"></span>`;
  return html`<img class="file-explorer-image" src=${thumb} alt="">`;
}

function tileClicked(explorer, index, { onOpen, onChange }) {
  const entry = explorer.entries[index];
  explorer.cursor = index;
  if (entry.kind === 'folder') {
    onOpen(entry.path);
    return;
  }
  toggle(explorer, entry.path);
  onChange();
}

// A double click on an image previews it; its two clicks marked it and took the mark back.
function previewed(entry, { onPreview }) {
  if (entry.kind !== 'image') return;
  onPreview(entry.path);
}

function tileView(explorer, entry, index, actions) {
  const current = index === explorer.cursor;
  let marked = nothing;
  if (entry.kind === 'image') marked = explorer.marked.has(entry.path) ? 'true' : 'false';
  let picture = FOLDER_GLYPH;
  if (entry.kind === 'image') picture = pictureView(explorer, entry);
  return html`
    <li>
      <button class="file-explorer-tile" data-kind=${entry.kind} data-path=${entry.path} aria-current=${current ? 'true' : 'false'}
        aria-pressed=${marked} title=${entry.name} @click=${() => tileClicked(explorer, index, actions)} @dblclick=${() => previewed(entry, actions)}>
        <span class="file-explorer-picture">${picture}</span>
        <span class="file-explorer-name">${entry.name}</span>
      </button>
    </li>
  `;
}

function gridView(explorer, actions) {
  if (explorer.path === null) return html`<p class="file-explorer-none">Reading…</p>`;
  if (explorer.entries.length === 0) return html`<p class="file-explorer-none">No folders or images here.</p>`;
  return html`
    <ul class="file-explorer-grid" @scroll=${actions.onScroll}>
      ${explorer.entries.map((entry, index) => tileView(explorer, entry, index, actions))}
    </ul>
  `;
}

function markedWords(explorer) {
  const count = explorer.marked.size;
  if (count === 0) return 'Nothing marked';
  if (count === 1) return '1 image marked';
  return `${count} images marked`;
}

// The explorer's template: a shade over the window that takes the pointer, and the box on it. Framed, it sits inside
// its container, for the Stylebook, and is no dialog. problem is why the last read failed, or null.
export function fileExplorerView(explorer, { onOpen, onPreview, onAttach, onClose, onChange, onScroll = null, problem = null, framed = false }) {
  const actions = { onOpen, onPreview, onChange, onScroll };
  const role = framed ? nothing : 'dialog';
  const modal = framed ? nothing : 'true';
  const attachable = fileExplorerMarked(explorer).length > 0;
  return html`
    <div class="file-explorer" role=${role} aria-modal=${modal} aria-label="Attach images" data-framed=${framed ? 'true' : nothing}>
      <div class="file-explorer-box">
        <nav class="file-explorer-places" aria-label="Places">
          <h2 class="file-explorer-title">Places</h2>
          <ul class="file-explorer-place-list">${explorer.places.map((place) => placeView(explorer, place, onOpen))}</ul>
        </nav>
        <section class="file-explorer-main" aria-label="Folder">
          <header class="file-explorer-head">
            <button class="icon" aria-label="Up a folder" title="Up a folder" ?disabled=${!explorer.parent} @click=${() => onOpen(explorer.parent)}>↑</button>
            <span class="file-explorer-path">${explorer.path || ''}</span>
          </header>
          ${problem ? html`<p class="file-explorer-problem" role="status">${problem}</p>` : nothing}
          ${gridView(explorer, actions)}
          <footer class="file-explorer-foot">
            <p class="file-explorer-help"><kbd class="hint">Space</kbd> mark <kbd class="hint">P</kbd> preview <kbd class="hint">Backspace</kbd> up <kbd class="hint">Esc</kbd> close</p>
            <span class="file-explorer-count">${markedWords(explorer)}</span>
            <button class="button" @click=${onClose}>Cancel</button>
            <button class="button yes" ?disabled=${!attachable} @click=${onAttach}>Attach <kbd>Enter</kbd></button>
          </footer>
        </section>
      </div>
    </div>
  `;
}
