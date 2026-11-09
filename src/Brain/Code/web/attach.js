// The one attach path's images: what a drop, a paste and the file explorer each hand over becomes the same image,
// { name, mediaType, data }, its bytes in base64, as the session is sent it. An image of another type, or larger than
// claude takes, is refused here with why, before it waits in the composer; the server checks the same again.
//
// The explorer's thumbnails are drawn small from the image the server read, so a folder of large photos does not keep
// each whole in the window.
const MEDIA_TYPES = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp']);
const LARGEST = 5 * 1024 * 1024;
const MEGABYTE = 1024 * 1024;
const THUMB_PX = 240;
const THUMB_QUALITY = 0.8;

export function dataUrlOf(image) {
  return `data:${image.mediaType};base64,${image.data}`;
}

// How many bytes base64 data holds.
function bytesOf(data) {
  let padding = 0;
  if (data.endsWith('=')) padding = 1;
  if (data.endsWith('==')) padding = 2;
  return (data.length * 3) / 4 - padding;
}

// Why image cannot be attached, or null when it can.
export function attachProblem(image) {
  if (!MEDIA_TYPES.has(image.mediaType)) return `${image.name} is no png, jpeg, gif or webp image`;
  const bytes = bytesOf(image.data);
  if (bytes > LARGEST) return `${image.name} is ${(bytes / MEGABYTE).toFixed(1)} MB, and an image goes up to 5 MB`;
  return null;
}

// Whether a dropped or pasted file is an image the session can be sent, by its type.
export function isImageFile(file) {
  return MEDIA_TYPES.has(file.type);
}

function readAsDataUrl(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

// A dropped or pasted file as an image; a pasted one has no name of its own, so it is named for what it is.
export async function imageOfFile(file) {
  const url = await readAsDataUrl(file);
  const data = url.slice(url.indexOf(',') + 1);
  let name = file.name;
  if (!name) name = 'pasted image';
  return { name, mediaType: file.type, data };
}

function loaded(src) {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('the image could not be drawn'));
    image.src = src;
  });
}

// A small copy of the image at src, as a data URL, at most THUMB_PX on its longer side.
export async function thumbnailOf(src) {
  const image = await loaded(src);
  const scale = Math.min(1, THUMB_PX / Math.max(image.naturalWidth, image.naturalHeight));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
  canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
  canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/webp', THUMB_QUALITY);
}
