namespace Brain.Search;

/// <summary>
/// The embedder of a process whose settings name no model it can run: it never answers, so search ranks by structure and
/// full text alone and says why. It never reads or writes a vector, so the index keeps every model's rows as they are.
/// Its profile is the default one, whose prompts a probe is built with before the embedder is asked.
/// </summary>
internal sealed class NoEmbedder(string why) : IEmbedder
{
    public EmbedderProfile Profile => EmbedderProfiles.Default;

    public string Silence => "no embedding model runs, because " + why;

    public IReadOnlyList<float[]>? Embed(IReadOnlyList<string> texts) => null;
}
