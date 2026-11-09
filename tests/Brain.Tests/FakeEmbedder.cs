using Brain.Search;

namespace Brain.Tests;

/// <summary>
/// Stands in for the ONNX model. Each word in <see cref="Meanings"/> names an axis, and a text's vector counts the words it
/// holds on their axes, so two texts mean the same when they share an axis. A text with none of the words points
/// nowhere and matches nothing. It records every call, and answers null while <see cref="Down"/> is set.
/// </summary>
internal sealed class FakeEmbedder : IEmbedder
{
    public Dictionary<string, int> Meanings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public bool Down { get; set; }

    public EmbedderProfile Profile { get; set; } = FakeProfile.Profile;

    public string Silence => $"the embedding model {Profile.Name} is down";

    /// <summary>The document texts embedded so far, query probes left out.</summary>
    public IEnumerable<string> Documents => Calls.SelectMany(call => call).Where(text => !text.StartsWith(Profile.QueryPrompt, StringComparison.Ordinal));

    public IReadOnlyList<float[]>? Embed(IReadOnlyList<string> texts)
    {
        Calls.Add(texts);
        return Down ? null : [.. texts.Select(Vector)];
    }

    private float[] Vector(string text)
    {
        var vector = new float[Meanings.Count == 0 ? 1 : Meanings.Values.Max() + 1];
        foreach (var (word, axis) in Meanings)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                vector[axis]++;
            }
        }

        return vector;
    }
}
