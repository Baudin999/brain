// The wrap toggle of a code block in brain code's conversation. A block stays unwrapped and scrolls sideways in its own
// box; when its code is wider than the box, a small toggle sits above it, and pressed, the code wraps. The toggle is
// per block and stays only as long as the drawn prose does. A block that fits shows no toggle, unless it is wrapped.

// The prose with a toggle above each of its code blocks, hidden until the block's code does not fit.
export function withWrapToggles(prose) {
  for (const block of prose.querySelectorAll('pre')) {
    if (block.closest('.diagram')) continue;
    block.before(toggleFor(block));
  }
  return prose;
}

function toggleFor(block) {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'code-wrap';
  button.textContent = 'Wrap';
  button.hidden = true;
  button.setAttribute('aria-pressed', 'false');
  button.addEventListener('click', () => {
    const wrapped = block.toggleAttribute('data-wrap');
    button.setAttribute('aria-pressed', String(wrapped));
  });
  new ResizeObserver(() => {
    button.hidden = !block.hasAttribute('data-wrap') && block.scrollWidth <= block.clientWidth;
  }).observe(block);
  return button;
}
