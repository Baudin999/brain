# Embedding models for brain's search

Measured on 2026-11-02. The raw evidence sits beside this file in `embedding-models/table.tsv` and
`embedding-models/dutch.tsv`.

## The question

Which embedding model gives brain's search the best result per size and speed? Multilingual support is a bonus, because
the user is Dutch and writes some queries in Dutch. The model it replaces is bge-small-en-v1.5, int8 ONNX, 34 MB, about
5 ms a query.

A good ranking matters because search is how a session finds what the graph knows. See the node
`project:how-reading-works`.

## Method

### Corpus

115 nodes: those of the project graph (`docs/brain/graph` of this repo), the global graph and the personal graph. Each
node is embedded as `NodeDocument.Passage` builds it: the slug with dashes as spaces, a colon, then the title, the why
and the body on separate lines. The scripts re-implemented the node parser and this passage format in Python.

### Test set

- `~/.local/share/brain/eval/search.tsv`: 35 labelled English queries, each with one or more wanted refs, and 8 nonsense
  queries that want nothing. This is the set `scripts/search-eval.sh` reads.
- 10 of the 35 labelled queries translated to Dutch, scored against the same refs. They are in
  `embedding-models/dutch.tsv`, each with its wanted refs. The English originals of those 10 were scored too, in the
  `EN10-r@5` column, so Dutch can be compared with English on the same queries. Only the torch runs have that column.

### Ranking

Cosine alone. Every vector is normalized, and a query ranks the 115 passages by dot product. There is no full-text
search, no fusion and no tier logic, so the numbers measure the model and not the rest of brain's search.

Each model got its own prompts, taken from its model card: a query prefix and a passage prefix, which is empty for most.
The `Represent this sentence for searching relevant passages: ` prefix for bge, `query: ` and `passage: ` for e5,
`query: ` for arctic, `search_query: ` and `search_document: ` for nomic, the `task: search result | query: ` form for
embeddinggemma and an `Instruct: ... Query: ` form for qwen3 and harrier. The query's token limit was the model's own,
capped at 8192.

### Runs

Two kinds of run, shown in the `device` column.

- `cpu-onnx-2t`: ONNX Runtime on the CPU with 2 threads, one text at a time, sequential execution, no spinning threads.
  This is how brain embeds, so its query times are the ones brain would see. The int8 files are the models' official
  quantized exports (or, for embeddinggemma, a quantized export from the onnx-community repo). Pooling was read from the
  model: the `sentence_embedding` output where the export has one, else the first token or the mean of the tokens.
- `cpu`: fp32 PyTorch through sentence-transformers on 16 threads, batches of 16 for the corpus and one text at a time
  for the query timing. Used for the models with no ONNX export tried, and as the reference for the ones with.

Every run was pinned as the machine's rules require: inside a build slot with `flock`, `nice -n 19` and
`taskset -c 16-31`, away from the CPUs 0 to 15 that the processor is losing. No run used the GPU for the numbers.

### Measures

- `r@5`: recall@5, the mean share of a query's wanted refs found in its top five.
- `MRR`: the mean of 1 over the rank of the first wanted ref.
- `minW`: the lowest cosine of any wanted ref over all labelled queries.
- `maxN`: the highest cosine that any nonsense query reaches against any passage.
- `gap`: separation, `minW` minus `maxN`. Positive means one cosine threshold splits every wanted ref from every
  nonsense hit. Negative means none does.
- `W>N`: the share of wanted refs whose cosine is above `maxN`.
- `top1>N`: the share of labelled queries whose best hit is above `maxN`.
- `NL-r@5`, `NL-MRR`: recall@5 and MRR on the 10 Dutch queries.
- `EN10-r@5`: recall@5 on the English originals of those 10.
- `over%`: the share of passages longer than the model's token limit, counting its prompt and special tokens. The
  longest passage is 1004 tokens with the XLM-R tokenizer and 926 with bge's. Only the torch runs have this column.
- `corpus_s`: seconds to embed the 115 passages. `query_ms`: the median of 20 single-query embeds.
- Size: parameters for the torch rows, the ONNX file in MB for the int8 and fp32 ONNX rows. The `params`, `dim`,
  `over%`, `EN10-r@5` and `gpu_MB` columns of `table.tsv` are `-` where the run kind does not measure them.

## Results

Rows are as in `table.tsv`, sorted as there. The first block is ONNX on 2 threads, the second is fp32 PyTorch on 16
threads. Times between the two blocks are not comparable.

### ONNX, 2 threads (brain's way)

| model | file MB | r@5 | MRR | minW | maxN | gap | W>N | top1>N | NL r@5 | NL MRR | corpus s | query ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| arctic-embed-l-v2.0 int8 | 570 | 0.919 | 0.986 | 0.010 | 0.228 | -0.218 | 0.90 | 0.97 | 0.917 | 0.950 | 55.6 | 46 |
| arctic-embed-m-v2.0 fp32 | 1226 | 0.919 | 0.981 | 0.047 | 0.179 | -0.132 | 0.94 | 1.00 | 0.817 | 0.883 | 42.3 | 46 |
| arctic-embed-m-v2.0 int8 | 311 | 0.919 | 0.963 | 0.080 | 0.201 | -0.120 | 0.90 | 1.00 | 0.817 | 0.795 | 21.7 | 18 |
| bge-small-en-v1.5 int8 (brain today) | 34 | 0.881 | 0.926 | 0.507 | 0.580 | -0.073 | 0.90 | 1.00 | 0.233 | 0.159 | 4.9 | 5 |
| multilingual-e5-small int8 | 118 | 0.852 | 0.890 | 0.772 | 0.796 | -0.024 | 0.94 | 1.00 | 0.783 | 0.753 | 5.6 | 5 |
| embeddinggemma-300m int8 | n/a | 0.914 | 0.914 | 0.090 | 0.216 | -0.126 | 0.96 | 1.00 | 0.767 | 0.672 | 54.9 | 194 |
| granite-97m-multi-r2 int8 | 98 | 0.843 | 0.886 | 0.680 | 0.766 | -0.085 | 0.78 | 1.00 | 0.617 | 0.561 | 9.5 | 7 |
| gte-multilingual-base fp32 | 1256 | 0.890 | 0.884 | 0.520 | 0.528 | -0.007 | 0.98 | 1.00 | 0.833 | 0.900 | 42.1 | 37 |
| gte-multilingual-base int8 | 340 | 0.900 | 0.911 | 0.515 | 0.524 | -0.009 | 0.98 | 1.00 | 0.767 | 0.738 | 21.7 | 15 |
| nomic-embed-text-v1.5 fp32 | 547 | 0.862 | 0.897 | 0.443 | 0.566 | -0.123 | 0.82 | 1.00 | 0.467 | 0.366 | 39.1 | 46 |
| nomic-embed-text-v1.5 int8 | 137 | 0.876 | 0.860 | 0.428 | 0.538 | -0.110 | 0.92 | 1.00 | 0.483 | 0.425 | 19.7 | 19 |

The embeddinggemma file size is not recorded: its weights sit in a separate data file and the run measured only the
small graph file.

### fp32 PyTorch, 16 threads

| model | params | dim | r@5 | MRR | minW | maxN | gap | W>N | top1>N | NL r@5 | NL MRR | EN10 r@5 | over% | corpus s | query ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| bge-small-en-v1.5 | 33.4M | 384 | 0.890 | 0.939 | 0.501 | 0.586 | -0.086 | 0.84 | 0.97 | 0.183 | 0.276 | 0.917 | 5.2% | 2.6 | 13 |
| granite-small-en-r2 | 47.7M | 384 | 0.929 | 0.933 | 0.725 | 0.786 | -0.061 | 0.88 | 1.00 | 0.267 | 0.260 | 0.950 | 0.0% | 12.9 | 9 |
| granite-97m-multi-r2 | 97.4M | 384 | 0.867 | 0.903 | 0.716 | 0.796 | -0.080 | 0.88 | 0.97 | 0.617 | 0.652 | 0.917 | 0.0% | 12.2 | 9 |
| bge-base-en-v1.5 | 109.5M | 768 | 0.905 | 0.916 | 0.366 | 0.482 | -0.117 | 0.86 | 1.00 | 0.267 | 0.236 | 0.917 | 5.2% | 7.4 | 22 |
| multilingual-e5-small | 117.7M | 384 | 0.862 | 0.860 | 0.766 | 0.796 | -0.030 | 0.94 | 1.00 | 0.783 | 0.683 | 0.900 | 5.2% | 3.4 | 8 |
| harrier-oss-v1-270m | 268.1M | 640 | 0.852 | 0.918 | 0.256 | 0.384 | -0.128 | 0.88 | 0.97 | 0.683 | 0.777 | 0.883 | 0.0% | 43.1 | 48 |
| multilingual-e5-base | 278.0M | 768 | 0.843 | 0.834 | 0.740 | 0.808 | -0.068 | 0.67 | 0.83 | 0.633 | 0.695 | 0.833 | 5.2% | 5.9 | 15 |
| embeddinggemma-300m | 307.6M | 768 | 0.914 | 0.914 | 0.077 | 0.203 | -0.127 | 0.96 | 1.00 | 0.767 | 0.661 | 0.950 | 0.0% | 12.8 | 33 |
| arctic-embed-l-v2.0 | 567.8M | 1024 | 0.919 | 0.971 | -0.001 | 0.218 | -0.220 | 0.92 | 0.97 | 0.917 | 0.950 | 0.950 | 0.0% | 29.2 | 45 |
| bge-m3 | 567.8M | 1024 | 0.852 | 0.932 | 0.347 | 0.488 | -0.141 | 0.82 | 0.91 | 0.867 | 0.950 | 0.917 | 0.0% | 31.8 | 41 |
| qwen3-embedding-0.6b | 595.8M | 1024 | 0.929 | 0.943 | 0.203 | 0.260 | -0.057 | 0.94 | 1.00 | 0.633 | 0.650 | 0.950 | 0.0% | 175.6 | 159 |
| harrier-oss-v1-0.6b | 596.0M | 1024 | 0.929 | 0.952 | 0.339 | 0.412 | -0.073 | 0.92 | 0.94 | 0.767 | 0.774 | 0.950 | 0.0% | 175.9 | 158 |

### License, ONNX and tokenizer

| model | license | ONNX | tokenizer | token limit |
|---|---|---|---|---|
| bge-small-en-v1.5, bge-base-en-v1.5 | MIT | brain's own int8 export for small | WordPiece | 512 |
| bge-m3 | MIT | none tried | XLM-R SentencePiece | 8192 |
| multilingual-e5-small, -base | MIT | official int8 for small | XLM-R SentencePiece | 512 |
| snowflake-arctic-embed-m-v2.0, -l-v2.0 | Apache-2.0 | official fp32 and int8, with a `sentence_embedding` output | XLM-R SentencePiece | 8192 |
| gte-multilingual-base | Apache-2.0 | onnx-community fp32 and int8 | XLM-R SentencePiece | 8192 |
| nomic-embed-text-v1.5 | Apache-2.0 | official fp32 and int8 | WordPiece | 8192 |
| granite-small-en-r2, granite-97m-multi-r2 | Apache-2.0 | official quantized export for the multilingual one | model's own | 8192, 32768 |
| embeddinggemma-300m | Gemma terms | onnx-community quantized | model's own | 2048 |
| qwen3-embedding-0.6b | Apache-2.0 | none tried | model's own | 32768 |
| harrier-oss-v1-270m, -0.6b | MIT | none tried | model's own | 32768 |

The license column comes from each model card's header. The tokenizer column comes from the models' cards, not from a
test. Check it before relying on a row other than arctic.

## Findings

1. In English no candidate beats bge-small by more than one or two of the 35 queries. Recall@5 spans 0.84 to 0.93 and
   one query is about 0.03. That is noise. The English test set cannot choose among these models.
2. In Dutch the models split. bge-small scores 0.23 recall@5, and the English-only models score 0.18 to 0.27. The
   multilingual models score 0.62 to 0.92.
3. snowflake-arctic-embed-m-v2.0 int8 scores 0.82 recall@5 in Dutch, with English recall@5 0.919 and MRR 0.963. It is
   311 MB, takes 18 ms a query and 22 s for the corpus, is Apache-2.0, ships an official int8 ONNX and reads 8192
   tokens, so no passage is cut.
4. snowflake-arctic-embed-l-v2.0 int8 scores MRR 0.986 and Dutch recall@5 0.92 at 570 MB and 46 ms a query. It is the
   best ranking measured, at about two and a half times the query time of m.
5. No model separates every wanted ref from every nonsense hit by cosine alone. Every `gap` is negative. The best are
   gte-multilingual-base at about -0.01 and multilingual-e5-small at -0.02, and both buy that with a cosine range
   squeezed into 0.5 to 0.8 that ranks worse. The arctic models rank best and have a gap of -0.12 to -0.22. The tiers in
   `src/Brain/Search/Evidence.cs` therefore keep needing the words and structure of a hit beside its cosine.
6. Nothing fits on the GPU beside vLLM, which holds 90% of it: 323 MiB were free when this was checked. It is not needed
   either, because the arctic models take 5 to 46 ms a query on two CPU threads.
7. Models rejected: embeddinggemma, qwen3 and harrier-0.6b are as accurate in English but take 33 to 194 ms a query or
   many seconds for the corpus. multilingual-e5-small is as fast as bge-small but ranks worse in English. nomic and the
   granite models are weak in Dutch. bge-m3 ranks well in Dutch but loses in English recall@5 and separation.

## What a switch changes in brain

- The tokenizer. arctic uses the XLM-R SentencePiece tokenizer where brain's embedder uses a WordPiece vocabulary file.
- The query prompt. arctic wants `query: ` before a query and nothing before a passage. bge's long instruction goes
  away.
- The output. The arctic ONNX exports have a `sentence_embedding` output, already pooled and ready to normalize, where
  bge-small gives token states that brain pools itself.
- The tier bars: `Close`, `Agree`, `Meant`, `Blend` and the meaning ranking's `Floor` were measured for bge-small and
  its cosines near 0.5 to 0.8. arctic's nonsense queries reach only 0.2 and its lowest wanted cosines lie near 0.01 to
  0.08. Each model's bars are measured on the test set for that model, and each says what it rests on.
- The index. Each row's hash includes the model's name, so the first lookup after a switch re-embeds the graph.

Each of these is a field of the model's profile, one file in `src/Brain/Search/Profiles`. Search, the tests outside a
profile, install and the drill name no model, so a model of a tokenizer and pooling brain reads needs a profile and
nothing else.

## Where it stands

Measured on 2026-11-03 through brain's own search, `scripts/search-eval.sh` with tiers, fusion and bars, on a copy of
the user's graph as it stood on 2026-11-03 at 06:15. English is the 35 labelled and 8 nonsense queries of `search.tsv`,
Dutch the 10 queries of `embedding-models/dutch.tsv`.

| model | English r@5 | English MRR | nonsense with any hit | Dutch r@5 | Dutch MRR |
|---|---|---|---|---|---|
| bge-small-en-v1.5 int8 | 0.919 | 0.936 | 0 of 8 | 0.000 | 0.000 |
| arctic-embed-m-v2.0 int8 | 0.900 | 0.890 | 0 of 8 | 0.550 | 0.583 |

On the graph as it stood at 08:45 the same day, a few nodes larger, bge-small scored 0.905 and 0.924 in English and 0
in Dutch, and arctic-embed-m-v2.0 0.900 and 0.900 in English and 0.517 and 0.583 in Dutch, both with no nonsense hit.

- arctic-embed-m-v2.0 ships as a profile anyone can select, with the best bars the grid found: close 0.53, agree 0.30,
  meant 0.25, floor 0.28 and blend 0.40. It is not the default: through brain's search its English falls below
  bge-small's, and a worse English search does not land as the default. Its cosine-only lead above does not survive
  the tiers.
- bge-small-en-v1.5 stays the default, with its bars as they were.
- arctic-embed-l-v2.0 is rejected on start-up: a fresh process reaches its first vector about 440 ms later than with m,
  and its gain in ranking is within the noise of the test set.
- Nothing model-specific is left outside a profile: the query prompt, the passage prompt, the bars, the model's files
  and their paths all moved into it.

Open:

- Bars calibrated from a measured noise level of each model, instead of a grid per model.
- The tokenizer's load time, which a fresh process pays before its first vector.
- Lookup through the running server, which keeps the model loaded.

## Running it again

The scripts are not kept. To repeat the experiment:

1. Build the corpus from the three graphs with the passage format above, and read `search.tsv` and `dutch.tsv`.
2. For each model, embed the passages and queries with its prompts and normalize.
3. Score by cosine: recall@5, MRR and the separation measures defined above.
4. For the ONNX rows, use ONNX Runtime with 2 intra-op threads, 1 inter-op thread, sequential execution and spinning
   off, and embed one text at a time. For the torch rows, use sentence-transformers with 16 threads.
5. Run every job inside a build slot, with `nice -n 19` and `taskset -c 16-31`.
