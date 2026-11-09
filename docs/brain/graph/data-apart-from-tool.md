---
kind: information
subjects: []
named-by: claude
born: 2026-10-18
session: 70b17ada-fe1a-4411-b5c6-e5d3f3577a72
---
# The brain tool is open source in ~/Projects/brain; the memory lives apart, private, and survives a wipe

~/Projects/brain holds the CLI, the UI and the harness: CLAUDE.md, settings, hooks, skills and agents in harness/. CLAUDE.md, hooks, skills and agents are symlinked into ~/.claude, so an edit there is live at once. settings.json is written, not linked: the installer merges harness/settings.json, the data repo's own harness/settings.json when it exists, and the permission mode the user picked, so a change to settings needs ./install.sh again. The personal and global scopes, the decision records, the voices and projects.tsv live in a private git repo at ~/.local/share/brain. That repo may carry a harness/ of its own for what belongs to one machine or one person, such as the user's CPU rules, hook and guard; the installer links it in beside the shipped harness. Each project's scope lives in that project's repo under docs/brain. Everything outside git, such as embeddings and the session trail, is a cache.

./install.sh at the repo root installs the CLI and the harness and creates or keeps the data directory, asking before each step, and ./uninstall.sh puts back what was there before while leaving the data repo alone. A restore is a hand clone of the data repo followed by the install; projects.tsv tells it which projects exist and where. tests/drill.sh proves this by installing a fresh clone into temp directories.

## Why
The tool is open source and the memory is the user's own, so they cannot share a repo. Keeping truth in plain text in git and everything else as a rebuildable cache means a wiped machine comes back from a clone and an install. Settings are generated because the permission mode is each installer's own choice and must not be written into the tracked file.

## Edges
