# brain code

This file covers [`brain code`](cli.md#brain-code): a window that runs Claude Code sessions of brain's agents, in
brain's styling, in place of a terminal. It says how a session starts and moves in, how the window draws a conversation
and its tool calls, how a session's folder shows as its workspace, how live text streams, how a turn is stopped, how
images are attached, the jobs and permission requests a session raises, how an agent asks you questions with
[`brain ask`](cli.md#brain-ask), how the agents a session sent are shown, the window's keys, and how the window's server
reloads onto a new build. The agents themselves are described in [gates.md](gates.md#how-work-lands) and in
`harness/agents`.

## Starting a session

`brain code [agent] [folder]` starts a Claude Code session of an agent in a folder, in brain code's window: the open
agent, `brain`, unless it names one, as `brain code immediate`, and the current folder unless it names one, as
`brain code ~/x`. Any agent file's name works. A positional that names an existing folder, such as `.`, `..`, `~/x` or
a full path, is the folder and any other is the agent, in either order. A folder that does not exist, two folders and
two agents are refused. The session list and each session's head show its full folder.

A session whose claude exits without a transcript, or with an error, is marked ended and says the exit status and
claude's stderr in its conversation; `brain code` prints the same when the server reports it within a second.

There is one brain code window, apart from brain's window. A `brain code` while it is open adds a session to it, shows
that session and brings the window forward. The window lists its sessions on the side, several alive at once, and its
own new session takes an agent and a folder, and refuses one that does not exist.

`brain code [agent] --resume <session-id>` moves a session that runs elsewhere, as in a terminal, into the window: it
continues by its id, in the folder it ran in, with its history drawn. A folder argument that differs from it is
refused. Close the terminal's copy yourself.

## What a session is

Each session is `claude -p --agent <agent>` with stream-json in and out, on your own logged-in claude, through the same
door brain serve's open agent starts through, and Claude Code keeps it as it would in a terminal: a new session gets a
session id of its own with `--session-id`, a moved one continues with `--resume`.

What the window draws is read from the session's transcript in Claude Code's `projects/<folder>/<session-id>.jsonl`, as
brain's own events: your words, the agent's text, each tool call with its result, and each agent it sent. The agent's
thinking and the harness's notes are left out. Text is markdown through the shared markdown component, mermaid
included.

An agent the session sent, a builder or a validator, opens read only, live while it runs, from its own transcript in
`projects/<folder>/<session-id>/subagents/agent-<id>.jsonl`, found by the `toolUseId` in the `.meta.json` beside it. Its
row says it runs until that meta says `finished`, since the call of an agent sent in the background gives back at once.
The transcripts are watched through the system's file events, never polled. A conversation the window shows scrolls to
its newest event as it grows, while it is at its end; nothing an agent does picks a session or opens a page.

## Two flows

A conversation has two flows. The main flow is the conversation itself, in the middle: you ask, the agent answers and
asks questions, you answer. The second flow is what you inspect, and it never crowds the first: the tool calls and the
brain calls.

Tool calls in a row with no main-flow event between them fold into one line that names what they did, such as "Read 4
files, ran 2 commands", with a chevron that shows it opens; opened, it lists each call as a line, and a lone call is one
line. A click on a call shows it in the information panel on the right, never inline: an edit as a diff, a Bash command
as the command and its output, a file read as code with its line numbers, and any other tool's input and output as
readable fields. The panel is closed until you click a call, changes only on your click, and closes with its button.

A brain call is a Bash call that runs one of the `brain` CLI's reading commands, `lookup`, `nearest`, `node` or `walk`.
Its line says it is a brain query and what it asked, and the panel lists the nodes it found, each with its ref and
title, in place of the raw output.

The Settings view, under the session list, has one toggle for each kind, tool calls and brain calls. Off hides that
kind from the conversation entirely, on draws it as above. Both start on. Under them, the agents panel draws as a Flow,
the default, or as a List. The server keeps every choice in the data repo's `cache`, so it survives a restart of the
window. Thinking has no kind yet: Claude Code writes its blocks with
empty text.

## A session's folder, its workspace

Each session's folder has its [workspace](workspace.md): the folder's tree, the reader, git's changes and the git pane,
as the workspace's own server serves them. Sessions in the same folder share one workspace. brain code's window only
shows it, in a frame, and reimplements none of it: its server starts the folder's workspace server the first time it
shows the folder, and that server ends as it does, a few seconds after nothing shows it. Its frame stays while a
session runs in the folder, so the file open and the tree stay as you left them.

A strip down the window's left edge, the strip of brain's window's inboxes (see
[window.md](window.md#the-inbox-strip)), holds the views of the session in view, each label turned to read upward: the
Conversation tab, `Alt+C`, shows its conversation, and the Code tab, `Alt+W`, its folder's workspace in the
conversation's place. Side by side, at the strip's far end, `Alt+V`, shows the two at once, the conversation on the
left, and turns off again. The server keeps the toggle's state in the data repo's `cache`, as it keeps the settings, so
it survives a reload and a restart.

Each tab gives the keys to what it shows, side by side as well: `Alt+C` to the conversation's composer, `Alt+W` to the
workspace. The workspace's keys work while it has the focus, and brain code's while the conversation has it. Side by
side, the pane with the focus carries a line of the focus color along its top, and the workspace's own panes carry
theirs only while it has the focus. The workspace hands brain code the Alt keys it does not bind, so `Alt+C`, `Alt+W`
and `Alt+V` work from it too. Nothing an agent does switches a tab or opens a file.

## Live text

The session's own agent writes into the window as it goes, as in a terminal. Each session runs with
`--include-partial-messages`, and the text of each `text_delta` comes with the conversation, through the same markdown
component, until the transcript holds that message, known by the id `message_start` names. Then it is the message's
event, drawn in the same element, so it neither jumps nor shows twice. The live text ends with the turn, so a text an
interrupt abandoned does not stay. Claude Code streams no subagent's text, so an agent's view grows from its transcript.

## Stopping a turn

While the agent works, the composer says so and offers Stop. Ctrl+C does the same, unless text is selected, which it
then copies. Esc never interrupts: it closes what is in front of you, such as the information panel or a confirm.

Stop and Ctrl+C end the turn with Claude Code's `interrupt` control request over the session's input, and the session
stays for your next words, which you can type and send while it works: claude reads them once the turn ends. A SIGINT
would end claude itself, so it is not used.

Claude Code stops the agents the session sent in the background with the turn. So while any of them still runs, Ctrl+C
or Stop first shows the stop confirm of the Stylebook: it names each agent by its kind and what it was asked, and only
its stop interrupts; Esc or Keep running leaves everything running. With no agent running, the turn ends at once.
Every permission request that waits is denied with the turn. An agent the interrupt stopped has ended: Claude Code
marks its meta `stoppedByUser`. An interrupt claude refuses shows why, and Stop works again. With no turn running,
Ctrl+C does nothing, since the interrupt would stop those agents even then. Brain's window asks with the same stop
confirm before closing a chat stops what it started. Each interrupt, each ask and each keep is a record.

Closing a session asks first, in a modal that names it. Close, or Stop and close while the session works, closes it;
Keep, Esc or a click outside keeps it running.

## Images

You attach images to your words three ways, all through one attach path: drop an image file on the window, paste an
image, or press Alt+P, which opens brain code's file explorer; the composer's Images button opens it too. Several images
go with one message. They wait over the composer as thumbnails, each with its remove button, and go to the session with
your words, or alone, as image content blocks of the user message over claude's stream-json input, as a paste in a
terminal sends them. Each is a png, jpeg, gif or webp of at most 5 MB whose bytes open as that type's do; any other is
refused, with why, before it is sent.

An image in your words is a thumbnail under them in the conversation, and an image a tool's result carries, as a Read
of an image file gives back, is a thumbnail in the call's information panel. Both are read from the transcript, and a
click opens one in the image viewer, the one way to look at an image.

### The file explorer

The file explorer is brain's own component, in brain's styling, laid over the window. It shows folders and images
only, the images as a grid of thumbnails after the folders.

- A click or Enter opens a folder, and Backspace or the up button leaves it.
- A click or Space marks an image, several in any folder.
- P or a double click opens the image under the cursor in the image viewer.
- Enter attaches the marked ones, or the image under the cursor when none is marked.
- Esc closes.

Its side panel lists Pictures, `~/Pictures`, the session's own folder, and the last five folders images were attached
from. It opens in the folder last attached from, and in `~/Pictures` the first time. A folder is remembered only once
an image is attached from it, never for being looked into, as state in the data repo's `cache/code/attached-from` and
not in `settings.json`.

The explorer reads the disk through brain code's server, which serves it folder listings and image files only: a
folder's folders and images, without what a dot hides, and an image file by its full path. It reads an image file only
when its links end in a file with a size, never a FIFO or a device, of at most 32 MB, whose first bytes are those of
the type its name says; anything else, a renamed file or a link to one among them, is refused unread. Each listing,
image read and folder attached from is a record.

## Jobs

The processes the session's agent started that still run, its jobs, wait behind a chip beside Close, as
`3 running, 1 idle`, so the head keeps its height as they come and go. The chip folds out the process fold of the
Stylebook over the conversation, which never moves; Esc or a click outside folds it away, and while it is out the
window reads the sessions each second.

Each job is one line: its command, whole on hover, a mark when it holds a build slot, and running or idle. A job is
idle once its whole tree used no CPU time, user and system from `/proc/<pid>/stat`, for 30 seconds, and the line says
for how long, as `idle 4m`: a hung shell, a sleep and a process waiting for input alike. Where there is no `/proc`, as
on macOS, every job runs.

Each job's Stop ends it and everything below it, a SIGTERM to each and a kill of what is still there two seconds
later, while the session and its turn go on. The server signals only a process that is one of the session's jobs in a
fresh read of the process table, each by its start, so a pid the window sends that is not the session's is refused,
and a job already gone answers as gone. Each stop is a record.

## Permission requests

A tool call that would ask your permission waits for you in the window. Each session carries Claude Code's
`PermissionRequest` hook in its own `--settings`, so the hook applies to brain code's sessions only and never to a
terminal's. The hook is `brain code --permission <session-id>`: it hands the request to the server and waits, up to a
week, for your answer. The harness's PreToolUse hooks run first and still refuse what they refuse.

The request shows under the session's conversation, and under the agent's when a subagent asked, as the permission
request of the Stylebook: the tool and what it will do, a Bash command as code, a file edit as the path and its change,
any other tool as its input. Allow lets the call run. Deny refuses it, and the words you type beside it go back to the
agent as the reason. The session's row says it asks you, so you see it from any other session, and nothing switches
your view.

A request still waiting when its session ends or the window closes is denied, and with no window open the hook denies
at once, so it never hangs. Each request and its answer is a record.

## brain ask

An agent in a session asks you questions with `brain ask`, one JSON document on stdin:

```json
{"wait": true, "questions": [{"text": "Where should the lens keep its index?",
  "options": [{"label": "SQLite", "description": "One file, queried in place"}, {"label": "Files"}],
  "recommend": "SQLite", "why": "One file to back up.", "multiple": false}]}
```

- `wait` is required. `true` means the agent cannot go on without the answers: `brain ask` blocks until you answered
  every question, then prints your answers, so the agent runs it in the background, as it runs `brain land`. `false`
  means it goes on: `brain ask` prints that the questions wait and returns, and once you answer, the server sends your
  answers to the session as your next message.
- You may instead close the questions without answering. A `wait: true` ask then returns at once, prints that you
  closed them and that your next message says what you want, and exits 0. A `wait: false` ask sends the session that
  same line as your next message.
- A question's `text` is markdown.
- `recommend` and `why` are required: the agent's answer, an option's label when there are options, and its reason.
- `options` is optional, each a `label` with an optional `description`. `multiple` lets you choose more than one.
- You may always answer in your own words, beside an option or alone, so `own` is still read and changes nothing.

`brain ask` refuses a document with any problem, an unknown field included, and says each one before anything reaches
the window. It works only in a session of the window, whose server sets `BRAIN_CODE_SESSION` to the session's id;
outside one it refuses and says to ask in prose.

The window draws each ask as a question, in the conversation where it was asked: the text, the options with the
recommended one marked and its reason under them, a field for your own words, always, and Answer and Close. The
session's row says it asks, its open questions are listed over the composer, where they never scroll away, and the Open
questions row on the side opens a page of every open question across the sessions, each naming its session. A question
still open when its session ends is ended, and a waiting `brain ask` fails with why. Each question and its answer is a
record in `perf/code/`.

## The agents a session sent

A session shows every agent it sent in a panel beside its conversation: its kind, what it was sent to do, whether it
runs, finished, was stopped by you or failed, and how long it ran. A builder and each validator whose brief names the
builder's agent id are one piece of work, and a readiness check belongs to the agent it checked.

The panel draws them as a flow by default. The open agent, the session itself, is the root, and a trunk runs down from
it with a branch to each piece of work and to each agent of no work, such as an Explore agent. A piece of work is two
lifelines, the builder's on the left and its validator's on the right, in the order things happened: each readiness
check a diamond in its verdict's color, which says when it asks questions; each agent a block, which says what it does
now while it runs; and between the lifelines one line for each step, whose arrow points the way it went: the builder's
report and each answer to the validator, with their commits, and each round of findings back to the builder. The
builder's worktree is a mark where it was opened, and another where it was removed. The work ends in its landing, with
the range it moved the default branch by and its commits, or says where it stands; under it, the work's Navarone
signals, counted by how they ended. A click on a block or a line opens what the list opens.

The server reads the worktree from the agents' own work: the one Claude Code gave the builder, opened as it started, or
the one the builder's own Bash call added with git's `worktree add`, opened when that call ran. It was removed when a
Bash call of the builder or of a validator of its work ran git's `worktree remove` on it, or once its folder is gone. A
call whose result was an error opened or removed nothing. The work's Navarone signals are read on that worktree.

The List setting draws the agents in a column in the order sent, each piece of work joined at its start. The session's
row counts the agents that still run, so a new agent marks its session without taking the view, and the stop confirm
names those same agents.

Each agent opens in a view made for its kind, with its whole conversation one switch away.

- A readiness check, `brain-ready`, shows its verdict, READY or NOT READY, and its questions.
- A builder and a validator show the trail of their work, top to bottom: the builder's report; each round's findings,
  the message the validator sent the builder with SendMessage, and the builder's answer, what it sent or handed back
  next, with the commits git printed meanwhile; then each validator's report, and a landing with the commits its
  fast-forward put on the default branch.

The server numbers each round: findings by their place among the validators' messages, an answer by how many the
builder had been sent before it, so messages that cross keep their rounds. A validator's findings reach the builder's
transcript as a line whose `origin` is a peer's, which the conversation draws as a message.

Where the work's worktree has Navarone signals in Navarone's event log, the trail shows each and how it ended: fixed,
answered with a reason the validator kept, replaced with the validator's finding, or a clash waiting for a judge in
brain's window, as Navarone's disputes list it (see [sources.md](sources.md#the-navarone-tab)). The log is read again
only after the work's agents ran Navarone. What leads is what happened in words; ids, hashes and the worktree sit
behind one details fold. The agent row, the agent flow, the agent message, the trail step, the Navarone signal and the
verdict are shared components, each in its states in the Stylebook.

## The server, and reloading it

The window's server, `brain code --serve`, is a process of the same binary that `brain code` starts in the background
with its output in the data repo's `cache/code/serve.log`. It runs every session, keeps the list of what runs under each
in memory, and ends a few seconds after its window closes, and every session and what runs under it with it. Closing
or reloading the window asks nothing first. Like a workspace, it takes calls only from its window's session;
`brain code` reaches it with the key in `cache/code`. The window is a Chromium-family browser in app mode with the
window class `brain-code`.

Once `./build.sh` or `./install.sh` has published a newer build beside the server's binary, which the server tells by
the `source.stamp` the build writes, the head of the sessions shows a **Reload brain code** button. It is disabled while
any session runs: a turn, a process or an agent it started, or a permission request or a question waiting for you. It
is highlighted once none does. Install never restarts brain code.

A click writes every session to `cache/code/handover.json`, by its Claude Code session id with its agent and its folder,
and the session in view, with a fresh launch code for the successor in `cache/code`, which only your account can read.
Then the server stops every session and starts its successor on the new build itself, from its own environment, so no
session owns the new server. The successor takes the handover once and resumes each session that was alive with
`--resume`, in the same order; it takes the code once too, in place of opening a window. Once the successor serves, the
old server says `moved` with the successor's launch URL on the window's own event stream, and the window goes there
with `location.replace` and trades the code. So the window you clicked in is the window on the new build, and no second
one opens. A session that had ended stays ended as it was shown, and the window shows the session that was in view.
Only when no window listened does the successor open a new one.

Every call and the server's work are records in the data repo's `perf/code/`, kept 14 days and out of git; they never
leave the machine.

## Keys

Everything is a key. brain code's keys work while its own part of the window has the focus, and none of them collides
with a [workspace's](workspace.md#keys).

| Where | Key | What it does |
|---|---|---|
| a session | `Alt+C` | show its conversation, and give it the keys |
| a session | `Alt+W` | show its folder's workspace, and give it the keys |
| a session | `Alt+V` | show the conversation and the workspace side by side, and back |
| a session | `Ctrl+C` | stop the agent's turn, or copy the text selected |
| anywhere | `Esc` | close what is in front: the information panel, a confirm, the explorer, the viewer, the fold |
| anywhere | `Alt+P` | open the file explorer to attach images |
| anywhere | `Alt+Q` | quote the selected text into the composer |

The file explorer's own keys are in [The file explorer](#the-file-explorer).

## Related

- [gates.md](gates.md): how a builder and a validator land work.
- [window.md](window.md): brain's own window, its chat and its stop confirm.
- [workspace.md](workspace.md): the workspace a session's folder shows.
- [cli.md](cli.md#brain-code): `brain code` and `brain ask`, with their flags.
