# Search

This file covers how brain finds nodes: the one ranking engine behind [`brain lookup`](cli.md#brain-lookup) and the
window's search box, the evidence and tiers that decide what a search shows, the embedding model that gives search its
sense of meaning, the nearest entries every node write is read against, and the script that measures search. What a
node is and how the graph is laid out is in [graph.md](graph.md). How the judge uses the nearest entries is in
[the-yes.md](the-yes.md).

## The ranking

`brain lookup` and the window's search box rank with one engine, `src/Brain/Search/Ranking.cs`, over every scope in
effect:

1. A node whose slug, ref or title is the query is pinned to the top.
2. Three rankings fuse by reciprocal rank fusion.
   - Structure: nodes whose title or slug starts with the query, then nodes that carry a query word as a subject.
   - Full text: SQLite FTS5 BM25 over slug, title, subjects, why and body, with slug and title weighted above
     subjects, why and body in that order.
   - Meaning: the cosine between the query's vector and each node's.
3. The edge neighbours of the top three hits rise a little, by edge weight.
4. Every hit keeps its evidence through fusion, and the evidence gives it a tier. The order stays best first.

## Evidence and tiers

The evidence of a hit is:

- whether the query is its slug, ref or title;
- whether structure found it;
- its cosine to the query, with the bars of the model that gave it;
- its word share: the share of the query's words its text holds, each word weighted by BM25's idf among the nodes
  searched, so a word no node holds weighs most and a word nearly every node holds weighs nearly nothing.

`src/Brain/Search/Evidence.cs` alone decides the tier:

- `sure`: the query names it, its cosine reaches the model's close bar, or its word share is at least 0.8 and its
  cosine reaches the model's agree bar or structure agrees.
- `likely`: its cosine reaches the model's meant bar, its word share is at least 0.4, structure found it, or its cosine
  plus half its word share reaches the model's blend bar.
- `weak`: anything else, such as a node that holds only a common word of the query. No search shows a weak hit.

Each model has its own scale of cosines, so each has its own bars, and a node below the model's floor is not in the
meaning ranking at all. The bars and the word shares are measured on a test set of labelled and nonsense queries, not
guessed; each says what it was measured against. Without the embedding model there is no cosine, and the tier comes
from the name, the structure and the words.

## What lookup prints

`brain lookup` prints up to 10 hits, each a ref, its tier and its title, an idea's tier followed by `[idea]`. A query
with no sure or likely hit prints `no node is sure or likely`.

```
$ brain lookup one door
project:one-door  sure  Only the brain CLI writes the graph, and every write passes its gates there
project:brain-workspace  likely [idea]  brain workspace is an IDE per project, tailored to how the user reads code, opened with `brain workspace <path>`
$ brain lookup purple elephant banana
no node is sure or likely
```

The window shows each hit with its tier and a snippet of its body or why, the matched words highlighted.

## The embedding model

The vectors come from an embedding model run inside brain by ONNX Runtime on at most two CPU threads, with no service
and no GPU.

Everything that belongs to one model is its profile, one file in `src/Brain/Search/Profiles`: its name, its files and
where they download from, its tokenizer (a WordPiece vocab or a SentencePiece model), the prompts before a query and
before a passage, how it pools, its dimension, its token limit and its bars, each with the measurement it rests on.
`EmbedderProfiles.All` lists the models brain ships. The one brain runs is the one `settings.json` names, or
`EmbedderProfiles.Default` when it names none. No code outside a profile names a model. A new model of a tokenizer and
pooling brain reads needs a profile and nothing else. The measurements behind the shipped profiles are in
[research/embedding-models.md](research/embedding-models.md).

Each vector row is kept under the name of the model that made it, and search reads only the rows of the model it runs.
The settings page switches the model; see [window.md](window.md#the-settings-page).

[`brain install`](cli.md#brain-install) downloads the model the settings name into its own directory under this repo's
`cache/model` (see [install.md](install.md)). Without it, lookup says so in one line and ranks by structure and full
text alone. ONNX Runtime ships for Linux on x64 and arm64 and for macOS on Apple silicon; on an Intel Mac the model
does not load and lookup ranks by keywords.

The full-text table and the vectors live in the data repo's `cache/search.sqlite`, rebuilt from the graph. A node is
indexed and embedded again only when its text changes.

## Nearest entries

[`brain nearest`](cli.md#brain-nearest), the judge's gate, every node write and the window ask one more query of the same
engine: the entries nearest to a node.

- The query is the whole node, its title, body and why, never a few words.
- A node with its slug or title is pinned to the top.
- Below it the three rankings fuse: nodes that share its subjects, BM25 over every word of its title, body and why, and
  the cosine between its passage and each node's, both embedded the same way.
- No edge neighbour rises, and the snippets carry no marks.
- The entries carry no tier: a node's cosine to another node runs higher than a query's, so lookup's tiers do not fit
  them.
- A node's own file never comes back, so an edit is not its own nearest entry.
- Without the embedding model the heading says so and the ranking is by keywords alone.

Which graphs it reads follows the scope the node lands in, not the session it came from. A project node is read against
that project, global and personal. A personal or global node is read against personal, global and every cloned project
in `projects.tsv`. A project node comes back under its `projects.tsv` name, such as `studio:cups`, a ref any directory
can open; only a project nobody registered shows as `project:`.

Each entry is a rung 0 line: its ref, its title and its scope.

```
nearest to global:chai:
  personal:tea  The user drinks tea  (personal)
  studio:cups  Cups for tea in the studio  (studio)
```

`brain nearest <ref>` asks it for a node, and `brain nearest --scope <scope>` for a node's markdown not yet written,
from stdin or `--file`, the markdown `add` reads, so an agent can ask before it writes. It reads only and travels no
edge.

## Measuring search

`scripts/search-eval.sh <queries.tsv>` runs `brain lookup` for each line of a test set: a query, a tab, and the refs it
should find. It prints recall@5, MRR, how many expected refs came back and how many hits a query returned; every hit
lookup returns is sure or likely. A line with no refs is a nonsense query, and the script counts the nonsense queries
that returned a sure hit and any hit. It only reads.

## Related

- [graph.md](graph.md): nodes, subjects and edges, what search ranks over.
- [the-yes.md](the-yes.md): how the judge reads a write against its nearest entries.
- [window.md](window.md): the reading pane and Home's search.
- [research/embedding-models.md](research/embedding-models.md): the measured comparison of embedding models.
