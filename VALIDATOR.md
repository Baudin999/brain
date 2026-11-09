# Validator, for brain

Extends the `validator` agent for this project. It holds only what is specific to brain.

## Read first

- [README.md](README.md), the doc of the area in [docs/](docs/README.md), and the graph nodes your brief names. Add `one-door`, `user-says-yes` and
  `open-source-install` when the change touches the graph door, pending writes or the install. Read them with
  `brain node <ref>`.
- [harness/CLAUDE.md](harness/CLAUDE.md) for a change in `harness/`.
- No conversation is read or used, and no brief points at one. A conversation is thinking, not spec, and its trails
  hold rejected ideas. What a conversation decided reaches you only as nodes in the graph.

## The gate

- The gate is `tests/run.sh`: shellcheck, a build with 0 warnings and 0 errors, the tests, the CLI against an empty
  temp data dir, the hook tests and the wipe drill.
- Every dotnet call goes through `scripts/dotnet-guard.sh`.

## What to check

- No code path writes a node without the user's yes, and no write to the graph bypasses the CLI.
- A change to an existing harness file goes live in every session the moment it lands, because `~/.claude` links into
  the primary worktree on the user's machine, installed for development with `./link.sh`. Check it before you land. A
  change to how the hooks treat an agent, such as its name or type, has a test in `tests/hooks.sh`. A prose change to
  an agent file is checked by review.
- A new child of `harness/hooks`, `skills` or `agents`, or a change to `harness/settings.json`, needs `./link.sh` to
  reach the user's machine, and a rebuild to reach the harness a bare binary unpacks.
- A change to `install.sh`, `link.sh`, `brain install` or `brain uninstall` still passes `tests/drill.sh` and keeps its
  prompts and its undo record.
- The README and the docs in `docs/` still say what the code does.
