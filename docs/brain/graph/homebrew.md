---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# On macOS, and on Linux and WSL outside Arch, brain installs from Homebrew

The Homebrew formula puts `brain` in `$(brew --prefix)/bin` and the harness in `$(brew --prefix)/share/brain/harness`, `/opt/homebrew` on Apple Silicon, a path Homebrew keeps stable across versions. After `brew install` you run `brain install`. Homebrew has no remove hook, so `brain uninstall` comes before `brew uninstall`.

## Why
Homebrew runs on macOS, Linux and WSL, so one formula covers every platform the AUR does not. Its stable prefix lets the links in ~/.claude survive an upgrade.

## Edges
- part-of -> install
- runs-on -> macos
- runs-on -> linux
- runs-on -> wsl
