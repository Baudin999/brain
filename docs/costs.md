# What brain costs and gives back

This file covers how brain counts its own cost and worth: the usage log it writes as it runs, every kind of event in
it, [`brain stats`](cli.md#brain-stats) that reads it, and the evidence lines that say what would have gone wrong without
each part of brain. The window's Evidence tab shows the same lines (see [window.md](window.md)). The Claude usage brain
spends, its models and their defaults, is summed up in the [README](../README.md) and set on the settings page.

## The usage log

brain logs its own use as it happens, so its cost and its worth are counted, not guessed. The log is the data repo's
`usage/`, one JSON line per event in a file per month, such as `usage/2026-11.jsonl`. It lives outside `graph/`, so
nothing reads it as knowledge, and it is committed with the data repo.

Code writes every line: the CLI, the window's server and a hook. No agent writes one, and the graph door refuses a
session's hand edit of `usage/`. A line is a fact, never a metric.

## Events

- `call`: every run of the CLI. When, the session from `CLAUDE_CODE_SESSION_ID` when there is one, whether an AI
  session or your own terminal called, the command and its arguments, the characters it printed on stdout and stderr,
  how long it ran and its exit status. A `lookup` and a `nearest` add the refs they returned, and a `node` and a `walk`
  the node they opened with the day it was born and the session that wrote it. A project node is named by its
  `projects.tsv` name, so a ref means the same node from every directory. `brain record` is no call of its own.
- `answer`: every answer to a pending write, logged where it is received, once: yes, no, or change, a no with your
  words. It carries the session that staged the write, the title of the first node it writes, your words on a change,
  and for a yes whether it landed.
- `spawn`, `message` and `skill`: the harness's `hooks/usage.sh` runs after every Agent, SendMessage and Skill call and
  hands its JSON to `brain record`. A spawn keeps the session, the agent that spawned, the subagent type, the model,
  the new agent's id and name, and the first line of its brief. A message keeps the session, the sending agent's id and
  type, and the recipient as the sender named it. A validator's message also keeps its text, cut to its first 2048
  characters on a character boundary; no other agent's message keeps text. A skill keeps its name and, for the brain
  skill, its size. The hook never refuses a tool call.
- `readiness`: the same hook runs when a subagent stops. When the readiness check stops, it keeps the session, the
  check's agent id and the first line it stopped on, READY or NOT READY. Its spawn holds the brief.
- `refusal`: every tool call a PreToolUse hook refused. `brain install` runs every PreToolUse hook in the settings it
  writes through `hooks/refusal.sh`, the data repo's own included, and the readiness check's agent file names it before
  its hook, falling back to the hook alone until an install puts `refusal.sh` in place, so its door refuses either way.
  It runs the hook unchanged and passes on its output and exit status. On exit 2 it hands the call's JSON to
  `brain record --refused` in the background, with the hook's name and the first line it said. A record that fails or
  hangs never changes the answer. The line keeps the session, the agent's id and type, the hook, the tool and that
  reason.
- `error`: every failure, never swallowed. A command that fails for any reason but a refusal prints one line, what
  failed, why and where the whole error is kept, and exits 1; a git that does not start, runs past a minute or fails is
  such a failure, never a directory in no repo. A call to the window's server that fails answers 500 with that line,
  which the window shows where you acted. A pending write that cannot land lands none of its files and is refused with
  that line. A hook that cannot find brain, on `PATH` or in `~/.local/bin`, or that brain crashed under, writes the
  line itself and never fails the session. The line keeps what failed, the problem and, from the CLI and the server,
  the whole exception with its stack.
- `told`: [`brain context`](cli.md#brain-context) at the session start ends with one line when errors were written since the
  last `told`: how many and the latest. Then it writes a `told`, so each error is told once.
- `duplicate`, `contradiction` and `missing-edges`: the judge's gate writes one as it refuses a node write. A
  `duplicate` is a write the judge found says the same as one of its nearest entries, a `contradiction` one that
  contradicts one, with what the contradiction is, and a `missing-edges` a node write that carried no edge while the
  judge named edges to its nearest entries, with those edges. Each keeps the session, the write's ref and title, and
  the node it matched or the edges named.

## brain stats

`brain stats` reads the log, all of it or `--since` and `--until` a day, and prints one section per metric; `--json`
prints the same sections as JSON. Every metric is a class in `src/Brain/Stats` that reads the events and returns its
section, and `Metrics.All` lists them, so a metric is added or taken out in one line. Every token count is an estimate,
the characters printed divided by four, and says so.

## The evidence

A metric may also tell one line of the evidence: what would have happened without one part of brain, a fixed sentence
filled from the log, with every caught event it counted as its cases. No line is an estimate and none is written by a
model. A line whose count is zero still shows, except Navarone's two lines, which are absent when the log holds no
event at all. brain's window shows the evidence under the Evidence tab at the bottom of its strip, all time or between
two days, from the same list, and `brain stats` prints each line and its first 5 cases at the top of its section, and
how many more there are; `--json` holds every case.

- **Saves**: your answers to pending writes. Its line: "Without brain, your agent would have gone off the rails X
  times. You corrected it." X counts the no with words; each case is the write's title and your words.
- **Validator**: every message a validator sent to a builder spawned in its session, by id or name, is a round of
  findings, per builder and in total. Its line: "Without brain's validator, X of Y builds would have shipped with
  problems." X counts the builders that got a round, Y the builders spawned; each case is a builder and its rounds.
  Its table lists the 10 builders with the most rounds.
  Each round shows the first 3 non-empty lines of what the validator wrote, and the details page shows each round's
  full recorded text. A round recorded before the text was kept says that nothing was recorded, and it still counts.
- **Knowledge reuse**: every node a `node` or a `walk` opened, its age when read and the sessions it served. Its line:
  "Without brain's graph, your agents would have started from zero X times, not knowing what you decided up to N days
  earlier." X counts the reads of a node written in another session, N is the oldest such node's age when read; each
  case is the node, its age and the session that read it.
- **Readiness**: every verdict of a readiness check. Its line: "Without the readiness check, X briefs would have gone
  to a builder with questions only you could answer." X counts NOT READY; each case is the brief's first line.
- **Guards**: every refusal. Its line: "Without brain's guards, agents would have done X things they must not", with
  the kinds named by the hook that refused them; each case is the hook, the tool and what the hook said.
- **Judge**: every node write the judge's gate refused against the nearest entries. Its line: "Without brain's judge,
  X duplicates and Y contradictions would have entered the graph and Z nodes would have landed with no edges." X counts
  the `duplicate` events, Y the `contradiction` events and Z the `missing-edges` events; a `duplicate` with the same
  session, ref and match as a `contradiction` logged before that session's next `call` counts as the contradiction
  only. Each case is the write's title and the node it matched, with the contradiction, or the edges the judge named.
- **Cost**: calls and estimated tokens per command and in total, calls by AI sessions and by your terminal, and the
  brain skill's fixed cost per load, with its loads and sessions. Its line closes the evidence: the estimated tokens of
  every call and load in the same period, with Navarone's tokens in the total and named as its share when Navarone is
  read.
- **Navarone**: two lines before the cost, read from the `navarone` line of `sources.tsv` (see
  [sources.md](sources.md)). Its fourth column, the events command, prints `{"source":"navarone","events":[...]}`,
  Navarone's event log, and runs as every other source command does. A line without the events column, a command that
  fails or prints what brain cannot read, or a log with no event at all leaves both lines out: brain works without
  Navarone. Brain only reads. A problem is a worktree and an id, since Navarone grants ids per worktree, and counts once
  however many rounds it took; the days picked select the events by their time.
  - Line one: "Without Navarone, X methods would have shipped as they were." X counts the problems with a `fixed`
    event.
  - Line two: "Without the validator reading Navarone's answers, X weak answers would have stood." X counts the
    problems with a `refused-fix` or `refused-reason` event.

  Each case is the symbol in the code's own words and the check, and in the window it opens the problem's story in the
  Navarone tab. The numbers are in agent work, not on master: Navarone logs in worktrees before a commit, and work
  thrown away counts. The cost is a floor, the characters Navarone printed divided by four; the tokens agents spend
  acting on it are not counted. `brain stats` prints each line once with its note and its cases.

Two more metrics tell no line of evidence:

- **Hit rate**: how often a lookup in a session is followed, in the same session, by opening one of the refs it
  returned.
- **Rework**: every message sent to a builder after it started, by the type of agent that sent it.

## Related

- [the-yes.md](the-yes.md): the answers and the judge's refusals the log counts.
- [sources.md](sources.md): the Navarone source and its events.
- [window.md](window.md): the Evidence tab.
