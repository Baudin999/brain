// What a session is doing now, in a word, the one reading of it every place that shows it draws from: ended once its
// claude is gone, asking while a permission request of it or a question it asked waits for the user, working while it
// answers, and else waiting for the user's words. A session that asks never reads as waiting, so it shows from any
// other session. Each place says the word in its own words.
export function sessionState(session) {
  if (!session.alive) return 'ended';
  if (session.asks.length > 0) return 'asking';
  if (openQuestions(session).length > 0) return 'asking';
  if (session.working) return 'working';
  return 'waiting';
}

// The questions a session asked through brain ask that wait for the user's answer.
export function openQuestions(session) {
  return session.questions.filter((asked) => asked.state === 'open');
}
