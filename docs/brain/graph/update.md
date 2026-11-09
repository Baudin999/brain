---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 84ba6677-c4af-44cf-9737-601ab42eaa05
---
# Updating brain is running brain install again after the package manager updated the binary

There is no `brain update` command. After pacman or Homebrew put a new binary and harness in place, you run `brain install` again, and it does the right thing. For a bare binary it unpacks the embedded harness into the data repo's `cache/harness` again; for a packaged binary the package already replaced `share/brain/harness`. Then it puts every link right. A part linked to a checkout by `link.sh` stays linked there. It keeps the data repo, offers the permission mode you have and keeps a CLAUDE.md or settings of your own. It overwrites any edit to an installed harness file. Getting a new binary is the package manager's job.

## Why
A package manager already knows how to fetch and replace a binary, so brain does not do it twice. One command that installs and reinstalls is less to learn than two, and `brain update` already changes a node. The user wants editing installed harness files to be at your own risk: whoever edits them should know a reinstall overwrites them, rather than brain merging their edits.

## Edges
- part-of -> install
