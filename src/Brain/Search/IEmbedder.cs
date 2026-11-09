namespace Brain.Search;

/// <summary>Turns texts into vectors, one per text and in order. Null when the model cannot be loaded or run.</summary>
internal interface IEmbedder
{
    /// <summary>
    /// The model's profile: its name is what its vector rows are kept under, and its prompts and bars are the ones search
    /// uses for the vectors it gives.
    /// </summary>
    EmbedderProfile Profile { get; }

    /// <summary>Why the embedder answers nothing when it does not, as a clause after "keyword hits only:".</summary>
    string Silence { get; }

    IReadOnlyList<float[]>? Embed(IReadOnlyList<string> texts);
}
