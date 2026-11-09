---
name: brain-quality-review
description: >
  Adaptive, language-agnostic code quality review. Use when the user asks for a
  "quality review", "code review for quality", or any open-ended "is this code
  any good?" question — including specific requests to find god classes,
  hardcoded values, deeply nested control flow, weak/inaccurate tests, or to
  get a senior-developer take. Produces a structured report with file:line
  citations, severity grading, and a senior-developer verdict whose persona
  adapts to the project's domain (game engine, IDE/dev tools, programming
  language/compiler, web backend, frontend, embedded, data/ML). Report-only,
  never edits code. Not for security review or for running formatters/linters.
---

# Quality Review

A six-lens quality pass over a scope the user picks. Subjective + objective signals
combined. Adaptive to the project's domain: a game engine is judged by a senior
gameplay engineer, a compiler by a senior PL engineer, an IDE by a senior
systems engineer, and so on.

**Report-only.** Do not edit code, run formatters, or apply fixes. The output is
a graded report the user reads to decide what to fix.

## Step 1 — Ask for scope

Use `AskUserQuestion`. Always ask, even if the user already hinted at scope.
The options must be exactly:

- **Whole project** — walk the entire source tree under the repo root
- **Uncommitted changes** — files in `git status` (staged + unstaged + untracked source files)
- **Branch vs main/master** — files changed on the current branch since it diverged
- **Other (manual path)** — wait for the user to type a path, file, or glob

If the user picks **Other**, ask a follow-up question for the exact target.

Compute the file list before doing anything else. Exclude:

- `target/`, `node_modules/`, `dist/`, `build/`, `out/`, `.next/`, `__pycache__/`
- Anything tracked by `.gitignore`
- Lock files (`Cargo.lock`, `package-lock.json`, `yarn.lock`, `poetry.lock`)
- Vendored / generated trees (anything under a directory called `vendor/`, `generated/`, `gen/`, or that contains a `DO NOT EDIT` header in its first 5 lines)

State the file count and language breakdown at the top of the report.

## Step 2 — Detect project persona

Read in this order; skip what doesn't exist:

1. `./CLAUDE.md` and any nested `CLAUDE.md` files (project conventions)
2. Manifest: `Cargo.toml`, `package.json`, `pyproject.toml`, `go.mod`,
   `build.gradle*`, `*.csproj`, `Gemfile`, `composer.json`, `mix.exs`
3. `README.md` (first 100 lines)
4. Sample 2–3 source files from likely "core" directories

Synthesize a one-line domain summary, then pick the senior persona:

| Domain signal | Persona |
|---|---|
| game engine, gameplay, graphics, ECS, shader, frame loop | Senior gameplay/engine engineer — cache-aware, hot-loop conscious, allocation-averse |
| IDE, editor, language server, dev tooling, debugger | Senior IDE/systems engineer — latency, keyboard flow, large-file behavior, UI-thread discipline |
| compiler, interpreter, parser, AST, type checker, codegen | Senior PL engineer — representation choices, error reporting/spans, perf of the hot pass |
| web backend, API server, queue, microservice | Senior backend engineer — concurrency, failure modes, idempotency, observability |
| web frontend, React/Vue/Svelte, design system | Senior frontend engineer — re-render cost, state shape, accessibility |
| embedded, firmware, RTOS, MCU | Senior embedded engineer — memory, determinism, ISR discipline |
| data pipeline, ETL, ML training/serving | Senior data/ML engineer — lineage, idempotency, schema drift |

If the project spans multiple domains, pick the persona dominant in the *scope*
being reviewed, not the project as a whole.

State the detected domain and chosen persona at the top of the report. If
detection is genuinely ambiguous, name the top two candidates and pick the
stricter one.

## Step 3 — God classes / oversized units

Per language, identify oversized units with thresholds:

- **Files**: > 500 LOC = `medium`, > 1000 LOC = `high`, > 2000 LOC = `critical`
- **Types** (class / struct + impl / trait impl): > 15 methods OR > 400 LOC of impl
- **Functions / methods**: > 80 LOC OR > 8 parameters OR cyclomatic complexity > 15
- **Flat modules**: > 20 source files in a single directory with no subdivision

Useful commands (adapt per language):

```
# file sizes
find <scope> -type f \( -name '*.rs' -o -name '*.ts' -o -name '*.tsx' -o -name '*.py' -o -name '*.go' -o -name '*.cpp' -o -name '*.cs' -o -name '*.java' \) -exec wc -l {} + | sort -rn | head -50

# Rust: methods per impl
grep -nE '^\s*(pub\s+)?fn\s+' <file>

# JS/TS: methods per class
grep -nE '^\s*(public|private|protected|static|async)?\s*\w+\s*\(' <file>
```

Report each hit as: `path:line — <unit> — <metric> — <severity>` plus a one-line
hint at where the SRP boundary is. **Don't** flag a file just because it's long
if it's a single coherent unit with no other reasonable decomposition (e.g.
a generated grammar, a state machine table). Call that out explicitly in the hint.

## Step 4 — Hardcoded values

Scan for values that should live in config, constants, or environment:

- **Magic numbers** in conditionals/loops, excluding `0`, `1`, `-1`, `2`, common
  bit-widths (`8`, `16`, `32`, `64`), and obvious tuples in graphics/color contexts
- **Identifier strings**: URLs (`http://`, `https://`, `ws://`), absolute paths
  (`/etc/`, `/home/`, `/var/`, `C:\\`, `~/.config/`), email addresses, IPs, ports
- **Embedded credentials** — `critical` always: anything matching `api_key`,
  `token`, `password`, `secret`, AWS keys (`AKIA[0-9A-Z]{16}`), JWT shapes
  (`eyJ[A-Za-z0-9_-]+\.eyJ`), private key headers (`-----BEGIN`)
- **Repeated literals** used in 3+ places (candidate for a named constant)

Skip if clearly fixture / example data, but **don't** skip the `tests/` tree for
credential patterns — those are the most embarrassing leaks.

Report literal value, location, severity, and a one-line suggestion (named
const, env var, config file, secret manager).

## Step 5 — Deeply nested control flow

Find blocks nested > 3 levels (e.g. `if` inside `if` inside `if` inside `if`,
or any equivalent loop/match/try mix).

Heuristic: per-language brace depth, or for Python indentation depth. For each
hit report: function name, file:line, max depth, severity, and a one-line
refactor hint — early return, guard clause, extract function, table-driven
dispatch, polymorphism.

`> 3` is the warn line, `> 5` is `critical`. Switch / match arms don't count
toward depth unless they contain further nesting.

## Step 6 — Functionality (subjective)

Read each unit in scope and ask:

- Does the code do what its name / docstring claims? (Misleading names are
  bugs in slow motion.)
- Are there branches that can never execute? Conditions always true/false?
- `TODO` / `FIXME` / `HACK` / `XXX` markers? Each one is a stale promise — flag
  with severity `info` unless the comment indicates a known correctness gap, in
  which case `high`.
- Silent failure paths: empty `catch {}`, `.unwrap_or(default)` on a critical
  path, `_ = result`, swallowed `Err(_)`, `// ignore` comments. These are
  `high` by default, `critical` if on a write/persistence path.
- Functions defined but never called (dead code). Cross-reference with `grep`.
- Public APIs that take parameters they don't use.
- Inconsistent error handling within the same module (some functions return
  `Result`, others panic, others log-and-continue) — pick one finding per
  module to avoid noise.

## Step 7 — Test accuracy

For every test file in scope (or for whole-project, every test file under
`tests/`, `__tests__/`, `*_test.go`, `*.test.*`, `test_*.py`, etc.):

- **Tautological**: `assert_eq!(x, x)`, `expect(x).toBe(x)`, snapshot tests
  whose only assertion is "renders without throwing"
- **Mock-only**: only the mock boundary is asserted; the system under test
  isn't actually exercised. Look for tests that set up a mock and then assert
  the mock was called, with no behavioral check on the SUT.
- **Happy-path-only**: no error paths, no boundary cases, no empty/null/large
  inputs covered for a non-trivial function
- **Stub tests**: the test exercises only a fixture builder or test helper,
  not real production code
- **Skipped without ticket**: `#[ignore]`, `it.skip`, `xit`,
  `@pytest.mark.skip`, `t.Skip()` — flag any without a referenced issue or
  dated comment explaining why
- **Stale snapshots**: snapshot files whose mtime is significantly older than
  the source they cover, or that haven't been touched in 6+ months in an
  active area

Report file:line, the weakness, severity, and what a stronger assertion would
look like in one sentence.

Tests are where projects lie to themselves. Be the most ruthless here.

## Step 8 — Senior developer verdict

Write a 4–8 paragraph review in the voice of the persona from Step 2. Required
beats:

1. **Honest opener** — one paragraph. Would this pass review at a serious shop
   in this domain? Don't hedge.
2. **Domain-specific issues** — 2–3 things the mechanical checks above can't
   catch, drawn from the persona's instincts. Examples:
   - *Game engine*: allocations inside the frame loop, branchy hot paths,
     cache-unfriendly data layouts, GC pressure in update logic
   - *IDE / dev tools*: blocking work on the UI thread, O(n²) on file size,
     poor incremental update behavior, keyboard-flow breaks
   - *Compiler / language*: error messages that drop source spans, IRs that
     leak frontend concerns into the backend, hot-pass allocation, ad-hoc
     type representations
   - *Web backend*: fan-out without backpressure, unbounded queues, retry
     loops without jitter, missing idempotency keys on mutating endpoints
   - *Frontend*: deep prop drilling masking a state-shape problem, effects
     that depend on derived state, re-renders blowing through memoization,
     a11y omissions
3. **What's good** — name 1–2 things the code does *well*. Real sr devs do not
   only criticize; refusing to acknowledge strengths makes the review easy to
   dismiss.
4. **Directional recommendation** — one paragraph naming the single
   highest-leverage refactor to attempt next. Be specific (which module,
   which method) — not "improve testing."

Take a position. Don't say "it depends." If you have genuinely contradictory
signals, pick a side and name the strongest counter-argument in one sentence.

## Step 9 — Render the report

A single markdown document, written to the chat (not a file unless asked).
Structure exactly:

```
# Quality Review — <scope summary>

**Domain:** <one-line domain summary>
**Persona:** <persona name>
**Files reviewed:** <count> (<language breakdown>)

## Findings

### 1. God classes / oversized units
<findings or "No issues found at this scope.">

### 2. Hardcoded values
...

### 3. Deeply nested control flow
...

### 4. Functionality
...

### 5. Test accuracy
...

## Senior developer verdict — <persona>
<4–8 paragraphs>

## Top 5 things to fix first
1. <severity> — <one-line summary> — <path:line>
2. ...
```

Always include the "Top 5" list ordered by impact × ease. If a lens has no
findings, write "No issues found at this scope." — never omit the heading.

## Output discipline

- **Cite every finding** with `path:line` or `path:line-line`. No exceptions
  for findings within a specific function.
- **Severity tags**: `critical`, `high`, `medium`, `low`, `info`. Be honest —
  most projects have ≤ 5 criticals in a typical scope. If everything is
  critical, the user stops reading.
- **Never invent line numbers.** If you can't cite a specific line, cite a
  range or say "throughout `<file>`."
- **Don't restate lint output** that the project's own gates already produce.
  Add value above the mechanical layer.
- **Parallelize on large scopes.** For whole-project review on a repo with
  > 50 source files, spawn one `Explore` agent per lens (Steps 3–7) and merge
  findings. Don't serialize what can run concurrently.
- **No edits, ever.** Even if a fix is obvious, the user reads the report
  first. If they want fixes, they invoke a different flow.

## When NOT to invoke

- Security review requested → use the project's `security-review` skill.
- "Fix X" or "refactor X" → that's regular editing flow, not this skill.
- Lint / format pass → that's the project's own gates.
- The user is mid-debugging a specific bug → debugging, not reviewing.
