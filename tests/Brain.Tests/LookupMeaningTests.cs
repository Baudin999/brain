namespace Brain.Tests;

public sealed class LookupMeaningTests : IDisposable
{
    private readonly TempBrain brain = new();

    public LookupMeaningTests()
    {
        brain.Embedder.Meanings["exchange"] = 0;
        brain.Embedder.Meanings["messages"] = 0;
        brain.Embedder.Meanings["cover"] = 1;
        brain.Ok("add", "--subjects", "test", "global", "actors-never-share-lines", "Actors send messages, never cache lines", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void LookupFindsANodeByMeaningWhenNoWordMatches()
    {
        var result = brain.Run("lookup", "how units exchange state");

        Assert.Equal("global:actors-never-share-lines  sure [idea]  Actors send messages, never cache lines\n", result.Out);
        Assert.Equal(string.Empty, result.Err);
        Assert.True(File.Exists(brain.IndexPath));
        Assert.False(File.Exists(brain.LogPath));
    }

    [Fact]
    public void ANodeFoundByWordsAndByMeaningIsListedOnceAndFirst()
    {
        brain.Ok("add", "--subjects", "test", "global", "ground-pass", "A phase", "--kind", "idea");

        var lines = brain.Ok("lookup", "ground", "cover").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("global:meadow  sure [idea]  Ground cover", lines[0]);
        Assert.Single(lines, line => line.StartsWith("global:meadow ", StringComparison.Ordinal));
    }

    [Fact]
    public void LookupFallsBackToKeywordsAndSaysSoInOneLineWithoutTheModel()
    {
        brain.Embedder.Down = true;

        var result = brain.Run("lookup", "cover");

        Assert.Equal(0, result.Code);
        Assert.Equal("global:meadow  likely [idea]  Ground cover\n", result.Out);
        Assert.Equal("lookup: keyword hits only: the embedding model fake-model is down\n", result.Err);
    }

    [Fact]
    public void TheIndexEmbedsOnlyWhatChangedAndRebuildsAfterADelete()
    {
        // Each add embedded the graph for its nearest entries already; this test counts what lookup embeds.
        File.Delete(brain.IndexPath);
        brain.Embedder.Calls.Clear();
        brain.Ok("lookup", "exchange");
        Assert.Equal(2, brain.Embedder.Documents.Count());

        brain.Embedder.Calls.Clear();
        brain.Ok("lookup", "exchange");
        Assert.Empty(brain.Embedder.Documents);

        brain.Ok("update", "meadow", "Ground cover and grass");
        brain.Embedder.Calls.Clear();
        brain.Ok("lookup", "exchange");
        Assert.Equal(["meadow: Ground cover and grass"], brain.Embedder.Documents);

        brain.Embedder.Calls.Clear();
        File.Delete(brain.IndexPath);
        brain.Ok("lookup", "exchange");
        Assert.Equal(2, brain.Embedder.Documents.Count());
    }

    [Fact]
    public void AnotherModelEmbedsEveryNodeAgain()
    {
        brain.Ok("lookup", "exchange");
        brain.Embedder.Calls.Clear();
        brain.Embedder.Profile = brain.Embedder.Profile with { Name = "another-model" };

        brain.Ok("lookup", "exchange");

        Assert.Equal(2, brain.Embedder.Documents.Count());
    }

    [Fact]
    public void AQueryAndAPassageEachGoBehindTheirModelsPrompt()
    {
        File.Delete(brain.IndexPath);
        brain.Embedder.Calls.Clear();
        brain.Embedder.Profile = brain.Embedder.Profile with { QueryPrompt = "search: ", PassagePrompt = "passage: " };

        brain.Ok("lookup", "exchange");

        var texts = brain.Embedder.Calls.SelectMany(call => call).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["passage: actors never share lines: Actors send messages, never cache lines", "passage: meadow: Ground cover", "search: exchange"], texts);
    }

    /// <summary>
    /// A query of two meanings and no word of either node lies at a cosine of 0.71 to each. Whether a node comes back
    /// is the model's floor and likely bar to say.
    /// </summary>
    [Fact]
    public void TheMeaningRankingsFloorAndLikelyBarAreTheModelsOwn()
    {
        brain.Embedder.Meanings["trade"] = 0;
        brain.Embedder.Meanings["lawn"] = 1;
        var bars = brain.Embedder.Profile.Bars with { Blend = 1 };

        brain.Embedder.Profile = brain.Embedder.Profile with { Bars = bars with { Floor = 0.7f, Meant = 0.7f } };
        Assert.Equal(2, brain.Ok("lookup", "trade", "lawn").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        brain.Embedder.Profile = brain.Embedder.Profile with { Bars = bars with { Floor = 0.72f, Meant = 0.7f } };
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "trade", "lawn"));

        brain.Embedder.Profile = brain.Embedder.Profile with { Bars = bars with { Floor = 0.7f, Meant = 0.72f } };
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "trade", "lawn"));
    }
}
