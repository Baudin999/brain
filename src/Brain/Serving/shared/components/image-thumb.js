// The image thumbnail: an image drawn small, where it was said in a conversation and in the composer while it waits to
// be sent. A click opens it in the image viewer, the one way to look at an image: it sends an open-image event, bubbling,
// with a copy of the image in its detail, as a diagram of the markdown component does. Given onRemove, it has a remove
// button, as the composer's thumbnails do.
import { html, nothing } from '/lit-html.js';

function opened(event, src, name) {
  const image = document.createElement('img');
  image.src = src;
  image.alt = name;
  event.currentTarget.dispatchEvent(new CustomEvent('open-image', { bubbles: true, detail: { node: image } }));
}

function removeView(name, onRemove) {
  if (!onRemove) return nothing;
  return html`<button class="image-thumb-remove" aria-label="Remove ${name}" title="Remove" @click=${onRemove}>×</button>`;
}

// One image by its source, a data URL, named for its alt text and its title.
export function imageThumbView(src, { name = 'image', onRemove = null } = {}) {
  return html`
    <span class="image-thumb">
      <button class="image-thumb-open" title=${name} @click=${(event) => opened(event, src, name)}><img class="image-thumb-image" src=${src} alt=${name}></button>
      ${removeView(name, onRemove)}
    </span>
  `;
}
