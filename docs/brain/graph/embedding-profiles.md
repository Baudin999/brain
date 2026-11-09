---
kind: information
subjects: []
named-by: user
born: 2026-11-03
session: c8734326-af46-46b2-99ee-17294ff114f9
---
# Swapping the embedding model is a new embedding profile; no code outside it knows which embedding model search runs on

An embedding profile holds what is true of one embedding model only: its name, its files and where they download from, its tokenizer kind, the prompts before a query and a passage, its pooling, dimension and token limit, and its bars, the cosines a search hit is read against, each with the measurement it rests on. No code, test or doc outside an embedding profile carries a number read off an embedding model, its prompt or its file. Each embedding model has its own bars, and a search score is compared against the bars of the embedding model that produced it. The embedder is injected and carries its profile; search reads prompts and bars only from it. Word shares and ranks are not specific to an embedding model and stay in search. A new embedding model of a tokenizer family brain already reads needs a new embedding profile and nothing else.

## Why
Swapping the embedding model should have been a moment in time, and the future will bring better embedding models. Instead bge had reached through search, tests, install and docs: each tuning step against the search eval wrote bge's numbers in as constants, so swapping to arctic-m took a builder and a grid search and still did not fit.

## Edges
