# brain

A personal knowledge graph shared by the user and Claude, stored as plain text in git, plus the Claude Code harness
that uses it. This repo is the tool: the `brain` CLI in C# on .NET 10 and the harness under `harness/`. The data, the
graph, lives in its own repo, `BRAIN_HOME`.

## What to read for which kind of thought

- What brain is: [README.md](README.md). The commands, scopes, nodes, the user's yes, the gate and the install: the
  docs in [docs/](docs/README.md), with every command in [docs/cli.md](docs/cli.md).
- Why brain is shaped this way: the graph in `docs/brain`, read with `brain lookup` and `brain node`. Start with
  `what-brain-is`, `one-door`, `user-says-yes` and `open-source-install`.
- The harness, what installs to `~/.claude`: [harness/CLAUDE.md](harness/CLAUDE.md), and the agents in
  `harness/agents`.
- Building: [BUILDER.md](BUILDER.md). Reviewing and landing: [VALIDATOR.md](VALIDATOR.md).
