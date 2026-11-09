// The toggle: a setting on or off. It keeps no state: on says where its knob is, and onToggle runs on a click. The keys
// reach it through the row it sits in, so it takes no tab stop of its own.
import { html } from '/lit-html.js';

export function toggle({ on, label, onToggle }) {
  return html`
    <button class="toggle" role="switch" tabindex="-1" aria-checked=${String(on)}
      aria-label=${label} @click=${onToggle}><span class="toggle-knob"></span></button>`;
}
