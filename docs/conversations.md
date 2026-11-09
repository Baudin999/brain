# Conversations and dreams

This file covers conversations: topics talked through over days with the open agent in brain's window. It says where a
conversation lives on disk, the commands that start, park, rename and remove one, the conversations inbox and its keys,
how a sitting's agent runs, and the two steps of a dream: distilling the sittings into `settled.md`, and the deep dream
that carries what was settled into the graph. The window around it is in [window.md](window.md). The writes a deep
dream stages wait for your yes as [the-yes.md](the-yes.md) describes.

## Where a conversation lives

A conversation is one topic, talked through over days with an agent in brain's window. Each lives in its own folder,
`<slug>/`, in its scope: a project's in its `docs/brain/conversations/`, personal and global ones in the data repo's
`conversations/personal/` and `conversations/global/`. All of them sit outside `graph/`, so lookup and check never see
one. The slug is the day it was born and a random id, fixed at birth, so a new title never moves the folder. A
conversation's ref is `<scope|project>:<slug>`, a project by its name in `projects.tsv`.

The folder holds:

- `state.md`: the title, the status and the day it was born in its frontmatter, and a table of every sitting under
  `## Sittings`: its number, model, effort, when it started, when the next one ended it, and the id of the dream that
  settled it.
- `settled.md`: what the conversation agreed so far. Dreams and your own edits change it, and no agent does.
- `trails/<n>.md`: sitting n's trail, which brain serve alone writes. The graph door refuses every session a write
  there.
- `research/`: what the agent found on the web.
- `TEMP/`: gitignored by the folder's own `.gitignore`, where a builder writes and runs a proof of concept the agent
  sent it for.

Every change rebuilds the data repo's `cache/conversations.json` from scratch, from every `state.md` of every scope; the
window reads that index and never walks the folders. A `state.md` brain cannot read is named in the index and by
`brain conversation list`, never dropped in silence.

## The commands

All of these are subcommands of [`brain conversation`](cli.md#brain-conversation).

- `brain conversation start <scope|project>` starts one with its first sitting.
- `brain conversation sitting <ref>` ends the current sitting and starts the next.
- `brain conversation park <session-id> <scope|project>` parks a terminal session. The open agent runs it with
  `$CLAUDE_CODE_SESSION_ID` when you say to park the session. brain reads Claude Code's transcript of that session,
  keeps only your words and the agent's answers, no thinking, tool calls or tool results, and writes them as the trail
  of sitting 1 of a new conversation, with that sitting already ended. The sitting and its agent phases name the model
  the transcript records, the last one it names that is Haiku, Sonnet, Opus or Fable by name, else the sitting model of
  the settings; `--model` wins over both. A transcript records no effort, so the sitting names none and shows `-` in the
  table, unless `--effort` is given. A dream can settle it, and the next sitting in the window resumes from it. A
  missing or unreadable transcript refuses and writes nothing. Parking does not commit the folder: the button in the
  window does, or `brain conversation commit <ref>`.
- `brain conversation rename <ref> "<title>"` renames it.
- `brain conversation remove <ref>` deletes its whole folder, `settled.md`, `state.md`, the trails, `research/` and
  `TEMP/`, and rebuilds the index; nothing of it is kept as a closed conversation. It refuses, deleting nothing, while a
  dream holds the conversation's lock or brain serve has a live sitting agent on it, which holds a second lock in
  `cache/locks` until the agent ends. Removing does not commit: the deletion waits in the working tree, and
  `brain conversation commit <ref>` commits it as "removed <ref>".
- `brain conversation commit <ref>` commits the conversation's folder.
- `brain conversation list` rebuilds the index and prints it.

A sitting runs on Haiku, Sonnet, Opus or Fable at low, medium, high, xhigh or max effort, on the model and effort the
settings name for a new sitting unless picked: Sonnet at low effort until they are changed.

The open agent never commits a conversation's folder on its own. It asks, and you press the commit button in the
window.

## The conversations inbox

The conversations inbox lists the active conversations, the latest sitting first, and shows only the conversations of
the project in focus, a conversation being of its scope or its project. A conversation shows two panes: on the left the
trail, the sitting's phases in order with the box to write in at the bottom, and on the right `settled.md`.

While the agent answers, its answer streams into the trail with one quiet line above it that says which tool it runs,
and the trail glides down with it; scrolling up lets go, and scrolling back to the bottom follows again. The trail and
settled draw markdown: headings, paragraphs, emphasis, links, inline and fenced code, block quotes, nested lists,
tables and rules, the answer as it streams too, and a mermaid diagram is drawn in the window's colors.

### Comments

The trail's right edge is its comment bar. Each comment anchors to a phase and the text it quotes, never to an offset,
and waits there folded as a numbered marker level with that text. A click, or `]` and `[` for the next and previous,
opens it and marks the text it quotes; `Esc` folds it. When the conversation is at least 1472 pixels wide, the bar
holds the whole card, level with its passage, and nothing floats over the trail. On a narrower screen the bar is thin
and the card floats under the text it quotes, and a switch in the header gives the room to the bar, larger comments, or
to settled, larger settled; the window remembers the choice. A comment whose text the phase no longer holds sits dashed
and says it is orphaned.

To comment, select text in the trail or in settled and press `Alt+C`, write the comment, and `Ctrl+Enter` keeps it as
a draft. Drafts go out with the text at the bottom as one phase, and `Esc` drops the comment being written.

Inside a conversation the reading pane starts closed, so the panes have the room; `/` or `Ctrl+\` opens it. Clicking
the title renames it. Every change goes through the code `brain conversation` runs, so the window never writes a
folder itself.

### Keys

| Keys  | Where                 | What it does                                                        |
|-------|-----------------------|---------------------------------------------------------------------|
| Alt+N | anywhere              | New conversation: pick a project, personal or global                |
| Alt+O | anywhere              | Open: pick one of the active conversations                          |
| Alt+T | inside a conversation | New trail: end the sitting, pick model and effort, start a new one  |
| Alt+C | inside a conversation | Comment on the selected text, in the trail or in settled            |

`Ctrl+Enter` sends what you wrote, `i` moves into the box and `Esc` out of it, and `j` and `k` move to the next and
previous conversation.

## A sitting's agent

A sitting's agent is the open agent, the same one a terminal runs. The first words said in a sitting start it:

```
claude -p --agent brain --model <pick> --effort <pick> --input-format stream-json --output-format stream-json
       --verbose --include-partial-messages --no-session-persistence --permission-mode dontAsk
       --allowedTools Bash,Read,Grep,Glob,Agent,Skill,SendMessage,WebSearch,WebFetch,Edit,Write
```

It runs in the project's repo, or in the data repo for personal and global, with your hooks and rules. The spawn sets
the permission mode and the tools itself: the open agent's own tools, and Edit and Write for the builders it sends.

Its first message is the seed: which conversation it sits in and where its folder is, `settled.md` and the trail of
every sitting no dream has settled yet, then your words. A sitting never resumes another. brain serve writes your phase
to the trail when it is sent and the agent's when its turn ends, its text only; its thinking and its tool calls are not
kept. The window follows an answer by asking brain serve for the conversation past the version it drew, which answers
once the sitting changed.

Talking to the agent needs the window's session, armed as for an answer, and the graph door refuses a session that
names the conversation calls. One agent runs per conversation. Alt+T stops the sitting's agent, and when brain serve
stops or restarts it stops every agent it started, with every process that agent started (see
[window.md](window.md#the-registry-and-the-keeper)).

## Dreams

[`brain dream <ref>`](cli.md#brain-dream), or the dream button in a conversation's header, distills the sittings no dream has
settled into `settled.md`.

1. The dreamer setting's two models, Haiku and Sonnet at medium effort unless set, each draft the whole new
   `settled.md` from `settled.md` and those sittings' trails, and each says first whether it is sure.
2. When both are sure, Sonnet compares the drafts, and when they agree, the second model's draft lands.
3. When they diverge or one is unsure, Opus gets the sittings, both drafts and why, and its draft lands.

The comparison and the escalation run at medium effort. Each model runs as
`claude -p --model <model> --effort <effort> --tools "" --no-session-persistence --setting-sources ""`, as the judge
does with the model and effort its setting names, through the door every `claude` brain starts goes through, so it
stops with the brain process that asked. In brain serve the registry lists it on the dreams tab while it answers.

A dream writes the conversation only, never the graph: `settled.md`, and in `state.md` its id on every sitting it
settled. It logs itself in the data repo's `dreams/<id>.json`: the conversation, the sittings, the shape of the task,
each draft, the comparison, agreed or escalated and why, and `settled.md` before and after. It commits that file alone
in the data repo before it writes the conversation, so a commit that refuses changes nothing, and it refuses before
asking any model when the data repo has anything else staged. A draft with no text is never sure, and an escalation
with no draft fails the dream.

A dream never runs while a sitting is open, one something was said in that no new sitting ended, so a dream settles
only ended sittings. While it runs it holds the conversation's lock in `cache/locks`, and a new sitting, a rename or
your words refuse until it lands.

When you open a conversation whose current sitting holds nothing yet, the window first shows what dreams changed in
`settled.md` since that sitting started, in one column and wrapped; "dream changes" in the header shows it again and
`Esc` goes back. The Dreams inbox lists the log, the escalations by default and every dream at one switch, and opens a
dream whole: `settled.md` before and after in one column, wrapped at 120 characters, each draft, and the comparison.

## The deep dream

`brain dream <ref> --deep`, or the "dream into brain" button beside the dream button, is a dream's second step: it
embeds what the conversation settled into the graph, and runs only when you ask.

When the conversation has sittings no dream settled, a dream settles them first. Then the open agent, the same one a
sitting and a terminal run, starts through the same door and keeper, in the conversation's place's repo, on the deep
dream setting, Opus at medium effort unless set. Its seed names the conversation and its folder, carries `settled.md`,
and gives it the task: search the graph, update the nodes the conversation made stale, forget the ones it made needless
and add what is new. It may read anything in the folder, the trails, `research/` and `state.md`, but `settled.md` comes
first.

Every write it makes is staged through the brain CLI and waits for your yes in the window, one node at a time, so you
can refuse one change and keep the rest. It lands each write before it stages the next, in the foreground, since nobody
comes back to it after its one turn. It refuses while a sitting is open or a dream holds the conversation's lock, and
holds the lock while it runs; the registry lists it on the dreams tab.

It commits each write once it lands, as any open agent does: a project's with `git -C <repo> add docs/brain/graph` and
`git -C <repo> commit` as two separate commands, personal and global ones the same on the data repo, with nothing else
staged. It never writes `settled.md`, `state.md` or a trail, and never commits the conversation's folder.

Once its turn ends, brain stops it with all it started and logs its final words in the data repo's `dreams/<id>.json`
as a deep dream, which brain commits alone. The command prints those words, and the Dreams inbox shows a deep dream
under All, with them. An agent that failed is logged with why, and the command exits 1.

## Related

- [window.md](window.md): the strip, the chat and the registry of running agents.
- [the-yes.md](the-yes.md): how the deep dream's writes wait for you.
- [graph.md](graph.md): what a node is, and why conversations sit outside it.
