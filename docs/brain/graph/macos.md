---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# Brain runs on macOS, installed from Homebrew; macOS is still a work in progress

On macOS brain installs from the Homebrew formula, which puts `brain` in `$(brew --prefix)/bin`, on PATH, and the harness in `$(brew --prefix)/share/brain/harness`. Then `brain install` links the harness into ~/.claude.

macOS support is a work in progress. Brain is built and tested on Linux, and no part of it has run on a Mac: its macOS paths are proven only by reasoning and by tests on Linux. The binary is not signed, since the user has no Apple developer account. Where macOS cannot meet a guarantee Linux meets, such as every process stopping with brain, brain gets as close as macOS allows and the README says what can still escape there.

## Why
Homebrew is how macOS developers install command line tools. A binary linked into ~/.local/bin, as the old installer did, is not on macOS's default PATH, so every `brain` command the skills run would fail. The user cannot test on a Mac and has no developer account to sign the binary, so macOS stays a work in progress; in the user's words, "if MacOS does not fully meet the guarantee, we must get as close as possible."

## Edges
- part-of -> install
