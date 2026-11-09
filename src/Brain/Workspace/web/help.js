// Help: a page over the whole window that lists every key of the keymap with its command's title and the focus it
// applies in, grouped by focus. It reads the keymap and the commands as data, so a key added anywhere shows here with
// no edit to this page. Alt+h opens it and the bar finds it by "help"; Esc or Alt+h closes it and gives the keys back
// to the focus they were in. While it is open it holds every key, so nothing acts on the window hidden behind it, and
// it does not open over the image viewer, which would hide it.
'use strict';

// What a focus is called on the page; a focus not named here shows as it is named in the keymap.
const HELP_FOCUS = {
  '*': 'Anywhere',
  tree: 'The tree',
  reader: 'The reader',
  bar: 'The bar',
  git: 'The git view',
  message: 'The commit message',
  viewer: 'The image viewer',
  help: 'Help',
};

// The keymap as groups in the order each focus first appears: its name and its keys with their command's title.
function helpGroups() {
  const groups = new Map();
  for (const binding of keymap) {
    if (!groups.has(binding.focus)) groups.set(binding.focus, []);
    const title = commands.get(binding.command)?.title ?? binding.command;
    groups.get(binding.focus).push({ key: binding.key, title });
  }
  return [...groups].map(([focus, keys]) => ({ name: HELP_FOCUS[focus] ?? focus, keys }));
}

function helpKey(entry) {
  return html`<li class="ws-help-key"><kbd>${entry.key}</kbd><span>${entry.title}</span></li>`;
}

function helpGroup(group) {
  return html`
    <section class="ws-help-group" aria-label=${group.name}>
      <h2 class="ws-help-heading">${group.name}</h2>
      <ul class="ws-help-keys">
        ${group.keys.map((entry) => helpKey(entry))}
      </ul>
    </section>
  `;
}

function helpView(ws) {
  if (!ws.help.open) return nothing;
  return html`
    <div class="ws-help" role="dialog" aria-label="Help">
      <header class="pane-head spread"><h1 class="pane-title">Help</h1></header>
      <div class="ws-help-body" id="help">${helpGroups().map(helpGroup)}</div>
    </div>
  `;
}

// Shows the page, or closes it and gives the keys back to the focus they were in.
function helpToggle(ws) {
  if (ws.help.open) {
    ws.help.open = false;
    ws.focus = ws.help.before;
    return;
  }
  ws.help.before = ws.focus;
  ws.help.open = true;
  ws.focus = 'help';
}

plugin({
  name: 'help',
  state: { open: false, before: 'tree' },
  holds: ['help'],
  commands: [{ name: 'help.toggle', title: 'Help: show or leave the keys', run: helpToggle }],
  keys: [
    { key: 'Alt+h', focus: '*', command: 'help.toggle', when: (ws) => !ws.viewer?.open },
    { key: 'Alt+h', focus: 'help', command: 'help.toggle' },
    { key: 'Alt+h', focus: 'message', command: 'help.toggle' },
    { key: 'Escape', focus: 'help', command: 'help.toggle' },
  ],
  views: [{ slot: 'overlay', draw: helpView }],
});
