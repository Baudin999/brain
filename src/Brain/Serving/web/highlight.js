// Syntax highlighting for the code brain's window draws, by Prism (vendored as /prism.js, loaded with data-manual so it
// colours nothing by itself). The language comes from the file's extension; a path without a known one draws plain.
//
// paintedLines(text, language) is a text cut into lines, tokenised once so a comment or a string over several lines keeps
// its colour on every line. It has the text without the one newline a file ends with, its lines, the offset each starts at,
// paint(from, to), the text between two offsets as strings and coloured spans, and paintLine(i). The lens's marks cut the
// text where they must and paint each piece, so the colour sits under whatever they add.
'use strict';

const LANGUAGES = {
  cs: 'csharp',
  rs: 'rust',
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  ts: 'typescript',
  sh: 'bash',
  bash: 'bash',
  json: 'json',
};

function languageOf(path) {
  const name = String(path || '');
  const dot = name.lastIndexOf('.');
  if (dot < 0) return null;
  return LANGUAGES[name.slice(dot + 1).toLowerCase()] || null;
}

// The classes of a Prism token: its type and its aliases.
function tokenClasses(token) {
  const aliases = [].concat(token.alias || []);
  return ['token', token.type, ...aliases].join(' ');
}

// Every run of the text with the classes of the token it sits in, in order; the runs cover the text without a gap.
function runsOf(tokens, classes, runs) {
  for (const token of tokens) {
    if (typeof token === 'string') {
      runs.push({ text: token, classes });
      continue;
    }
    runsOf([].concat(token.content), tokenClasses(token), runs);
  }
  return runs;
}

// The index of the first run that ends past an offset, by bisection: a file paints line by line.
function firstRunAfter(runs, offset) {
  let low = 0;
  let high = runs.length;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (runs[middle].to <= offset) low = middle + 1;
    else high = middle;
  }
  return low;
}

function paintedLines(text, language) {
  const shown = text.replace(/\n$/, '');
  const lines = shown.split('\n');
  const starts = [];
  let at = 0;
  for (const line of lines) {
    starts.push(at);
    at += line.length + 1;
  }
  const grammar = window.Prism?.languages[language];
  const runs = grammar ? runsOf(window.Prism.tokenize(shown, grammar), null, []) : [{ text: shown, classes: null }];
  at = 0;
  for (const run of runs) {
    run.from = at;
    at += run.text.length;
    run.to = at;
  }
  const paint = (from, to) => {
    const pieces = [];
    for (let i = firstRunAfter(runs, from); i < runs.length && runs[i].from < to; i++) {
      const run = runs[i];
      const piece = run.text.slice(Math.max(from, run.from) - run.from, Math.min(to, run.to) - run.from);
      pieces.push(run.classes ? h('span', { class: run.classes }, piece) : piece);
    }
    return pieces;
  };
  const paintLine = (i) => paint(starts[i], starts[i] + lines[i].length);
  return { text: shown, lines, starts, paint, paintLine };
}
