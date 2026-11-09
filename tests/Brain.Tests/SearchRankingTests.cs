using Brain.Search;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>The ranking engine behind lookup and the window: structure, full text, meaning, fusion and the edge lift.</summary>
public sealed class SearchRankingTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string[] Lookup(params string[] words) =>
        brain.Ok(["lookup", .. words]).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ')[0]).ToArray();

    [Fact]
    public void AnExactSlugOrTitleIsPinnedAboveNodesThatHoldTheWordsMore()
    {
        brain.Ok("add", "--subjects", "test", "global", "cover", "Plain", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "deep-cover", "Cover under cover", "--why", "Cover again.", "--body", "Cover, cover.");
        brain.Ok("add", "--subjects", "test", "global", "roads", "Where the roads lead home", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "home-roads", "Roads lead home, roads lead home", "--kind", "idea");

        Assert.Equal("global:cover", Lookup("cover")[0]);
        Assert.Equal("global:roads", Lookup("where", "the", "roads", "lead", "home")[0]);
        Assert.Equal("global:deep-cover", Lookup("global:deep-cover")[0]);
    }

    [Fact]
    public void ATitleHitOutranksAWhyHitAndAWhyHitOutranksABodyHit()
    {
        brain.Ok("add", "--subjects", "test", "global", "in-body", "Third", "--body", "A lantern hangs here.", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "in-why", "Second", "--why", "A lantern hangs here.");
        brain.Ok("add", "--subjects", "test", "global", "in-title", "A lantern hangs here", "--kind", "idea");

        Assert.Equal(["global:in-title", "global:in-why", "global:in-body"], Lookup("lantern"));
    }

    [Fact]
    public void ASubjectFindsANodeWhoseTextLacksTheWord()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("update", "meadow", "--subjects", "roads");

        Assert.Equal(["global:meadow"], Lookup("roads"));
    }

    [Fact]
    public void AnEdgeNeighbourOfTheTopHitRisesAboveItsEqual()
    {
        brain.Ok("add", "--subjects", "test", "global", "lantern", "Lantern light", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "alpha", "A note on lanterns", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "beta", "A note on lanterns", "--kind", "idea");
        Assert.Equal(["global:lantern", "global:alpha", "global:beta"], Lookup("lantern"));

        brain.Ok("link", "lantern", "see", "beta");

        Assert.Equal(["global:lantern", "global:beta", "global:alpha"], Lookup("lantern"));
    }

    [Fact]
    public void TheFullTextFollowsAnUpdateAndAForget()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        Assert.Equal(["global:meadow"], Lookup("ground"));

        brain.Ok("update", "meadow", "Grass first");
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "ground"));
        Assert.Equal(["global:meadow"], Lookup("grass"));

        brain.Ok("forget", "meadow");
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "grass"));
    }

    [Fact]
    public void EachHitCarriesItsTierAndAHitHoldingOnlyACommonWordIsLeftOut()
    {
        brain.Ok("add", "--subjects", "test", "global", "teapot", "Teapot", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "kettle", "The teapot on the stove", "--kind", "idea");
        for (var n = 0; n < 4; n++)
        {
            brain.Ok("add", "--subjects", "test", "global", $"plain-{n}", $"The plain note {n}", "--kind", "idea");
        }

        var lines = brain.Ok("lookup", "the", "teapot").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var window = ReviewViews.Search(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), "the teapot");

        Assert.Equal(["global:kettle  sure [idea]  The teapot on the stove", "global:teapot  likely [idea]  Teapot"], lines);
        Assert.Equal([("global:kettle", "sure"), ("global:teapot", "likely")], window.Hits.Select(hit => (hit.Ref, hit.Tier)));
    }

    [Fact]
    public void AQueryWithNothingSureOrLikelySaysSoInOneLine()
    {
        brain.Ok("add", "--subjects", "test", "global", "plain", "The plain note", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "other", "The other note", "--kind", "idea");

        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "the", "elephant"));
    }

    [Fact]
    public void AQueryWithoutWordsMatchesNothing()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");

        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "???"));
    }

    [Fact]
    public void WithoutAModelTheEmbedderAnswersNull()
    {
        using var embedder = new OnnxEmbedder(Path.Combine(brain.Root, "no-model"), FakeProfile.Profile);

        Assert.Null(embedder.Embed(["Ground cover"]));
    }
}
