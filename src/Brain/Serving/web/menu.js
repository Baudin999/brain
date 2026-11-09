// The menu of a code mark with several links: its links on 1 to 9, j and k to move, Enter to pick, Esc to close. The
// Navarone lens opens it on a call that leads to more than one view. brain knows nothing of what a link means: its
// label and query are the source's text.
'use strict';

function openContextMenu(links, anchor) {
  closeContextMenu();
  const menu = h(
    'div',
    { id: 'lens-menu', class: 'lens-menu', role: 'menu', 'aria-label': 'Pick a query' },
    links.map((link, index) =>
      h(
        'button',
        {
          class: 'row menu lens-menu-item',
          role: 'menuitem',
          onclick: () => {
            closeContextMenu();
            link.run();
          },
        },
        index < 9 ? h('kbd', {}, String(index + 1)) : null,
        h('span', { class: 'lens-menu-label' }, link.label),
        link.query ? h('code', { class: 'lens-menu-query' }, link.query) : null,
      ),
    ),
  );
  document.body.append(menu);
  const at = anchor.getBoundingClientRect();
  const below = at.bottom + 4 + menu.offsetHeight <= innerHeight;
  menu.style.left = `${Math.max(8, Math.min(at.left, innerWidth - menu.offsetWidth - 8))}px`;
  menu.style.top = `${below ? at.bottom + 4 : Math.max(8, at.top - 4 - menu.offsetHeight)}px`;
  menu.querySelector('button').focus();
  document.addEventListener('mousedown', closeOutside, true);
}

function closeOutside(event) {
  if (!event.target.closest('#lens-menu')) closeContextMenu();
}

function closeContextMenu() {
  document.removeEventListener('mousedown', closeOutside, true);
  $('lens-menu')?.remove();
}

// The menu's keys while it is open; true when it took the key.
function contextMenuKey(event) {
  const menu = $('lens-menu');
  if (!menu) return false;
  const items = [...menu.querySelectorAll('.lens-menu-item')];
  const at = items.indexOf(document.activeElement);
  const move = (delta) => items[Math.max(0, Math.min(items.length - 1, at + delta))].focus();
  const pick = /^[1-9]$/.test(event.key) ? items[Number(event.key) - 1] : null;
  const actions = {
    j: () => move(1),
    k: () => move(-1),
    ArrowDown: () => move(1),
    ArrowUp: () => move(-1),
    Enter: () => (items[at] || items[0]).click(),
    Escape: () => {
      closeContextMenu();
      $('main').focus();
    },
  };
  const action = pick ? () => pick.click() : actions[event.key];
  if (!action) return false;
  event.preventDefault();
  action();
  return true;
}
