namespace Brain.Search;

/// <summary>How a model's tokenizer file turns text into the model's ids.</summary>
internal enum TokenizerKind
{
    /// <summary>A BERT WordPiece vocab, lowercased, between [CLS] and [SEP].</summary>
    WordPiece,

    /// <summary>A SentencePiece model with XLM-R's ids: &lt;s&gt;, &lt;pad&gt;, &lt;/s&gt; and &lt;unk&gt; first, every piece one higher.</summary>
    SentencePiece,
}

/// <summary>Which output of the model is a text's vector, before it is normalized.</summary>
internal enum Pooling
{
    /// <summary>The first token of the first output, [CLS].</summary>
    FirstToken,

    /// <summary>The model's own pooled output, sentence_embedding.</summary>
    SentenceEmbedding,
}

/// <summary>A file of a model: the url it downloads from and its SHA-256. It lands in the model's directory under the url's last segment.</summary>
internal sealed record ModelFile(Uri Url, string Sha256)
{
    public string Name => Path.GetFileName(Url.AbsolutePath);
}

/// <summary>
/// The cosines search compares a model's scores against. Each model has its own scale, so each model has its own bars.
/// Close: meaning alone makes a hit sure. Agree: meaning backs words that hold most of the query, for a sure hit. Meant:
/// meaning alone makes a hit likely. Floor: below it a node is not in the meaning ranking. Blend: the cosine plus half
/// the word share at which meaning and words together make a hit likely.
/// </summary>
internal sealed record Bars(float Close, float Agree, float Meant, float Floor, double Blend);

/// <summary>
/// One embedding model brain can rank by meaning with, as data. Its name is the directory its files land in and part of
/// every vector row's hash, so a switch embeds every node again. Its files say where install downloads them from and how
/// large they are. Then how it tokenizes, the prompts before a query and before a passage, how it pools, its dimension,
/// its token limit and its bars. A model of a tokenizer kind and pooling brain reads needs a profile and no new code.
/// </summary>
internal sealed record EmbedderProfile(
    string Name,
    ModelFile Weights,
    ModelFile Tokenizer,
    int Megabytes,
    TokenizerKind Tokenizing,
    string QueryPrompt,
    string PassagePrompt,
    Pooling Pooling,
    int Dimension,
    int MaxTokens,
    Bars Bars)
{
    /// <summary>A query as the model embeds it, behind its query prompt.</summary>
    public string Query(string text) => QueryPrompt + text;

    /// <summary>A passage as the model embeds it, behind its passage prompt.</summary>
    public string Passage(string text) => PassagePrompt + text;

    /// <summary>The files install downloads and the embedder loads.</summary>
    public IReadOnlyList<ModelFile> Files => [Weights, Tokenizer];

    /// <summary>Where this model's files live under the root of every model's files, so models never overwrite each other's.</summary>
    public string Directory(string modelRoot) => Path.Combine(modelRoot, Name);
}
