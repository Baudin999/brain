# The gate, and how work lands

This file covers how a change to this repo is checked and how it reaches the default branch: the gate,
`tests/run.sh`, and what it runs; the guard every dotnet call goes through; the builder and validator who build and
land work; and the immediate agent for a small change made in place. The project-specific rules for each role are in
[BUILDER.md](../BUILDER.md) and [VALIDATOR.md](../VALIDATOR.md), and the roles themselves in `harness/agents`.

## The gate

```
tests/run.sh
```

It runs, and exits non-zero on any failure of:

- shellcheck on every shell script;
- a build that must report 0 warnings;
- the tests;
- the CLI against an empty temp data dir;
- the hooks' tests, `tests/hooks.sh`;
- the wipe drill, `tests/drill.sh`, which clones this repo, installs it into temp dirs and checks the result.

Two of the tests hold the window's style:

- `StyleTokenTests` fails on a raw color, named colors included, font size or radius in any sheet of brain's window or
  a workspace's but `tokens.css`, and in any style their scripts set.
- `ComponentPlacementTests` fails on a sheet other than `components.css` that sets more than a component's place: its
  margin, flex, grid, position, size or display. It also fails on a control, a link, button, field, summary or element
  in a control's role or reached by its tabindex or its onclick, whose class names no component of `components.css`.

The unit tests build their environment through `tests/Brain.Tests/TempBrain.cs`, and the scripts set `BRAIN_HOME` to a
temp dir. Nothing touches the real graph, and the gate never calls the judge.

## The guard

Run dotnet only through `scripts/dotnet-guard.sh`; `tests/run.sh` and `build.sh` call it themselves. It runs `dotnet`
itself, giving build, test and publish two MSBuild nodes, no node reuse and no shared compiler server, so nothing
outlives the command. When `${XDG_CONFIG_HOME:-~/.config}/brain/guard` is executable it runs
`guard dotnet <args>` instead, so a machine can pin or lock its builds without this repo knowing. `brain install` links
that file from `BRAIN_HOME/harness/guard` (see [install.md](install.md)).

## How work lands

A builder builds in its own worktree under `.claude/worktrees/<name>`, on a branch from the default branch. It commits
there with the gate green and never merges. A validator, a second fresh agent, then reviews the work adversarially and
fixes nothing itself. It sends its findings back to the builder, and this repeats until the validator is satisfied.

The validator then rebases the branch on the default branch, runs the gate again, fast-forwards the default branch with
`git -C <primary> merge --ff-only <branch>` and removes the worktree. When the rebase conflicts, the validator aborts it
and sends the conflict to the builder, which rebases its own branch, resolves it and reports again. The default branch
carries the work, with no merge commits, and the validator reports the hashes as they landed.

The brain, the open agent, never merges code: it verifies the validator's report. Closed agents write nothing to the
graph. Before it sends a builder or a validator, the open agent runs the readiness check, `brain-ready`, on the exact
brief, and sends only on READY.

This is the builder's and the validator's process, taught by the `brain-developer` skill. No hook enforces it: a hook
that refused work in a primary worktree would impose one way of working on everyone who installs brain. What a hook
does enforce is that a closed agent loads the `brain-developer` skill before its first edit, build, test or commit.

## The immediate agent

A small change, such as a line in a bashrc, files moved around or a quick fix, needs no builder and no validator. For
that you launch the immediate agent yourself: `claude --agent immediate`, or an alias such as
`alias bi='claude --agent immediate'` in your own shell file. It makes the change in place, in the current directory or
wherever you point it, in any repo, and commits it when it is done unless you say otherwise.

It uses no worktree, no validator, no readiness check and no `brain-developer` skill: the developer gate lets a session
of agent type `immediate` through without the skill, and only when you launched it, never as an agent the brain sends.
The open agent never sends it and stays as locked as before. The graph door still refuses its direct writes to the
graph, and the machine's own rules, such as CPU pinning, still bind it.

## Related

- [BUILDER.md](../BUILDER.md) and [VALIDATOR.md](../VALIDATOR.md): what each role must know about this repo.
- [harness/CLAUDE.md](../harness/CLAUDE.md): how the open agent and the closed agents work together.
- [brain-code.md](brain-code.md): how brain code's window shows a builder's and a validator's rounds.
- [install.md](install.md): `./link.sh`, and why a harness change goes live when it lands.
