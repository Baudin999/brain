# brain's docs

This folder is brain's reference. The [README](../README.md) at the root says what brain is and how to start; the files
here say exactly how each part works. Each one opens with a paragraph on what it covers and ends with links to the
files next to it.

## The graph

- [graph.md](graph.md): the one rule, the layout of both repos, scopes, refs, nodes and node writes, subjects, edges,
  weights, the rungs, forgetting and the old-format migration.
- [search.md](search.md): the ranking behind `brain lookup`, tiers and evidence, the embedding model, the nearest
  entries every write is read against, and measuring search.

## Your yes and the windows

- [the-yes.md](the-yes.md): how every write waits for you, the judge, `brain land`, undo, the desktop notification,
  and who can answer.
- [window.md](window.md): brain's window: serving it, node review, the project focus, Answered, Home's constellation,
  the settings page, the chat, the registry of agents and the theme.
- [sources.md](sources.md): other tools' questions in the window, the JSON they print, the bench and its keys, and the
  Navarone tab.
- [conversations.md](conversations.md): conversations talked through over days, sittings, dreams and the deep dream.
- [workspace.md](workspace.md): `brain workspace`, a live window over one project folder, with git.
- [brain-code.md](brain-code.md): `brain code`, Claude Code sessions in a brain window, with `brain ask`.

## Commands

- [cli.md](cli.md): every command, its flags, what it prints and what it refuses.

## Running brain

- [costs.md](costs.md): the usage log, `brain stats`, and the evidence of what brain gave back.
- [install.md](install.md): prerequisites, install, restore after a wipe, update, uninstall, and developing brain with
  `./link.sh`.
- [gates.md](gates.md): the gate, the dotnet guard, how a builder and a validator land work, and the immediate agent.

## Elsewhere in the repo

- [harness/CLAUDE.md](../harness/CLAUDE.md): how the open agent and the closed agents work together; the agents
  themselves are in [harness/agents](../harness/agents).
- [BUILDER.md](../BUILDER.md) and [VALIDATOR.md](../VALIDATOR.md): what a builder and a validator must know about this
  repo.
- [research/](research/): measurements behind design choices, such as
  [research/embedding-models.md](research/embedding-models.md), the comparison of embedding models for search.
- `docs/brain/`: this repo's own graph, the why behind brain's shape. Read it with `brain lookup` and `brain node`,
  never by hand, and write it only through the CLI.
