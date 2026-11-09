namespace Brain.Search.Profiles;

/// <summary>
/// bge-small-en-v1.5, int8, from Xenova's ONNX export at a pinned revision: English only, 384 dimensions, 512 tokens, a
/// WordPiece vocab, the first token as the vector. The default. Every bar was measured on the search test set,
/// <c>scripts/search-eval.sh</c> over the user's graph.
/// </summary>
internal static class BgeSmall
{
    private const string Revision = "https://huggingface.co/Xenova/bge-small-en-v1.5/resolve/ea104dacec62c0de699686887e3f920caeb4f3e3/";

    public static readonly EmbedderProfile Profile = new(
        "bge-small-en-v1.5",
        new(new(Revision + "onnx/model_int8.onnx"), "bf64d05457cb391fa88d045faf5927a15ea36d96228ddf23ea970087afdc1197"),
        new(new(Revision + "vocab.txt"), "07eced375cec144d27c900241f3e339478dec958f92fddbc551f295c992038a3"),
        Megabytes: 34,
        TokenizerKind.WordPiece,
        // bge's instruction for a short query that looks for passages; a passage itself takes none.
        QueryPrompt: "Represent this sentence for searching relevant passages: ",
        PassagePrompt: string.Empty,
        Pooling.FirstToken,
        Dimension: 384,
        MaxTokens: 512,
        new Bars(
            // On the search test set every hit at or above it was a node the query wanted.
            Close: 0.75f,
            // On the search test set the hits with most of the words and a cosine above it were wanted, at 0.71 and
            // 0.73, and those at 0.60 to 0.67 were not.
            Agree: 0.70f,
            // Of 0.6, 0.65 and 0.7 as the meaning ranking's floor and the likely bar together, 0.65 scored best on the
            // search test set.
            Meant: 0.65f,
            // The same measurement as Meant: the two bars were measured as one.
            Floor: 0.65f,
            // On the search test set it keeps the wanted nodes whose cosine and words are each too low alone, the
            // lowest at 0.72, and no nonsense query reaches it. At 0.72 that node fell out as the graph grew, so the
            // bar sits below it.
            Blend: 0.70));
}
