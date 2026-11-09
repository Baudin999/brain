// The tool detail: a tool call's input and output, drawn by the kind of call and meant for the information panel. An edit
// is a diff, what came out and what went in; a Bash command is the command and its output; a file read is the file as
// code with its line numbers; a brain query is the nodes it found, each a ref and a title; and any other tool is its
// input fields and its output, each readable. A call still running says so, and a failed call's output is in the no color.
// The images a result carries, as a Read of an image gives back, follow as thumbnails, which a click opens in the image
// viewer.
//
// The call is brain's shape of a tool event, { name, input, brain }, and result the one of its result event, or null
// while it runs: { text, failed, items, images }, images each a data URL.
import { html, nothing } from '/lit-html.js';
import { imageThumbView } from '/components/image-thumb.js';

// The kind of detail a call has, by its tool.
export function detailKind(call) {
  if (call.brain) return 'brain';
  if (call.name === 'Edit' || call.name === 'MultiEdit') return 'edit';
  if (call.name === 'Bash') return 'command';
  if (call.name === 'Read') return 'read';
  return 'other';
}

function section(label, body) {
  return html`<section class="tool-detail-section"><h3 class="tool-detail-label">${label}</h3>${body}</section>`;
}

function codeView(text, failed = false) {
  return html`<pre class="tool-detail-code" data-failed=${failed ? 'true' : 'false'}>${text}</pre>`;
}

function waiting(result) {
  if (result) return nothing;
  return html`<p class="tool-detail-waiting">Waiting for the result.</p>`;
}

function hasImages(result) {
  return Boolean(result?.images?.length);
}

function imagesView(result) {
  if (!hasImages(result)) return nothing;
  return section('Images', html`<div class="tool-detail-images">${result.images.map((src) => imageThumbView(src))}</div>`);
}

function outputView(result) {
  if (!result) return waiting(result);
  if (!result.text && hasImages(result)) return nothing;
  if (!result.text) return html`<p class="tool-detail-waiting">It gave back nothing.</p>`;
  let label = 'Output';
  if (result.failed) label = 'Failed';
  return section(label, codeView(result.text, result.failed));
}

function linesView(text, way) {
  const mark = way === 'removed' ? '−' : '+';
  return text.split('\n').map((line) => html`<div class="tool-detail-line" data-way=${way}><span class="tool-detail-mark">${mark}</span>${line}</div>`);
}

function oneEdit(old, replacement) {
  return html`<div class="tool-detail-diff">${linesView(old, 'removed')}${linesView(replacement, 'added')}</div>`;
}

// The edits a call made: the one Edit holds, or each of the list a MultiEdit holds.
function editsOf(input) {
  if (input.edits) {
    try {
      return JSON.parse(input.edits).map((edit) => [edit.old_string || '', edit.new_string || '']);
    } catch {
      return [];
    }
  }
  return [[input.old_string || '', input.new_string || '']];
}

function editView(call, result) {
  const input = call.input || {};
  let outcome = waiting(result);
  if (result?.failed) outcome = outputView(result);
  return html`
    ${section('File', html`<p class="tool-detail-path">${input.file_path}</p>`)}
    ${section('Change', html`${editsOf(input).map(([old, replacement]) => oneEdit(old, replacement))}`)}
    ${outcome}
  `;
}

function commandView(call, result) {
  const input = call.input || {};
  let described = nothing;
  if (input.description) described = html`<p class="tool-detail-note">${input.description}</p>`;
  return html`
    ${section('Command', html`${described}${codeView(`$ ${input.command || ''}`)}`)}
    ${outputView(result)}
  `;
}

// A line of what the Read tool gave back is "<number><tab><text>": split into its number and its text, else null.
function numbered(text) {
  const rows = [];
  for (const line of text.split('\n')) {
    const match = /^\s*(\d+)\t(.*)$/.exec(line);
    if (!match) return null;
    rows.push([match[1], match[2]]);
  }
  return rows;
}

function fileView(result) {
  if (!result) return waiting(result);
  if (result.failed || !result.text) return outputView(result);
  const rows = numbered(result.text.replace(/\n+$/, ''));
  if (!rows) return section('File', codeView(result.text));
  const lines = rows.map(([number, text]) => html`<div class="tool-detail-line"><span class="tool-detail-gutter">${number}</span>${text}</div>`);
  return section('File', html`<div class="tool-detail-code tool-detail-file">${lines}</div>`);
}

function readView(call, result) {
  const input = call.input || {};
  return html`
    ${section('Path', html`<p class="tool-detail-path">${input.file_path}</p>`)}
    ${fileView(result)}
  `;
}

function itemView(item) {
  return html`<li class="tool-detail-item"><span class="tool-detail-ref">${item.ref}</span><span class="tool-detail-title">${item.title}</span></li>`;
}

function brainView(call, result) {
  const asked = html`<p class="tool-detail-path">brain ${call.brain} ${call.subject || ''}</p>`;
  return html`${section('Asked', asked)}${section('Found', foundView(result))}`;
}

// What a brain query found: waiting while it runs, its output when it failed, else its nodes.
function foundView(result) {
  if (!result) return waiting(result);
  if (result.failed) return outputView(result);
  const items = result.items || [];
  if (items.length === 0) return html`<p class="tool-detail-waiting">It found nothing.</p>`;
  return html`<ul class="tool-detail-items">${items.map(itemView)}</ul>`;
}

function fieldView([name, value]) {
  return section(name, codeView(value));
}

function otherView(call, result) {
  const fields = Object.entries(call.input || {});
  let input = html`<p class="tool-detail-waiting">It was given nothing.</p>`;
  if (fields.length > 0) input = html`${fields.map(fieldView)}`;
  return html`${input}${outputView(result)}`;
}

const VIEWS = { brain: brainView, edit: editView, command: commandView, read: readView, other: otherView };

// The call's detail, by its kind.
export function toolDetailView(call, result) {
  return html`<div class="tool-detail" data-kind=${detailKind(call)}>${VIEWS[detailKind(call)](call, result)}${imagesView(result)}</div>`;
}
