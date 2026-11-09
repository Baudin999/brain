// Markdown, read-only, as both brain's window and a workspace draw it. Every character is escaped first; only the tags
// made here reach the page. A caller that reads files passes links, which turns a link's target into the path of a file
// it opens, or null for a target that is no file; without it a link that names no web address stays text. A caller that
// marks lines passes mark, which a workspace uses to colour what git sees changed: mark(lead, from, to) says how the
// block of source lines from to to, the blank lines from lead before it, is marked, as the classes and attributes the
// block's tag takes. A paragraph, a heading, a list item, a table, a quote, a rule and a code block each take it whole.
// A backtick in a pattern is written \x60, so the gates that read this file do not take it for a template.
// Whether a file is markdown, by its path: brain's window wraps a markdown file's diff by it.
export function isMarkdownPath(path) {
  return /\.(md|markdown)$/i.test(path ?? '');
}

export function escapeHtml(text) {
  return text.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

// The text an escaped attribute or link target stands for.
function unescapeHtml(text) {
  return text.replace(/&(amp|lt|gt|quot|#39);/g, (_, name) => ({ amp: '&', lt: '<', gt: '>', quot: '"', '#39': "'" })[name]);
}

function inline(text, links) {
  const kept = [];
  const keep = (made) => {
    kept.push(made);
    return `\u0000${kept.length - 1}\u0000`;
  };
  let out = escapeHtml(text).replace(/\x60([^\x60]+)\x60/g, (_, code) => keep(`<code>${code}</code>`));
  out = out
    .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a class="link accent" href="$2" target="_blank" rel="noopener noreferrer">$1</a>')
    .replace(/&lt;(https?:\/\/[^\s<>&]+)&gt;/g, '<a class="link accent" href="$1" target="_blank" rel="noopener noreferrer">$1</a>');
  if (links) {
    out = out.replace(/\[([^\]]+)\]\(([^\s)]+)\)/g, (whole, words, target) => {
      const path = links(unescapeHtml(target));
      if (path === null) return whole;
      return keep(`<a class="link accent" href="#" data-path="${escapeHtml(path)}">${words}</a>`);
    });
  }
  out = out
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/__([^_]+)__/g, '<strong>$1</strong>')
    .replace(/~~([^~]+)~~/g, '<del>$1</del>')
    .replace(/(^|[^*\w])\*([^*\s][^*]*?)\*(?!\w)/g, '$1<em>$2</em>')
    .replace(/(^|[^\w])_([^_\s][^_]*?)_(?!\w)/g, '$1<em>$2</em>');
  return out.replace(/\u0000(\d+)\u0000/g, (_, i) => kept[Number(i)]);
}

const LIST_ITEM = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const FENCE = /^\s*(\x60{3}|~~~)/;
const RULE = /^\s{0,3}([-*_])(\s*\1){2,}\s*$/;
const TABLE_RULE = /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$/;

function indentOf(line) {
  return /^\s*/.exec(line)[0].length;
}

// The cells of a table row: split at every | that no backslash escapes, the outer ones dropped.
function cells(row) {
  return row
    .trim()
    .replace(/^\|/, '')
    .replace(/(^|[^\\])\|$/, '$1')
    .split(/(?<!\\)\|/)
    .map((cell) => cell.trim().replace(/\\\|/g, '|'));
}

// A table: a header row, a rule row that sets each column's alignment by its colons, then rows until a line without |.
// Alignment is a class, since the page's policy allows no inline style. A wide table scrolls in its own frame.
function table(lines, i, links, attrs) {
  const head = cells(lines[i]);
  const aligns = cells(lines[i + 1]).map((rule) => (/^:-+:$/.test(rule) ? 'center' : /-:$/.test(rule) ? 'right' : null));
  const cell = (tag, text, at) => `<${tag}${aligns[at] ? ` class="align-${aligns[at]}"` : ''}>${inline(text || '', links)}</${tag}>`;
  const rows = [];
  i += 2;
  while (i < lines.length && lines[i].includes('|') && lines[i].trim() !== '') {
    const row = cells(lines[i++]);
    rows.push(`<tr>${head.map((_, at) => cell('td', row[at], at)).join('')}</tr>`);
  }
  const html = `<div${attrs(i - rows.length - 2, i, 'table')}><table><thead><tr>${head.map((text, at) => cell('th', text, at)).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table></div>`;
  return [html, i];
}

// A list, its items at one indent. Lines indented past an item's marker belong to it, nested lists among them, and so
// does a line that runs on without a blank before it. An item with a blank line inside is loose and keeps its
// paragraphs.
function list(lines, i, links, attrs) {
  const first = LIST_ITEM.exec(lines[i]);
  const base = first[1].length;
  const ordered = /\d/.test(first[2]);
  const start = ordered ? parseInt(first[2], 10) : 1;
  const items = [];
  while (i < lines.length) {
    const item = LIST_ITEM.exec(lines[i]);
    if (!item || item[1].length !== base) break;
    const from = i;
    const inside = item[1].length + item[2].length + 1;
    const content = [item[3]];
    i++;
    while (i < lines.length) {
      const line = lines[i];
      if (line.trim() === '') {
        let next = i + 1;
        while (next < lines.length && lines[next].trim() === '') next++;
        if (next < lines.length && indentOf(lines[next]) > base) {
          content.push('');
          i++;
          continue;
        }
        break;
      }
      if (indentOf(line) > base) content.push(line.slice(Math.min(inside, indentOf(line))));
      else if (!LIST_ITEM.test(line) && content[content.length - 1] !== '' && !FENCE.test(line) && !RULE.test(line) && !/^#{1,6}\s/.test(line)) content.push(line.trim());
      else break;
      i++;
    }
    const loose = content.includes('');
    const lead = loose ? 0 : content.findIndex((line, at) => at > 0 && (LIST_ITEM.test(line) || FENCE.test(line)));
    const html = loose
      ? markdown(content.join('\n'), links)
      : lead < 0
        ? inline(content.join(' '), links)
        : inline(content.slice(0, lead).join(' '), links) + markdown(content.slice(lead).join('\n'), links);
    items.push(`<li${attrs(from, i)}>${html}</li>`);
  }
  const tag = ordered ? 'ol' : 'ul';
  return [`<${tag}${ordered && start !== 1 ? ` start="${start}"` : ''}>${items.join('')}</${tag}>`, i];
}

// The class and attributes of a block's tag: its own class, and what mark says of its lines, the blank lines before it
// since the block before.
export function blockAttrs(mark, lead, from, to, own) {
  const marked = mark ? mark(lead, from, to) : { classes: [], attrs: '' };
  const classes = [own, ...marked.classes].filter(Boolean);
  const cls = classes.length ? ` class="${classes.join(' ')}"` : '';
  return cls + marked.attrs;
}

// Markdown as the window draws it: headings, paragraphs, emphasis, links, inline code, fenced code, block quotes,
// nested lists, tables and rules. A fence or a table still streaming in draws as far as it came.
export function markdown(source, links = null, mark = null) {
  const lines = (source || '').replace(/\r\n?/g, '\n').split('\n');
  const html = [];
  let i = 0;
  let lead = 0;
  // The tag attributes of the block from to to, which ends the blank lines a block takes before it.
  const attrs = (from, to, own = '') => {
    const made = blockAttrs(mark, lead, from, to, own);
    lead = to;
    return made;
  };
  const para = [];
  let paraFrom = 0;
  const flush = () => {
    if (para.length) html.push(`<p${attrs(paraFrom, paraFrom + para.length)}>${inline(para.join(' '), links)}</p>`);
    para.length = 0;
  };
  while (i < lines.length) {
    const line = lines[i];
    const fence = FENCE.exec(line);
    if (fence) {
      flush();
      const code = [];
      const close = new RegExp(`^\\s*${fence[1]}\\s*$`);
      const from = i;
      i++;
      while (i < lines.length && !close.test(lines[i])) code.push(lines[i++]);
      i++;
      html.push(`<pre${attrs(from, Math.min(i, lines.length))}><code>${escapeHtml(code.join('\n'))}</code></pre>`);
      continue;
    }
    const heading = /^(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
    if (heading) {
      flush();
      const level = Math.min(5, heading[1].length + 2);
      html.push(`<h${level}${attrs(i, i + 1)}>${inline(heading[2], links)}</h${level}>`);
      i++;
      continue;
    }
    if (RULE.test(line) && !(para.length && /^\s*-+\s*$/.test(line))) {
      flush();
      html.push(`<hr${attrs(i, i + 1)}>`);
      i++;
      continue;
    }
    if (line.includes('|') && i + 1 < lines.length && TABLE_RULE.test(lines[i + 1]) && cells(lines[i + 1]).length === cells(line).length) {
      flush();
      const [drawn, next] = table(lines, i, links, attrs);
      html.push(drawn);
      i = next;
      continue;
    }
    if (LIST_ITEM.test(line)) {
      flush();
      const [drawn, next] = list(lines, i, links, attrs);
      html.push(drawn);
      i = next;
      continue;
    }
    if (/^\s*>\s?/.test(line)) {
      flush();
      const quote = [];
      const from = i;
      while (i < lines.length && /^\s*>\s?/.test(lines[i])) quote.push(lines[i++].replace(/^\s*>\s?/, ''));
      html.push(`<blockquote${attrs(from, i)}>${markdown(quote.join('\n'), links)}</blockquote>`);
      continue;
    }
    if (line.trim() === '') {
      flush();
    } else {
      if (para.length === 0) paraFrom = i;
      para.push(line.trim());
    }
    i++;
  }
  flush();
  // Lines removed after the last block mark the end.
  const end = blockAttrs(mark, lead, lines.length, lines.length, '');
  if (end) html.push(`<div${end}></div>`);
  return html.join('');
}
