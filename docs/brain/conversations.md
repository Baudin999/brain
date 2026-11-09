# Conversations

A draft for review before anything is built. Nothing here is in the graph yet.

## Why

In the user's words:

> We have nodes in the graph, but before something is a ruling, before something is understood, we go through the
> process of "understanding". Sometimes, this goes for a long time, and we do not have a clear trail of that
> understanding. We have the context of the agent, but the context gets mixed with all kinds of information.

> CkLang is a great example: discussions about language features are usually long and hard. Those discussions need to
> have a place. Not somewhere where we automatically find it while using brain, that would clog the search and the
> context of the agent using brain, but we need the evidence trail in the project for deep inspection.

A conversation is that place. It is one topic, talked through over days, with the user and an open agent, in brain's
window. It keeps the real why, which today dies with an agent's context. It ends in nodes, landed the normal way, or in
nothing, with its reasons kept.

It replaces idea nodes completely. An idea node today is "a record of thinking together: what we discussed, what we
rejected, and the reasons". That is a conversation squeezed into a node. Once conversations exist, the graph holds only
information.

It is not a work log. A conversation happens only in open mode, and the user is its arbiter.

## The words

- **Conversation.** One topic, open for as long as it takes. It closes only when the user closes it.
- **Sitting.** One stretch of talking: one agent process, from opening the pane to closing it. A conversation has many.
- **Phase.** One turn inside a sitting. The user's phase is their input; the agent's phase is its thinking and output.
- **Trail.** The raw record of a sitting, every phase in order. The short-term layer.
- **Settled.** What we agreed so far, including rules settled while the whole stays open. The long-term layer.
- **Dream.** Distilling unsettled sittings into settled. The user calls it with `brain dream`.

## Where it lives

Every scope has its own conversations. Nothing is central.

- A project's conversations live in that project's `docs/brain/conversations/`.
- Personal and global conversations live in their own scopes in BRAIN_HOME.

They live outside `graph/`, so `brain lookup` never sees them. An agent reaches one on purpose, as a rung below the
node it led to.

```
docs/brain/conversations/<slug>/
  state.md        title, status, born, every sitting with its model, effort and the dream that settled it
  settled.md      the long-term layer
  trails/         one file per sitting, its phases in order
  research/       web results the agent stored
  TEMP/           gitignored sandbox, wiped often
```

Each folder holds its own state in `state.md`. `conversations.json` in the data repo's cache indexes every
conversation in every scope, so the window never walks the folders. It is a cache: brain rebuilds it from scratch
from every `state.md`.

## Phases

A conversation is a sequence of phases.

```
user   input: text at the bottom, highlight comments
agent  thinking, folded away by default
       output: text, mermaid, images, citations
user   input
agent  thinking, output
...
```

Each phase has an id and a timestamp. A user phase can hold text, comments and images. Its comments keep the order
they were added in.

A comment is anchored to a phase id and the quoted text it is about, not to a character offset. When that text is
rewritten, the comment still says what it was about, and the window shows it as orphaned instead of losing it.

Parts of a trail can be deleted, rewritten and challenged. Throwing away is cheap and done often, because git keeps
every committed version.

## Starting one, and moving around

The window is driven from the keyboard.

- **Alt+N, new conversation.** A modal opens with every project in projects.tsv, plus personal, conversations about
  the user, and global, conversations about the process. Pick one and start talking. There is no topic to type: the
  topic flows from the conversation, and the title changes as it does. The agent proposes a title; the user can
  rename it. The folder's slug is fixed at birth from the date and an id, so a new title never moves a folder.
- **Alt+O, open.** A modal lists the active conversations across every scope, filtered as you type. Pick one to
  continue it.
- **Alt+T, new trail.** Only inside a conversation. It ends the current sitting and starts a fresh one, seeded the
  usual way.
- **Model and effort.** Every new sitting starts on Sonnet at low effort. The user can pick Haiku, Sonnet, Opus or
  Fable, and an effort from low, medium, high, xhigh and max. A sitting is one process, so its model and effort are
  fixed for the sitting; changing either is a new trail.

| Keys  | Where                 | What it does                                                        |
|-------|-----------------------|---------------------------------------------------------------------|
| Alt+N | anywhere              | New conversation: pick a project, personal or global                |
| Alt+O | anywhere              | Open: pick one of the active conversations                          |
| Alt+T | inside a conversation | New trail: end the sitting, pick model and effort, start a new one  |
| Alt+C | inside a conversation | Comment on the selected text, in the trail or in settled            |

A terminal session can start one too. A session that hits something too big runs
`brain conversation start <scope|project>` with a seed: the user's words so far, what it understood, what is open. The
seed is the first trail. The conversation waits in Alt+O's list until the user opens it. Nothing starts behind the
user's back.

Every door is one CLI command. The window runs the CLI and never writes a folder itself.

## The agent

When the user opens a conversation, `brain serve` starts one agent for the sitting:

```
claude -p --agent brain --model <pick> --effort <pick> --input-format stream-json --output-format stream-json
       --permission-mode <explicit> --allowedTools <explicit>
```

It streams typed events to the window, the way T3 Code drives its agents, not a terminal in a pane. One sitting per
conversation at a time.

- **Seed.** `settled.md` plus every unsettled sitting. Never `--resume`, so a sitting never grows from the last one.
- **The open agent.** A sitting's agent is the open agent, `--agent brain`, the same as in a terminal: the same
  agent file, role, tools, hooks and rules. There is no separate conversation agent. It reads the code, the graph
  through the brain CLI, older sittings and other conversations, searches the web, and stores what it found in
  `research/`. It never implements. For a proof of concept it sends a builder after the readiness check, which writes
  and runs the spike in TEMP through the machine's guard. The window is just another terminal.
- **Writing.** What the open agent writes anywhere, plus `research/`. It never writes `settled.md`; dreams do, and the user edits it by hand. The server,
  not the agent, writes the trail from the event stream, so no session can edit a trail.
- **Model.** `--model` and `--effort` from the user's pick: Sonnet at low effort unless they chose otherwise.

## Dreaming

`brain dream` distills a conversation's unsettled sittings into `settled.md`. Only dreams and the user's own edits
change `settled.md`.

1. Two cheap dreamers, local AI, Haiku or Sonnet, distill the same sittings independently.
2. If their drafts agree, the result lands in `settled.md`: the conversation only, never the graph.
3. If they diverge or are unsure, Opus gets both drafts and the sittings.

Every dream is logged in BRAIN_HOME and committed: the shape of the task, the models, agreed or escalated, and the
outcome. The window's filter shows escalations by default, with one switch for all. After a few days the user reviews
the log to see where cheap dreamers were collectively wrong, and the process changes.

Two cheap models fail the same way on the same trail, and the next sitting is seeded from `settled.md`. So when the
user opens a conversation, the window first shows what dreams changed since their last sitting.

A dream never runs while a sitting is open on the same conversation.

## Landing

Nothing is special. A node that comes out of a conversation is written through the brain CLI and waits for the user's
yes in the window, like every node. It links back to its conversation.

## Committing

A button in the window, "commit conversation", runs `brain conversation commit <ref>`. It stages that conversation's
folder and nothing else, TEMP excluded, and refuses when anything else is staged. The message is fixed: the
conversation's title and the sitting's date. It commits in the scope's repo: the project, or BRAIN_HOME.

The user presses it, so the user is the gate for what gets published. A public repo publishes its conversations.

The open agent commits the graph as it always does, in a terminal or a sitting. A conversation's folder it does not
commit: it asks, and the user presses the button. Only when the user insists does it commit the folder itself.

## The window

Conversations are their own inbox in the window's rail. Node review stays the first inbox, opens first, and is not
touched. The project chips filter conversations the way they filter every inbox.

```
+------+--------------------------------------------+---------------------------+
| rail | conversation: generics in CkLang           | comments                  |
|      | sittings: 4  unsettled: 2   [dream changes]|                           |
| nodes|--------------------------------------------| 1  "monomorphise at..."   |
| conv |  SETTLED  |  TRAIL                         |    why not at link time?  |
|  3   |           |                                | 2  "traits are..."        |
| navar|  rules    |  user   phase 7                |    this contradicts #4    |
|      |  agreed   |  agent  phase 8                |                           |
|      |  so far   |    thinking (folded)           |                           |
|      |           |    output, with the            |                           |
|      |           |    highlighted text underlined |                           |
|      |           |--------------------------------|                           |
|      |           |  [ text at the bottom    ] send|  [commit conversation]    |
+------+--------------------------------------------+---------------------------+
```

- **Two panes, not a chat.** Settled on one side, the trail arguing about it beside it. Closer to editing a
  manuscript with a collaborator in the margin than to messaging.
- **Highlight and comment.** Select any text in the trail or in settled. It is underlined and the comment appears in
  the side panel. Comments are drafts until the user sends, and go out with the text at the bottom as one phase.
- **Sittings.** A list of the conversation's sittings, the unsettled ones marked, with a count on the conversation.
- **Brain search.** The window's usual search box works inside a conversation, and opens nodes to read beside the
  trail, so the user can check what the graph already holds while talking.
- **Dream changes.** On opening, a side-by-side diff of what dreams changed since the user's last sitting.
- **Pruning.** Dead conversations are pruned in a quick list, triaged like an inbox.

Inspiration, and what to take from each:

- **iA Writer:** typography for long reading. A trail is days of reading, so it reads like a book, not a chat log.
- **Google Docs comments, GitHub PR review:** a challenge anchored to a passage, in the margin, resolved.
- **Readwise Reader:** highlighting while reading, then a distilled view.
- **Zed's agent panel:** tool calls folded to one quiet line that opens.
- **Perplexity:** sources as small inline citations beside the claim.
- **Linear:** speed, keyboard first, calm density.
- **Kaleidoscope:** clean side-by-side diffs.

No UI lands without screenshots the user has judged.

## Security

The agent runs with the user's reach and reads web pages, the same as the open agent in a terminal does today.

- The spawn sets `--permission-mode` and `--allowedTools` itself, independent of the user's settings.
- The open-mode hooks hold it to open mode. A guard keeps every session off the trails, which only the server writes.
- The window's API grows from "answer one staged write" to "drive an agent", so the server's key guards it.
- A separate `claude` operating-system user is the stronger fence the user suggested. It reads projects and
  BRAIN_HOME through a group, and cannot read `cache/serve/key`, `~/.ssh` or anything else private. It costs a
  one-time setup with its own claude login, and differs on macOS and WSL. It is not part of the first slices.

## Retiring idea nodes

Last. The README, `what-brain-is`, `brain check` and `brain migrate` all define the idea kind, and change with it. The
existing idea nodes in every scope move into conversations, project:tool-disputes among them.

## Slices

Each lands whole, works end to end, and is judged by the user before the next starts.

1. **The conversation UI.** The conversation inbox in the rail, Alt+N with projects, personal and global, Alt+O
   and Alt+T, the model and effort picker, the two panes, the bottom input and mermaid rendered in the output,
   against a stand-in agent, so the user can judge how it looks and feels first.
2. **The agent.** `brain serve` starts the real agent for a sitting and streams it into the window, the trail
   written by the server, every pid stopped with brain. It is a full open agent from the start: it reads the code
   and the graph, searches the web into `research/`, and sends builders for proofs of concept.
3. **Phases and comments.** Highlighting, underlining, the side panel, phases with their ids, comments in order.
4. **Git.** The commit button and `brain conversation commit`.
5. **Dreaming.** `brain dream`, its log, the escalation filter and the dream-changes view.

Later: starting from a terminal session, pruning, and retiring idea nodes. With dreaming landed, a sitting's seed is
`settled.md` plus only the unsettled sittings.

## Trail format

A trail is markdown, one file per sitting, written only by the server. Each phase is a header with its number, who,
its timestamp and, for the agent, the model and effort. A comment is a quote of the phase and text it anchors to,
followed by the comment. Images are files in the folder, linked.

```markdown
## phase 7 · user · 2026-11-01T14:02

> 1 on phase 6: "monomorphise at the call site"
why not at link time?

> 2 on phase 6: "traits are structural"
this contradicts #4

And the text typed at the bottom.

## phase 8 · agent · 2026-11-01T14:03 · sonnet low

The answer, with ![diagram](../research/p8-1.svg) for an image.
```

The agent's thinking is not kept. A flag keeps it, for when we want it later. Tool calls are not kept either; what
they found is in the output and in `research/`.

## Lifecycle

When brain stops, its sittings stop, period. The server records the pid of every agent and builder a sitting started,
and when `brain serve` stops or restarts, every one of them stops with it. Whatever was not saved is gone.

## Builders and validators never use conversations

A conversation is thinking, not spec, and its trails hold rejected ideas. `BUILDER.md` and `VALIDATOR.md` say that no
conversation is read or used, and a brief never points at one. What a conversation decided reaches a builder only as
nodes in the graph.
