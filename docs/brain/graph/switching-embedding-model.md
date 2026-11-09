---
kind: idea
subjects: []
named-by: user
born: 2026-11-03
session: 9c3f198c-50a3-4038-a6cf-5f0f68a4a7b5
---
# Switching the embedding model is a setting: download it with a progress bar, re-embed every node with a progress bar, done

The Settings page offers every embedding profile brain ships. bge-small stays the default; arctic-m is something the user picks, and the default may change later, not now. Picking another model first says what it costs, the download size, how many nodes are embedded again and that it takes CPU, and the user accepts that every embedding is generated again.

Then the model downloads if its files are not there, with a progress bar in the window, and every node is embedded again with a progress bar. The new vectors are built beside the old ones and search stays on the old model until they are complete; a stop or a failed download changes nothing. When they are complete search moves to the new model and the old model's vectors are thrown out, so switching back embeds again. The choice lives in settings.json, so install downloads the model the settings name.

Edges stay out for now: only nodes are embedded. Edges will need vectors in the future.

## Edges
