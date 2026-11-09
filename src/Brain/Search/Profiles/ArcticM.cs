namespace Brain.Search.Profiles;

/// <summary>
/// snowflake-arctic-embed-m-v2.0, int8, Snowflake's own ONNX export at a pinned revision: many languages, Dutch among
/// them, 768 dimensions, 8192 tokens, XLM-R's SentencePiece model, the model's pooled output as the vector. Its repo
/// carries the tokenizer only as tokenizer.json, so the SentencePiece model comes from arctic-embed-l-v2.0's repo, which
/// has the same tokenizer. Selectable, not the default: through brain's search it scores below bge-small-en-v1.5 in
/// English, recall@5 0.900 and MRR 0.890 against 0.919 and 0.936 on the search test set. Every bar was measured on that
/// set, <c>scripts/search-eval.sh</c> over the user's graph.
/// </summary>
internal static class ArcticM
{
    private const string HuggingFace = "https://huggingface.co/";

    public static readonly EmbedderProfile Profile = new(
        "snowflake-arctic-embed-m-v2.0",
        new(new(HuggingFace + "Snowflake/snowflake-arctic-embed-m-v2.0/resolve/95c2741480856aa9666782eb4afe11959938017f/onnx/model_int8.onnx"),
            "03d923bb1850ebdccb068e2f3abd8aa43fe81c50d07d037ef103fe3d0fb78e3b"),
        new(new(HuggingFace + "Snowflake/snowflake-arctic-embed-l-v2.0/resolve/ac6544c8a46e00af67e330e85a9028c66b8cfd9a/sentencepiece.bpe.model"),
            "cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"),
        Megabytes: 316,
        TokenizerKind.SentencePiece,
        // The query prompt arctic's model card gives; a passage takes none.
        QueryPrompt: "query: ",
        PassagePrompt: string.Empty,
        Pooling.SentenceEmbedding,
        Dimension: 768,
        MaxTokens: 8192,
        new Bars(
            // On the search test set the highest cosine of a node a query did not want was 0.525.
            Close: 0.53f,
            // On the search test set the wanted nodes with most of the query's words had cosines of 0.26 to 0.60 and
            // the unwanted ones 0.07 to 0.25.
            Agree: 0.30f,
            // Of 0.22, 0.25, 0.28 and 0.31 as the likely bar, with the floor at 0.28, 0.25 scored best on the search
            // test set: 0.22 scored the same with more hits a query, 0.28 cut Dutch recall@5 from 0.55 to 0.33, and
            // 0.31 lowered English recall@5 to 0.886.
            Meant: 0.25f,
            // Of floors from 0.20 to 0.34, with the likely bar at 0.25, 0.27 and 0.28 scored best in English on the
            // search test set, recall@5 0.900, and 0.28 kept the higher MRR, 0.890. A floor of 0.30 scored MRR 0.910
            // at recall@5 0.886.
            Floor: 0.28f,
            // On the search test set the highest blend a nonsense query reached was 0.34.
            Blend: 0.40));
}
