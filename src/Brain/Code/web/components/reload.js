// The reload button: once a newer build of brain code is published, it puts the window's server on it, every session
// with it. It shows only then, as the server says: 'running' while a session runs, when it is disabled and says why,
// and 'ready' once none does, when it is highlighted and a click reloads. Once clicked it takes no second click.
import { html, nothing } from '/lit-html.js';

export function reloadView(reload, reloading, { onReload }) {
  if (!reload) return nothing;
  const ready = reload === 'ready' && !reloading;
  let why = 'A newer build is ready: reload brain code on it, every session with it.';
  if (reload === 'running') why = 'A newer build is ready. A session still runs: reload once none does.';
  return html`
    <button class="button highlight" ?disabled=${!ready} title=${why} @click=${onReload}>Reload brain code</button>
  `;
}
