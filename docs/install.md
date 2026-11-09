# Install, update and uninstall

This file covers getting brain onto a machine and off it again: prerequisites, a first install and a restore after a
wipe, what `./install.sh` and `./install-aot.sh` do, every step and every change of [`brain install`](cli.md#brain-install),
the restart of a running window, backups and the install record, updating, [`brain uninstall`](cli.md#brain-uninstall), and
developing brain itself with `./link.sh`. Where everything lands on disk is in [graph.md](graph.md#layout).

## Prerequisites

- bash 3.2 or newer, git, jq and the claude CLI.
- The .NET 10 SDK, to build the CLI: https://dotnet.microsoft.com/download.
- A Chromium-family browser for brain's window, where you answer every graph write (see
  [window.md](window.md#the-browser)).

Linux, macOS with its stock bash 3.2, and WSL are supported, with or without systemd. On Windows, clone brain inside
WSL, not under `/mnt`, and run claude from WSL. `./install.sh` and `brain install` both stop on Windows itself, and
`./install.sh` warns when a WSL checkout is under `/mnt`, where builds and hooks are slow and links may not work.

Each installer names every missing prerequisite before it stops, so one round of installs fixes them all.
`./install.sh` checks bash, git and, when the CLI needs a build, the .NET SDK of the target framework. `brain install`
checks git, jq and the claude CLI.

## A first install, and a restore after a wipe

```
git clone <tool remote> ~/Projects/brain
git clone <your private data remote> ~/.local/share/brain    # restore only; skip on a first install
~/Projects/brain/install.sh
```

There is no flag to clone the data. It becomes the agent's memory with your authority, so an install step that loads
someone else's data would be prompt injection.

## ./install.sh

`./install.sh` is the bootstrap of a checkout, until brain ships as a binary.

1. It asks whether you install as a user or for brain's own development. An empty answer is a user.
2. It checks for bash, git and, when the CLI needs a build, the .NET SDK, and names each one missing.
3. It builds the CLI into `cache/publish` when it is missing or was built from other source. `./build.sh` stamps each
   build with a hash of the source it was built from, the harness included, so after a pull it rebuilds, and when
   nothing changed it builds nothing. The build touches nothing outside the checkout.
4. It hands every argument over: a user's to `brain install --harness-parts none`, a developer's to `./link.sh`.

`./install.sh` takes `--yes` and `--permission-mode <mode>`, which it passes on.

### ./install-aot.sh

`./install-aot.sh` is a trial of the same install with a Native AOT build of the CLI for this machine's runtime, on
Linux. It runs `./build.sh --aot` into `cache/publish`, which needs the .NET SDK and a C toolchain, then hands over to
`brain install` as `./install.sh` does. The AOT build's stamp ends in ` aot`, so `./install.sh` builds the JIT CLI again
over it. A build over the other kind empties `cache/publish` first, so nothing of the other stays; a build over its own
kind publishes over the folder as before.

## brain install

`brain install` is the guided installer, in the terminal. It checks that git, jq and the claude CLI are on PATH, and
stops on Windows itself. Then, in this order:

1. It says what brain is: that Claude Code, in every project, then starts as brain's open agent, which talks with you
   and sends closed agents to build, and never writes code itself; that brain is a beta under development; and what it
   costs: the installed settings default to Opus, and every graph write calls a Sonnet judge.
2. It asks where to keep your personal files, the data repo, `BRAIN_HOME` or `~/.local/share/brain` by default. A data
   repo anywhere else goes into the installed settings.json as `env.BRAIN_HOME`, so Claude Code finds it. Your own
   shell needs `export BRAIN_HOME=<dir>` too, and the install says so.
3. It asks which permission mode Claude Code starts in, for `permissions.defaultMode`. On a first install the menu
   offers the mode your settings.json sets now, but never `auto`: `auto` is yours to pick, never a default. On a
   reinstall it offers the mode you have, `auto` included.
4. It shows every change on disk, each with its diff, or the whole new file where nothing stood. A link that changes
   target, or a copy that becomes a link, says so in words. Where a CLAUDE.md or settings.json of your own stands, your
   edit of brain's settings.json included, it asks: replace it, and yours is backed up for the uninstall, or keep it,
   and it says what brain then lacks. Last it names every target already in place.
5. When a part links into the installed harness, it says once that editing those files is at your own risk: running
   `brain install` again overwrites an unpacked harness, and an update of the package overwrites a packaged one. Then
   it asks `Do you want to continue? [y/N]`. Anything but yes exits, and nothing has been changed.

### The installed harness

Everything brain puts in Claude Code's config directory, `CLAUDE_CONFIG_DIR` or `~/.claude`, is a link, but
settings.json, which it writes. The links go to the installed harness:

- When `../share/brain/harness` exists beside the binary, a package put it there, such as `/usr/share/brain/harness`
  under pacman or `$(brew --prefix)/share/brain/harness` under Homebrew, and brain links to it.
- Otherwise the binary is bare: brain unpacks the harness it carries into the data repo's gitignored `cache/harness`
  and links to that. A build from this checkout is bare too. Nothing generated lands in git.

### Every change it makes

- `CLAUDE.md`, a link to the harness's CLAUDE.md.
- `settings.json`: the harness's settings.json, then `BRAIN_HOME/harness/settings.json` when it exists, then the
  permission mode. Objects merge and lists append. Every PreToolUse command hook runs through `hooks/refusal.sh`, once,
  so each refusal lands in the usage log. Every hook command runs from the real config directory: the `~/.claude/` the
  harness writes becomes `CLAUDE_CONFIG_DIR` when it is set. The harness's settings.json is what makes Claude Code
  start as the open agent: it sets `agent` to `brain` and `model` to Opus, and turns Claude Code's auto-memory off, so
  the graph is the only memory.
- Each child of the harness's `hooks`, `skills` and `agents`, one link each, so `skills/synced` and anything else
  Claude Code manages stays put.
- `~/.local/bin/brain`, a link to `bin/brain`, when the binary was built from a checkout.
- An empty data repo at `BRAIN_HOME`, with `graph/personal`, `graph/global` and `voices`, made with `git init`. An
  existing `BRAIN_HOME` is never touched, so a data repo cloned there first is kept and used.
- Each child of `BRAIN_HOME/harness/hooks`, `skills`, `agents` and `rules`, linked the same way, and
  `BRAIN_HOME/harness/guard` linked to `~/.config/brain/guard`. This is where a machine's own rules live, such as CPU
  pinning, so they survive a reinstall without shipping in this repo.
- The files of the embedding model the data repo's `settings.json` names, each from the url its profile pins, into the
  model's own directory under `cache/model`. The install shows the size and every url first. Each file must match its
  pinned SHA-256, or nothing is kept and search ranks by keywords. `BRAIN_MODEL_URL` serves the same files from a mirror
  at the same paths, over http, https or file; the hashes stay pinned. A model file at the top of `cache/model`, where
  an install before each model had its own directory put it, moves into its model's directory, so it does not download
  again; any other file there is removed.

The judge reads its rules from the installed brain skill, in the config directory, so it works from a package and from
a bare binary alike.

### Restarting a running window

Last, it restarts a running window. When a `brain serve` of `BRAIN_HOME` answers on `BRAIN_PORT`, `brain ui --restart`
asks it to restart. It starts its successor on this build, in its own directory and project, and the window that is
open moves onto the successor in place, so it is the same window on the new build and no second one opens. Only when
no window was open does the successor open one. Every write waiting for your answer stays waiting and shows in the
window, and a `brain land` blocked on one keeps waiting for it. When no window server runs, none starts.

It runs from a Claude Code session too: the successor is a child of the old server, never of the session, and the call
carries nothing that shapes it. When the window cannot restart, such as a `brain serve` from before this step existed,
the install ends by saying so, with the one command that fixes it, such as `kill <pid> && brain ui`.

### Backups, the record and flags

Whatever of yours stood in the way moves to `backups/pre-install-<epoch>/` in the config directory, and nothing of
yours is deleted. What was brain's, such as a link brain made, is replaced without a backup, so a backup directory
holds only what was yours. Each install records what it made, the directories it created and what it backed up in
`backups/brain-install.tsv`. A second run changes nothing.

- `--yes` answers every question with its default and replaces an existing CLAUDE.md and settings.json.
- `--permission-mode <mode>` picks the mode without the menu.
- `CLAUDE_CONFIG_DIR`, `LOCAL_BIN` and `BRAIN_HOME` move the targets, which is how `tests/drill.sh` installs into temp
  dirs.

## Update

Getting a new binary is the package manager's job. Then run `brain install` again: it is the update. It asks what a
reinstall asks, offering the data repo and the permission mode you have, and asks replace or keep for a CLAUDE.md or
settings.json of your own. A bare binary unpacks its harness again, which overwrites edits to the installed harness
files, and every link is put right. A part linked to a checkout by `./link.sh` stays linked there, and only the parts
linked to the installed harness follow it. `--harness-parts none` links every part to the installed harness, as
`./install.sh` does when you pick user.

## Uninstall

Run `brain uninstall` before you remove brain's package: a package manager runs nothing per user, so it cannot take
brain out of your config directory.

`brain uninstall` reads the install record, says what it will undo and asks the same way. It removes every link brain
made that still points where brain pointed it, and every file or directory brain wrote that still holds what brain
wrote, the unpacked harness included; one you edited since moves to `backups/uninstall-<epoch>/`, so no edit is lost.
It removes the embedding model and puts back every file of yours as it was before brain. A later backup of it, such as
your edit of brain's settings.json that a reinstall replaced, stays in `backups/`, and the uninstall names where. Then
it removes the record, every backup directory left empty and every directory brain created that is empty, so an
uninstall that put everything back leaves no `backups/` behind. It never touches the data repo's graph, and says where
it is.

## Developing brain: ./link.sh

Pick development in `./install.sh`, or run `./link.sh` once the CLI is built. It asks which parts of the harness link to
this checkout: CLAUDE.md, agents, hooks and skills, all of them by default. The parts you do not pick link to the
installed harness. It hands over to `brain install --harness <checkout>/harness --harness-parts <parts>`, so one install
record and one `brain uninstall` cover it all; `brain install` itself has no development mode.

A change to a linked part goes live when it lands on the default branch. Build it in a worktree like any other change
(see [gates.md](gates.md)). settings.json is written from the checkout's `harness/settings.json`, not linked, so the
tracked file never changes: run `./link.sh` again after it does, and after a new child of `hooks`, `skills` or `agents`.
Running `brain install` again keeps each part where it links. To switch a part back to the installed harness, run
`./link.sh` again without it, or `./install.sh` and pick user. Commit the data with `git -C` on `BRAIN_HOME`.

## Related

- [graph.md](graph.md#layout): the layout of this repo and of the data repo.
- [gates.md](gates.md): the gate and the guard.
- [search.md](search.md#the-embedding-model): the embedding model the install downloads.
- [cli.md](cli.md#brain-install): `brain install` and `brain uninstall`, with their flags.
