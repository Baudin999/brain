# Other tools' questions

This file covers how other tools put questions before you in brain's window: the `sources.tsv` file that names them,
the JSON a tool prints for its questions and for a context, how the window draws a question as a bench, its keys, where
your answer goes, and the Navarone tab, which is one such tool given a tab of its own. The window itself is in
[window.md](window.md), and your yes on graph writes in [the-yes.md](the-yes.md). The Evidence lines Navarone feeds are
in [costs.md](costs.md).

## sources.tsv

The window also shows other tools' questions for you to judge, each tool in its own inbox. `sources.tsv` in the data
repo names the tools, one line each, its columns separated by tabs:

1. a name;
2. a shell command that prints the tool's open questions as JSON;
3. optionally, the context command, which prints a context for a query;
4. optionally, the events command, which prints the tool's events as JSON;
5. optionally, the code command, which prints the code stored under a hash, given as its argument.

A line named `navarone` is the Navarone tab, described below, and the Evidence tab reads its events column. A blank
line or one that starts with `#` is skipped. The name is letters, digits, dot, dash and underscore.

You write the file yourself: the graph door refuses a session's write to it, because `brain serve` runs every command in
it as you, outside every session.

`brain serve` runs every command through `/bin/sh -c` in the data repo when the window opens and on `F5` or `Ctrl+R`,
all at once, each for at most 15 seconds and 16 MB of output. The question stays in the tool's state, and brain never
copies it into its pending store. A source whose command fails, runs late or prints JSON brain cannot read shows in the
strip as failing, with the reason. The other inboxes and node review go on as before.

## A question

A command prints its open questions:

```json
{"source": "<its name in sources.tsv>", "questions": [{
  "id": "q1", "repo": "/abs/path/of/the/repo", "title": "...", "tags": ["..."], "created": "2026-11-01T08:12:00+01:00",
  "blocks": [
    {"type": "text", "caption": "...", "text": "..."},
    {"type": "code", "caption": "...", "path": "src/x.cs", "startLine": 14, "code": "...", "highlights": [17],
     "marks": [{"start": {"line": 17, "col": 9}, "end": {"line": 17, "col": 31}, "label": "if", "weight": 1,
                "note": "...", "text": "if ((a || b) && c != 12)", "marks": [{"start": ..., "label": "||", ...}]}],
     "tally": {"label": "cyclomatic", "base": 1, "value": 4}},
    {"type": "diff", "caption": "...", "files": [{"path": "src/x.cs", "before": "...", "after": "..."}]},
    {"type": "claims", "caption": "...", "claims": [{"text": "...", "evidence": "...", "holds": false}]},
    {"type": "facts", "caption": "...", "agreement": true, "facts": [{"label": "Limit", "value": "8", "tone": "neutral"}]},
    {"type": "timeline", "caption": "...", "steps": [
      {"label": "...", "when": "2026-10-20T14:30:00+02:00", "tone": "good", "blocks": [<blocks, no timeline>]}]}],
  "choices": [{"id": "a", "label": "..."}]}]}
```

An added file has `before` "", a removed one `after` "". The window draws every block in the given order and shows a
block of another type as one it does not know, with its fields. A field brain does not draw, on a block, a mark, a
tally, a fact or a step, shows as the source sent it, never dropped. A source sends data only, never HTML, and brain
knows the meaning of none of it: a caption, a tag, a label and a fact's value are the tool's text.

`repo` maps to a project of `projects.tsv` by full path, `~` expanded, and the question carries that project's chip; a
path no project has carries "no project".

### Optional fields

A question may also carry five optional fields, and one without them draws as before:

```json
{"group": "self-audit", "commit": "2c0e96878c4c", "stale": true, "context": "method Classifier.Classify class:short",
 "focus": [{"path": "src/Classifier.cs", "lines": [28, 33, 38]}]}
```

- `group`: the inbox lists a source's questions under their group, those without one in one unnamed group.
- `commit`: the commit the question is about, behind the opened question's Details fold.
- `stale`: true shows a banner at the top of the opened question that says it is about older code. The source decides
  that, and brain compares no commits.
- `context`: a query that brain reads and keeps; the window draws no context beside a question.
- `focus`: lines the question is about, a repo-relative `path` and `lines`, the file's own line numbers, any number of
  them. brain lights exactly those lines in any code block of the question, and in any code block of a context, whose
  `path` equals the focus path, and opens each context section that holds one, with its ancestors. It matches on path
  and line numbers alone, and a diff has no start line, so no focus lands in a diff. It refuses a focus without a path
  or with a line below 1.

### Code marks and tallies

A code block's `marks` and `tally` are optional. A mark's lines are the file's own, like `startLine`. Its columns count
UTF-16 code units from 1, and the end column is one past the span. `start`, `end`, `label` and `weight` are required,
and `note`, `text` and nested `marks` are optional.

The window draws each mark where it sits in the code with its weight as a badge, a nested mark inside its parent and a
weight of 0 too. Hovering or focusing a mark shows its label and note. With a tally the window shows
`base + weights = value`, and `brain serve` adds the weights itself: every mark's, nested marks too.

brain says when its sum is not `value`, when a mark's `text` is not what the code reads at its span, and when a mark
lies outside the code shown, reaches outside its parent, overlaps the mark before it or is empty. It says so on the
block, at the top of the question, and on a timeline step that holds it while the step is closed.

### Facts and agreement

A fact's `value` is a string, and brain does no arithmetic on it. A fact's or a step's `tone` is `good`, `bad` or
`neutral`, or absent, and colours it.

A facts block may carry `"agreement": true`. brain then compares the facts' values for exact string equality and
attaches no other meaning to them. Each fact is a chip, and chips of one value share one look. The block shows `=` and
"agree" when every value is equal, and `≠` and "disagree" with each distinct value and its count, such as "1 keep · 1
replace", when they differ. The sign and the word carry it, and colour only adds. A timeline step shows the sign of the
first agreement block in its blocks on its tab, and an item in the rail shows the sign of the question's first
top-level agreement block. Fewer than two facts show no marker, and a block without the field draws as before.

### Timelines

A timeline's steps run oldest first: a step whose `when` comes before an earlier step's is refused, as is a timeline
inside a timeline. `when` is optional and may be null. Each step's blocks draw with the same code as a question's.

## A context

The context command, the third column, prints a context for a query, and the Navarone tab's lens is what runs it.
brain serve runs it in the directory the lens names, the problem's worktree, through `/bin/sh -c` with the query's
words after it: the query is split on whitespace and each word is an argument of its own, passed as `"$@"`, so no shell
parses a word, and `$(...)`, `;`, `*` and quotes reach the command as typed. It runs for at most 15 seconds and 16 MB of
output, and when it fails, runs late or prints JSON brain cannot read, the lens says why with what the command said on
stderr. It prints:

```json
{"commit": "2c0e96878c4c", "sections": [
  {"id": "subject", "title": "...", "open": true, "blocks": [<blocks, as a question's>],
   "sections": [{"id": "result", "title": "...", "blocks": [...], "sections": [...]}]}]}
```

`commit` is optional, and `open` is false when absent. A section may carry `sections`, its children, which nest without
limit; a leaf leaves the field out. The tree is the truth and there is no depth: the lens indents each section by its
depth. Ids are unique across the whole tree, and a mark's `section` may name a section at any depth. Its blocks draw
with the same code as a question's, and brain checks them the same way.

A code block's mark in a context may also carry `queries`, a list of `{"label", "query"}`, and `section`, the id of
another section. A click on a mark with one query shows it at once; with several, as on a line that holds several
calls, a menu picks one. A click on a mark with `section` scrolls to that section. brain refuses a context whose
section ids repeat, whose mark names a section it does not hold, or that offers an empty query.

## The inbox and the bench

The strip lists Home first, then node review, then conversations and dreams, then one inbox per source with how many
questions wait. A source's inbox opens on its list: the questions under their groups, each group with its count, each
row led by the question's title, its tags and project as small chips after it and its id at the end. A source's tags
filter its own inbox, and the project in focus filters it as it filters every inbox.

A question opens as a bench, with the queue column and the reading pane folded away: one question on one screen, read
top to bottom with nothing beside it.

1. A stale question says so at the top.
2. Then the problem: the title large and under it the first text block.
3. Then the code in the order the source sent it: a diff side by side with only the changed lines lit, a markdown
   file's diff in one column wrapped at 120 characters, or a code block with its focus lines lit.
4. Then each further text block in full as a voice under its caption, two voices side by side while each keeps its
   width. Text blocks next to each other read side by side.
5. A facts block and the small print, the source, tags, age, commit, group, id and repo, sit behind one closed Details
   fold.
6. The choices come last, with optional words.

A diff lists its files to jump to, folds unchanged lines, and shows each file with its line numbers before and after.
A timeline shows its steps as a strip with the latest open.

### Keys

The question view's keys are vim motions.

| Where | Key | What it does |
|---|---|---|
| list | `j`, `k` | move the cursor |
| list | `gg`, `G` | first and last question |
| list | `Enter` | open the question |
| open question | `Esc` | back to the list |
| open question | `j`, `k` | next and previous question |
| open question | `gg`, `G` | first and last question |
| both | `l`, `h` | next and previous inbox |
| open question | `]]`, `[[` | move between the parts of a question |
| open question | `n`, `N` | step through the marks with a running total, and back |
| open question | `gt`, `gT` | move between a timeline's steps |
| open question | `v` | open two steps side by side |
| open question | `s` | a diff of code in one column, and back |
| open question | `e` | show whole files |
| open question | `yy` | copy the first code of the part in view |
| open question | `i` | move into the box for your words; `Esc` out of it |
| open question | a number | pick a choice |
| open question | `Ctrl+Enter` | send the verdict, with your words or without |

A diff of code opens side by side. A markdown file's diff is always one column, wrapped at 120 characters, and a diff
of markdown alone offers no `s`. Code and diffs use the free width of the pane, and every code block and diff file has
a Copy button that copies its file name with the code, a fenced block with the path and line range or a unified diff,
from the source's text. A first `g`, `y`, `[` or `]` waits a second for its second key and shows in the foot.

The box for your words is always under the choices, and its draft stays with the question until you send. Keys typed in
it are text. `Esc` takes the picked choice back, and with none picked goes back to the list.

## Where the answer goes

The answer goes to `cache/answers/<source>/<question id>.json`: `{"id", "choice", "words", "answeredAt"}`, `words` null
when you gave none. Only `brain serve` writes there, on your choice for a question the window showed, and only once.
brain never deletes an answer. The question leaves the window once its answer is written and stays gone while the answer
file exists, even while the tool still lists it. The tool collects the answer on its next run.

## The Navarone tab

The `navarone` line of `sources.tsv` is one tab in the strip, Navarone, in place of an inbox of its own; without the
line there is no tab. Navarone is a code-quality tool: it raises signals on the code a builder changed, the builder
answers each, and the validator accepts or refuses the answer. Its count is the escalations that wait for you, so the
strip still means what waits, and with none it shows no count. The tab says it is agent work, not on master: Navarone
logs in a worktree before any commit, and nothing yet links a fix to master. It holds three flows; `1` and `2` switch
between the first two.

- **Escalations** are the questions the line's command lists, where the builder and the validator disagree. They open
  as the bench above and are answered as any source's are. With none waiting the flow says nothing waits.
- **Signals** are every problem Navarone raised, read from the events column, in the project in focus: a problem's
  `repo`, the main checkout, maps to a project of `projects.tsv` as a question's does. A problem is a worktree and an
  id. The rows are one per method, in the order their methods first appear: the method in the code's own words,
  `Type.Method`, its branches small, and how many problems it has. A search box above the rows narrows them by method
  name as you type. In the box letters are text; `Esc` leaves it and keeps the text, `ArrowDown` and `ArrowUp` move
  over the rows and `Enter` opens the row under the cursor. Out of the box, `j` and `k` move and `Enter` opens the
  method with all its problems together, each leading with where its story stands, open, fixed, reasoned, refused,
  accepted, escalated or settled, its check small and last. `Enter` on a problem opens its story top to bottom:
  raised, the builder's answer, a fix as the code before and after side by side or a reason in full, and the
  validator's position, accepted or refused with its finding in full. brain serve fetches the code with the code
  column, for a hash a listed story names, only when a story opens. `Esc` goes from the story back to the method, and
  from the method back to the methods. Nothing in the window opens, selects or scrolls to a method for you.
- **The lens** is a modal over the tab, on a problem or an escalation that names a method: the button in its story or
  in the escalation's head, or `Alt+L`. Down its left side are its views, Class `class:short`, Callers `callers` and
  Calls `expand:1`, on `1`, `2` and `3`. The code sits in the middle, an escalation's finding line lit. A call is a
  mark, and a click makes that method the subject, a menu picking one when the line calls several; the trail above
  names the methods before it, and `u` steps back. brain serve runs the context column for a listed problem as
  `method <Type.Method> <view>`, each word an argument of its own, in the problem's worktree, or its repo once the
  worktree is gone. `Alt+A` summons the tab's chat with the problem, its story and the code the lens shows. `Esc`
  closes the lens where you were.

## Related

- [window.md](window.md): the strip, the project focus and the chat.
- [the-yes.md](the-yes.md#who-can-answer): why only the window answers, and why a session may not write `sources.tsv`.
- [costs.md](costs.md): Navarone's lines in the evidence.
- [brain-code.md](brain-code.md): Navarone signals in a builder's trail.
