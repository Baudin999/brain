// The one seam between the window's classic scripts and its modules, while the views move to modules one at a time. A
// classic script shares the page's global scope and a module has a scope of its own, so they reach each other only
// through here.
//
// Into the modules: app holds every name of a classic script that a module uses. It is read once, when the first module
// that imports it runs, and index.html runs every module after the classic scripts that declare these names. A module
// imports app and never names a classic script's global itself.
//
// Out to the classic scripts: offer() puts a module's functions on window, under the names the classic scripts call.
// Besides lit.js and the shared offer.js, it is the only code that writes to window.
//
// The components only brain's window draws are offered here, below: a component file has no side effect on import and
// knows nothing of the Stylebook, until the Stylebook becomes a module. The components both windows share are offered by
// shared/offer.js.
//
// SeamTests holds both rules, and ScriptNameTests fails an offered name that a classic script or another offer() also
// puts on window.
import { askedCost, embedderPicker, embedderPickerKey, embedderPickerView, embedderSwitchRow } from '/components/embedder-picker.js';
import { modelSelector, modelSelectorView } from '/components/model-selector.js';
import { optionList } from '/components/option-list.js';
import { pendingBar } from '/components/pending-bar.js';
import { projectRow } from '/components/project-row.js';
import { waitingCounts } from '/components/waiting-counts.js';

export const app = {
  state,
  registered,
  NODE_REVIEW,
  MODELS,
  EFFORTS,
  $,
  call,
  toast,
  keys,
  plainKey,
  registerInbox,
  showInbox,
  shownInbox,
  renderMain,
  renderQueue,
  showReview,
  templateNode,
  SETTLE_MS,
  inFocus,
  graphChips,
  title,
  ago,
  changeView,
  diff,
  loadPending,
  hostSource,
  currentQuestion,
  visibleQuestions,
  inboxRow,
  blocksView,
  indexed,
  plainCode,
  changeMarks,
  isOpen,
  summonChat,
  openContextMenu,
  contextMenuKey,
};

export function offer(functions) {
  Object.assign(window, functions);
}

offer({ optionList, modelSelector, modelSelectorView, embedderPicker, embedderPickerKey, askedCost, embedderPickerView, embedderSwitchRow, pendingBar, projectRow, waitingCounts });
