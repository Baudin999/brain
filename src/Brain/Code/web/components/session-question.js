// A session's question: the shared question component, for the questions one brain ask of a session asked, wherever
// the window draws them: in the session's conversation and on the page of open questions. actions.answering(session,
// asked) says whether the answer is on its way, actions.onChange() redraws, and actions.onReply(session, asked, replies)
// answers it.
import { questionView } from '/components/question.js';

export function sessionQuestionView(session, asked, { answering, onChange, onReply, onDismiss }) {
  return questionView(asked, {
    key: `${session.id} ${asked.id}`,
    answering: answering(session, asked),
    onChange,
    onAnswer: (replies) => onReply(session, asked, replies),
    onClose: () => onDismiss(session, asked),
  });
}
