---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# On Windows brain runs only inside WSL, installed from Homebrew there; native Windows and choco are not supported

On Windows you run claude and brain from WSL. Inside WSL brain installs from Homebrew as on Linux, and the checkout, if any, lives in the WSL home, not under `/mnt`.

## Why
Native Windows would be a different product: the harness is bash hooks and symlinks, and Claude Code itself runs from WSL there. Choco was left out for the same reason. Under `/mnt` the files sit on the Windows drive, where builds and hooks are slow and links may not work.

## Edges
- part-of -> install
