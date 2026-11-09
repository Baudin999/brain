using Brain.Search;

namespace Brain.Tests;

/// <summary>
/// A settings.json that cannot name an embedding model, unreadable or naming a model this brain does not ship, stops no
/// reading: the process runs without a model, lookup ranks by keywords and says why, and context still prints.
/// </summary>
public sealed class UnreadableSettingsTests : IDisposable
{
    private readonly TempBrain brain = new();

    public UnreadableSettingsTests() => brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");

    public void Dispose() => brain.Dispose();

    [Theory]
    [InlineData("{ not json", "is not JSON")]
    [InlineData("""{"embedder":"a-model-from-a-newer-brain"}""", "embedder is \"a-model-from-a-newer-brain\", not one of")]
    public void LookupRanksByKeywordsAndSaysWhyAndContextStillPrints(string settings, string why)
    {
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), settings);
        brain.Running = EmbedderProfiles.Running(brain.Home, Path.Combine(brain.Root, "model"));

        var lookup = brain.Run("lookup", "meadow");
        var context = brain.Run("context");

        Assert.IsType<NoEmbedder>(brain.Running);
        Assert.Equal((0, "global:meadow  sure [idea]  Ground cover\n"), (lookup.Code, lookup.Out));
        Assert.StartsWith("lookup: keyword hits only: no embedding model runs, because ", lookup.Err, StringComparison.Ordinal);
        Assert.Contains(why, lookup.Err, StringComparison.Ordinal);
        Assert.Equal(0, context.Code);
        Assert.Contains("global", context.Out, StringComparison.Ordinal);
    }
}
