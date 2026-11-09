// The window's tokens as the browser resolves them, for what draws without a stylesheet: a canvas, or a library that
// takes plain values, as mermaid does. Both brain's window and a workspace use them.
// A theme color as the browser resolves it, so mermaid gets plain rgb() whatever the theme writes.
export function resolved(name) {
  const probe = document.createElement('span');
  probe.style.color = `var(${name})`;
  document.body.append(probe);
  const color = getComputedStyle(probe).color;
  probe.remove();
  return color;
}

// A text size token as the browser resolves it, in px, so mermaid gets a plain size on the window's scale.
export function resolvedSize(name) {
  const probe = document.createElement('span');
  probe.style.fontSize = `var(${name})`;
  document.body.append(probe);
  const size = getComputedStyle(probe).fontSize;
  probe.remove();
  return size;
}
