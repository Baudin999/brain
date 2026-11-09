---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: ade40c7f-2169-46b9-b2fe-cb84a512d940
---
# The brain tool lives in Forgejo's baudin/brain and its data in baudin/brain-data, both mirrored privately to GitHub

The tool, this repo at `~/Projects/brain`, has `origin` `git@forgejo:baudin/brain.git` and is mirrored to `https://github.com/Baudin999/brain.git`. The data, `BRAIN_HOME` at `~/.local/share/brain` with the personal and global graph, voices and the usage log, has `origin` `git@forgejo:baudin/brain-data.git` and is mirrored to `https://github.com/Baudin999/brain-data.git`. Both follow the user's pattern: Forgejo holds the truth and pushes to a private GitHub mirror on every push.

## Why
Neither repo had a remote, so a wipe would have lost the graph, the voices and the usage log. The data is kept apart from the tool, so each needs its own remote and its own backup.

## Edges
