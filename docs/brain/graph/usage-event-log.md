---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: ade40c7f-2169-46b9-b2fe-cb84a512d940
---
# Brain logs what happens as events, and metrics read the event log

Every brain CLI call, every answer to a pending write, every agent spawn, message, skill load, readiness verdict and hook refusal appends one event to the data repo's usage log, `usage/YYYY-MM.jsonl`. Writers record facts only: the CLI writes its calls and answers, and hooks write the harness events. Interpretation lives in metrics: each implements `IMetric` over the parsed events, and the metrics in use are registered in one list that both `brain stats` and the window read. Spawning a builder that already exists reads as rework; a validator messaging its builder reads as a findings round. Tokens are estimates, characters divided by four.

## Why
The user wants inspection: facts in one general log can be parsed whenever we want, so a question thought of later is answered from what already happened, without changing the writers. Metrics that each sit behind one interface in one registry can be added and removed in a line, which keeps the code easy to read and refactor.

## Edges
