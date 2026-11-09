---
kind: information
subjects: []
named-by: user
born: 2026-11-03
session: 917c19ce-0bc5-4f03-8fac-3a59bb91e5a7
---
# Brain's settings live in the data repo's settings.json, committed there, and only the judge and dreamer may leave Claude

The Settings page in brain's window sets which model, and at which effort, each agent, the judge, the dreamer's two models, a new chat and a new sitting use, and whether Home shows its starry background. Each change is written to `settings.json` at the data repo's root and committed there with nothing else staged. A missing file or key gives the default.

The agents, builder easy, builder difficult, validator and readiness check, stay Claude, through Claude Code. The judge and the dreamer are swappable between providers: Gemma4 local, GLM-5.3-flash and Claude.

## Why
The user wants a fresh install with one pull to have the same settings, so they live where we commit them. The builder and validator stay Claude Code because the user's preference is Claude; brain may become a harness for other LLMs later. The judge and dreamer run through brain's own model layer, so brain can send them to any model the machine reaches.

## Edges
