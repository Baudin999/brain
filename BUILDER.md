# Builder, for brain

Extends the `builder` agent for this project. It holds only what is specific to brain.

## Read first

- [README.md](README.md), then the doc of the area you touch in [docs/](docs/README.md): [docs/cli.md](docs/cli.md)
  for the commands, [docs/the-yes.md](docs/the-yes.md), [docs/gates.md](docs/gates.md) and
  [docs/install.md](docs/install.md).
- The graph nodes your brief names, and `one-door`, `user-says-yes` and `open-source-install` when you touch the
  graph door, the pending writes or the install. Read them with `brain node <ref>`.
- [harness/CLAUDE.md](harness/CLAUDE.md) and the agent file you change, when your work is in `harness/`.
- No conversation is read or used, and no brief points at one. A conversation is thinking, not spec, and its trails
  hold rejected ideas. What a conversation decided reaches you only as nodes in the graph.

## The gate and builds

- The gate is `tests/run.sh`: shellcheck, a build with 0 warnings and 0 errors, the tests, the CLI against an empty
  temp data dir, the hook tests and the wipe drill.
- Every dotnet call goes through `scripts/dotnet-guard.sh`. `tests/run.sh` calls it itself.

## Boundaries

- The unit tests build their environment through `tests/Brain.Tests/TempBrain.cs`, and the scripts set `BRAIN_HOME` to
  a temp dir. Nothing touches the real graph, and the gate never calls the judge.

## The graph

- The graph is written only through the `brain` CLI, never by editing a node file.
- Every node write waits for the user's yes. Code that stages or lands a write keeps that wait.

## The harness

- The user's machine is installed for development, with `./link.sh` and every part picked, so `~/.claude` links into
  the primary worktree's `harness/`, and a change to an existing hook, skill, agent or `harness/CLAUDE.md` goes live
  in every session the moment it lands on the default branch. The check comes before the landing. `./link.sh` is
  needed again only for a new child of `harness/hooks`, `skills` or `agents` and for `harness/settings.json`, which is
  written, not linked. A user's install links to the installed harness: the one a package put beside the binary, or
  the one the binary carries, unpacked into the data repo's `cache/harness`.
- A change to how the hooks treat an agent, such as its name or type, gets a test in `tests/hooks.sh`. A prose change
  to an agent file is checked by review.
- `install.sh`, `link.sh`, `brain install` and `brain uninstall` change a machine: test them only through
  `tests/drill.sh`, which installs into temp dirs, and the unit tests, which pass `brain install` an installation of
  temp dirs.
