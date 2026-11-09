// The settings cog: the small button in the session sidebar's header that opens the settings page, current while that
// page is in view. A click opens the page.
import { html } from '/lit-html.js';

export function settingsCogView(current, { onOpen }) {
  return html`
    <button class="icon" aria-label="Settings" title="Settings" aria-current=${current ? 'true' : 'false'} @click=${onOpen}>
      <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"><path d="M6.46 3.03 L6.85 1.30 L9.15 1.30 L9.54 3.03 L10.43 3.40 L11.93 2.45 L13.55 4.07 L12.60 5.57 L12.97 6.46 L14.70 6.85 L14.70 9.15 L12.97 9.54 L12.60 10.43 L13.55 11.93 L11.93 13.55 L10.43 12.60 L9.54 12.97 L9.15 14.70 L6.85 14.70 L6.46 12.97 L5.57 12.60 L4.07 13.55 L2.45 11.93 L3.40 10.43 L3.03 9.54 L1.30 9.15 L1.30 6.85 L3.03 6.46 L3.40 5.57 L2.45 4.07 L4.07 2.45 L5.57 3.40Z"/><circle cx="8" cy="8" r="2"/></svg>
    </button>
  `;
}
