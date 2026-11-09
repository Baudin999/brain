---
name: brain-help
description: List every brain skill in a few lines each, then talk with the user about how to use the brain well. Called only by the user, as /brain-help.
disable-model-invocation: true
---

# Help

Every brain skill starts with `brain`, so `/brain-` autocompletes the whole set. This skill is the menu behind that
prefix. Read the list fresh each time, so it never drifts when a skill is added or renamed.

## The list

Read the frontmatter of every brain skill:

```
awk 'FNR==1{n=0} /^---$/{n++; next} n==1' "${CLAUDE_CONFIG_DIR:-$HOME/.claude}"/skills/brain*/SKILL.md
```

Show each skill in alphabetical order, with no groups:

- The name as `/name` on its own line.
- At most 3 lines under it, in plain words: what it does and when to use it.
- Whether the user calls it or it loads by itself. `disable-model-invocation: true` means the user calls it.

Condense each description. Don't copy it.

## After the list

End with one short line inviting the user to talk about how to use the brain. No list of questions.

When they take it up, ground every answer in the graph, not in assumption. Look it up with `brain lookup`, read with
`brain node`, and walk how we work with `brain walk`, starting from `global:harness`, `global:open-and-closed-modes`
and `global:lazy-context`. The `brain` skill says how to read the graph a rung at a time.
