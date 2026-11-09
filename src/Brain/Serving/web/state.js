// The window's global state: what every page shares, held here and written only here. Today that is the settings, brain
// serve's settings.json at the data repo's root: the window reads them once it has its session, and each change is
// saved the moment it is made, which writes the file and commits it there.
//
// A change renders the open page again: this module calls redraw() of the inbox in view, which a page that reads the
// global state registers. Settings redraws its page, Home its sky.
//
// The classic scripts that still read a setting, home.js, perf.js and conversations.js, call setting() and
// firstSitting() as globals, which offer() puts on window until they move.
import { app, offer } from '/seam.js';

// values is every setting as brain serve last said it, null until it has; problem is why it could not.
const settings = {
  values: null,
  problem: null,
};

// A setting's value as brain serve last said it; undefined until it has.
export function setting(key) {
  return settings.values?.[key];
}

// The model and effort a new chat, or else a new sitting, starts with until the user picks otherwise.
export function firstSitting(chat) {
  return setting(chat ? 'chat' : 'sitting');
}

// True once brain serve said what the settings are.
export function settingsKnown() {
  return settings.values !== null;
}

// Why the settings could not be read, or null.
export function settingsProblem() {
  return settings.problem;
}

export async function loadSettings() {
  const { ok, body } = await app.call('/api/settings');
  settings.problem = ok ? null : body.error;
  if (ok) settings.values = body;
  else app.toast(body.error);
  redrawOpenPage();
}

// Saves one change and takes every setting back as brain serve now has them; a refusal keeps what was there.
export async function saveSetting(key, value) {
  const { ok, body } = await app.call('/api/settings', { method: 'POST', body: JSON.stringify({ key, value }) });
  if (ok) settings.values = body;
  else app.toast(body.error);
  redrawOpenPage();
}

function redrawOpenPage() {
  app.shownInbox().redraw?.();
}

offer({ setting, firstSitting });
