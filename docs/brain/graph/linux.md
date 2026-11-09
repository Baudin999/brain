---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# Brain runs on Linux, installed from the AUR on Arch and from Homebrew on other distributions

On Linux the package puts `brain` on PATH and its harness under `share`, and `brain install` links the harness into ~/.claude. Arch users take the AUR package; any other distribution takes the Homebrew formula until it has a package of its own.

## Why
Linux is where the user works, on Omarchy, an Arch distribution, so the AUR comes first. Homebrew covers the other distributions with one formula, so brain needs no package per distribution to start.

## Edges
- part-of -> install
