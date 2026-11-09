---
kind: information
subjects: [aot, release, build]
named-by: user
born: 2026-11-08
session: 39e4eb5e-5b92-425c-8ad0-811e8cd44adf
---
# Released brain binaries are Native AOT; dev and local installs run JIT

A brain binary that ships to strangers, through the AUR or Homebrew, is published with Native AOT: one self-contained native binary that needs no .NET runtime on the machine. A build from a checkout, `./build.sh` and `./install.sh`, stays a framework-dependent JIT build, because it compiles in seconds where an AOT publish takes minutes. `./install-aot.sh` puts an AOT build in `cache/publish` in place of the JIT one, so the user can test it on his own machine; `./install.sh` rebuilds JIT from it.

Native AOT cannot compile for another operating system, so the release binaries are built on GitHub Actions, a Linux runner and a macOS runner, not on the user's machine.

## Why
The user said: "I want AoT when we publish, I want locally and while in dev to run the JIT version." brain is called on every hook, hundreds of times a day, so AOT's start-up matters where it is installed, and a stranger's install should not need the .NET runtime. In development the minutes of an AOT compile cost more than its start-up saves, and they load a machine whose CPU is dying. GitHub Actions is the only way to build the macOS binary Homebrew needs, and it keeps the AOT compile off the user's CPU.

## Edges
- refines -> open-source-install
- refines -> install
- relates-to -> aur
- relates-to -> homebrew
- relates-to -> macos
