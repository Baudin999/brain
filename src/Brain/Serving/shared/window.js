// The plumbing of a small window that its own server runs, a workspace's and brain code's alike: the launch code that
// arrives once in the URL fragment and is traded for a session token, which the tab keeps for a reload; calls that
// carry the token; the event stream the server publishes on; the theme sheet swapped on each Omarchy switch; and the
// window's records, sent to the server in batches. What each window does with its events and records is its own.
'use strict';

const WINDOW_SESSION = 'X-Window-Session';
const WINDOW_EVENTS_RETRY_MS = 2000;

// The token of the window's session, or null before launch and once the window is shut.
const windowState = { token: null };

// Trades the launch code in the URL for a session, or takes the one the tab kept under key for a reload.
async function windowLaunch(key) {
  const code = new URLSearchParams(location.hash.slice(1)).get('launch');
  history.replaceState(null, '', location.pathname);
  let session = null;
  if (code) {
    const response = await fetch('/api/launch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code }) });
    if (!response.ok) return null;
    session = await response.json();
    sessionStorage.setItem(key, JSON.stringify(session));
  } else {
    session = JSON.parse(sessionStorage.getItem(key) || 'null');
  }
  if (session) windowState.token = session.token;
  return session;
}

// Forgets the session the tab kept under key: the window is shut.
function windowForget(key) {
  windowState.token = null;
  sessionStorage.removeItem(key);
}

// A call to the server with the session's token: its status and its body, null when it has none.
async function windowCall(path, options = {}) {
  const headers = { [WINDOW_SESSION]: windowState.token };
  if (options.body) headers['Content-Type'] = 'application/json';
  const response = await fetch(path, { ...options, headers });
  if (response.status === 204) return { ok: true, status: 204, body: null };
  const body = await response.json();
  return { ok: response.ok, status: response.status, body };
}

// Listens on the window's event stream until it is shut: heard(event, data) for each event the server says. ended(why)
// says the stream will not come back: 'session' when the server no longer knows the session, 'gone' when the server
// stopped. isShut says the window is shut, which ends the listening.
async function windowListen(heard, ended, isShut) {
  while (!isShut()) {
    try {
      const response = await fetch('/api/events', { headers: { [WINDOW_SESSION]: windowState.token } });
      if (response.status === 401) {
        ended('session');
        return;
      }
      await windowReadEvents(response.body.getReader(), heard);
    } catch {
      // The server is gone: it ends only once its window went, so it was stopped or it failed.
      ended('gone');
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, WINDOW_EVENTS_RETRY_MS));
  }
}

async function windowReadEvents(reader, heard) {
  const decoder = new TextDecoder();
  let buffer = '';
  while (true) {
    const { value, done } = await reader.read();
    if (done) return;
    buffer += decoder.decode(value, { stream: true });
    let end = buffer.indexOf('\n\n');
    while (end >= 0) {
      const lines = buffer.slice(0, end).split('\n');
      const event = lines.find((line) => line.startsWith('event: '))?.slice('event: '.length);
      const data = lines.find((line) => line.startsWith('data: '))?.slice('data: '.length);
      if (event) heard(event, data);
      buffer = buffer.slice(end + 2);
      end = buffer.indexOf('\n\n');
    }
  }
}

// The new theme sheet goes last, and the older ones go once it loaded; then drawn() draws the page in its colors.
function windowSwapTheme(drawn) {
  const next = document.createElement('link');
  next.rel = 'stylesheet';
  next.href = `/theme.css?at=${Date.now()}`;
  next.dataset.theme = '';
  next.addEventListener('load', () => {
    const sheets = [...document.querySelectorAll('link[data-theme]')];
    for (const old of sheets.slice(0, sheets.indexOf(next))) old.remove();
    drawn();
  });
  next.addEventListener('error', () => next.remove());
  document.head.append(next);
}

// Sends the records in unsent to the server and empties it; closing sends them as the page goes. A batch the server did
// not take is dropped: the log loses it, and the window goes on.
async function windowSendRecords(unsent, closing) {
  if (!windowState.token || unsent.length === 0) return;
  const body = JSON.stringify({ records: unsent.splice(0, unsent.length) });
  await windowCall('/api/records', { method: 'POST', body, keepalive: closing }).catch(() => null);
}

// Says goodbye as the page goes, so the server need not wait out the stream's heartbeat.
function windowBye() {
  if (windowState.token) windowCall('/api/bye', { method: 'POST', keepalive: true });
}
