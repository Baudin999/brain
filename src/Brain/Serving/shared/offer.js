// The shared components for the classic scripts of both windows, as globals: markdownView, through which a classic
// script draws markdown, the image viewer's functions, which each window uses on the instance in its own state and the
// Stylebook draws, the permission request, the question, the stop confirm, the process fold, the image thumbnail, the
// file explorer, the agent row, the agent message, the trail step, the Navarone signal, the verdict, the tool call, the
// tool run, the information panel and the tool detail, which brain code's window draws and the Stylebook shows, and
// resolved, the token as the browser resolves it. Besides seam.js and lit.js, it is the only code that writes to window.
import { imageViewer, imageViewerDo, imageViewerKey, imageViewerKeys, imageViewerPaint, imageViewerView } from '/components/image-viewer.js';
import { markdownView } from '/components/markdown.js';
import { permissionRequestView } from '/components/permission-request.js';
import { questionView } from '/components/question.js';
import { stopConfirmView } from '/components/stop-confirm.js';
import { imageThumbView } from '/components/image-thumb.js';
import { fileExplorer, fileExplorerShow, fileExplorerThumb, fileExplorerView } from '/components/file-explorer.js';
import { processFoldView } from '/components/process-fold.js';
import { agentRowView } from '/components/agent-row.js';
import { agentMessageView } from '/components/agent-message.js';
import { trailStepView } from '/components/trail-step.js';
import { navaroneSignalView } from '/components/navarone-signal.js';
import { verdictView } from '/components/verdict.js';
import { toolCallKind, toolCallView } from '/components/tool-call.js';
import { toolRunView } from '/components/tool-run.js';
import { infoPanelView } from '/components/info-panel.js';
import { toolDetailView } from '/components/tool-detail.js';
import { isMarkdownPath } from '/markdown.js';
import { resolved } from '/tokens.js';

Object.assign(window, {
  isMarkdownPath,
  markdownView,
  imageViewer,
  imageViewerDo,
  imageViewerKey,
  imageViewerKeys,
  imageViewerPaint,
  imageViewerView,
  permissionRequestView,
  questionView,
  stopConfirmView,
  imageThumbView,
  fileExplorer,
  fileExplorerShow,
  fileExplorerThumb,
  fileExplorerView,
  processFoldView,
  agentRowView,
  agentMessageView,
  trailStepView,
  navaroneSignalView,
  verdictView,
  toolCallKind,
  toolCallView,
  toolRunView,
  infoPanelView,
  toolDetailView,
  resolved,
});
