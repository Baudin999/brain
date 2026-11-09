using System.Text.RegularExpressions;
using Brain.Search;
using Brain.Serving;
using Brain.Staging;

namespace Brain.Tests;

/// <summary>
/// Every staged node write prints the entries nearest to each node it writes, under the pending write, so a duplicate is
/// seen before the user's yes; <c>brain nearest</c> asks the same query on its own. The tier follows the scope a node
/// lands in: a project node meets its project, global and personal; a personal or global node meets every project.
/// </summary>
public sealed class NearestTests : IDisposable
{
    private const string Pending = "pending ";
    private readonly TempBrain brain = new();

    public NearestTests()
    {
        brain.Embedder.Meanings["tea"] = 0;
        brain.Embedder.Meanings["chai"] = 0;
        brain.Embedder.Meanings["grass"] = 1;
        brain.Ok("add", "--subjects", "drinks", "personal", "tea", "The user drinks tea", "--kind", "idea", "--body", "Green tea in the morning.");
        brain.Ok("add", "--subjects", "ground", "global", "meadow", "Grass grows first on open ground", "--kind", "idea");
        brain.SaysYes = false;
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AnAddPrintsTheNearestEntriesUnderThePendingWrite()
    {
        var output = brain.Ok("add", "--subjects", "test", "global", "chai", "The user drinks chai", "--kind", "idea");

        var near = After(output);
        Assert.Equal("nearest to global:chai:", near[0]);
        Assert.Equal("  personal:tea  The user drinks tea  (personal)", near[1]);
        Assert.True(output.IndexOf(Pending, StringComparison.Ordinal) < output.IndexOf("nearest to", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEditLeavesTheNodeItselfOut()
    {
        var output = brain.RunWithInput("---\nsubjects: [drinks]\n---\n# The user drinks green tea\n\nEvery morning.\n", "edit", "personal:tea").Out;

        Assert.DoesNotContain("personal:tea  ", string.Join('\n', After(output)[1..]), StringComparison.Ordinal);
        Assert.Equal("nearest to personal:tea:", After(output)[0]);
    }

    [Fact]
    public void ASameTitleElsewhereIsPinnedFirst()
    {
        var output = brain.Ok("add", "--subjects", "test", "global", "tea-again", "The user drinks tea", "--kind", "idea");

        Assert.Equal("  personal:tea  The user drinks tea  (personal)", After(output)[1]);
    }

    [Fact]
    public void AForgetAndAChangeOfKindAlonePrintNoNearestEntries()
    {
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "global", "lawn", "A lawn of grass", "--why", "Lawns are grass.");
        brain.SaysYes = false;

        Assert.DoesNotContain("nearest to", brain.Ok("forget", "global:lawn"), StringComparison.Ordinal);
        Assert.DoesNotContain("nearest to", brain.Ok("update", "global:lawn", "--kind", "idea"), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTheModelTheHeadingSaysTheEntriesAreKeywordHits()
    {
        brain.Embedder.Down = true;

        var near = After(brain.Ok("add", "--subjects", "test", "global", "green-tea", "Green tea is a drink", "--kind", "idea"));

        Assert.Equal("nearest to global:green-tea, by keywords only: the embedding model fake-model is down:", near[0]);
        Assert.Contains("  personal:tea  The user drinks tea  (personal)", near);
    }

    [Fact]
    public void AFailingSearchRefusesTheWriteBeforeTheJudge()
    {
        Directory.Delete(Path.Combine(brain.Home, "cache"), recursive: true);
        Directory.CreateDirectory(brain.IndexPath);
        brain.Judge.Calls.Clear();

        var (code, output, errors) = brain.Run("add", "--subjects", "test", "global", "chai", "The user drinks chai", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.DoesNotContain(Pending, output, StringComparison.Ordinal);
        Assert.StartsWith("brain: the search index", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Judge.Calls);
    }

    [Fact]
    public void AMergeShowsTheNearestEntriesOfEachNewNode()
    {
        var output = brain.RunWithInput("+ global:chai | The user drinks chai | idea | user\n+ global:lawn | A lawn of grass | idea | user\n+ global:chai [test]\n+ global:lawn [test]\n", "merge").Out;

        Assert.Contains("nearest to global:chai:\n  personal:tea  The user drinks tea  (personal)", output, StringComparison.Ordinal);
        Assert.Contains("nearest to global:lawn:\n  global:meadow  Grass grows first on open ground  (global)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NearestAsksTheSameQueryForANodeOrForMarkdown()
    {
        var forNode = brain.Ok("nearest", "personal:tea");
        var forDocument = brain.RunWithInput("# Chai with milk\n\nThe user drinks chai.\n", "nearest", "--scope", "global").Out;

        Assert.Equal("nearest to personal:tea:\n  no node is near\n", forNode);
        Assert.StartsWith("nearest to this markdown:\n  personal:tea  The user drinks tea  (personal)\n", forDocument, StringComparison.Ordinal);
        Assert.False(File.Exists(brain.LogPath));
    }

    [Fact]
    public void AProjectNodeMeetsItsProjectGlobalAndPersonalButNoOtherProject()
    {
        var studio = brain.MakeRepo("studio-repo");
        brain.Init("studio");
        brain.MakeRepo("other-repo");
        brain.Init("other");
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "project", "kettle", "The office kettle boils tea", "--kind", "idea");
        brain.Cwd = studio;
        brain.Ok("add", "--subjects", "test", "project", "cups", "Cups for tea in the studio", "--kind", "idea");
        brain.SaysYes = false;

        var near = After(brain.Ok("add", "--subjects", "test", "project", "mugs", "Mugs for tea", "--kind", "idea"));

        Assert.Contains("  studio:cups  Cups for tea in the studio  (studio)", near);
        Assert.Contains("  personal:tea  The user drinks tea  (personal)", near);
        Assert.DoesNotContain(near, line => line.Contains("kettle", StringComparison.Ordinal));
    }

    [Fact]
    public void APersonalNodeMeetsEveryRegisteredProject()
    {
        brain.MakeRepo("studio-repo");
        brain.Init("studio");
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "project", "cups", "Cups for tea in the studio", "--kind", "idea");
        brain.Cwd = brain.Root;
        brain.SaysYes = false;

        var near = After(brain.Ok("add", "--subjects", "test", "personal", "chai", "The user drinks chai", "--kind", "idea"));

        Assert.Contains("  studio:cups  Cups for tea in the studio  (studio)", near);
        Assert.Contains("  personal:tea  The user drinks tea  (personal)", near);
    }

    [Fact]
    public void TheWindowShowsTheEntriesByNodeAndAProjectEntryInItsProjectsColor()
    {
        brain.MakeRepo("studio-repo");
        brain.Init("studio");
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "project", "cups", "Cups for tea in the studio", "--kind", "idea");
        brain.Cwd = brain.Root;
        brain.SaysYes = false;
        var id = Regex.Match(brain.Ok("add", "--subjects", "test", "personal", "chai", "The user drinks chai", "--kind", "idea"), "pending ([0-9a-f]{12})").Groups[1].Value;
        new PendingStore(brain.Home).Ask(id);

        var view = Assert.Single(ReviewViews.Nearest(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), id));

        Assert.Equal("personal:chai", view.Ref);
        Assert.Contains(view.Hits, hit => hit is { Ref: "studio:cups", Scope: "studio" } && hit.Hue == ReviewViews.Hue("studio"));
        Assert.Contains(view.Hits, hit => hit is { Ref: "personal:tea", Scope: "personal", Hue: null });
    }

    [Fact]
    public void LookupAndNearestKeepOneIndexRowPerProjectNode()
    {
        brain.MakeRepo("studio-repo");
        brain.Init("studio");
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "project", "cups", "Cups for tea in the studio", "--kind", "idea");
        brain.Ok("lookup", "cups");
        brain.SaysYes = false;
        brain.Ok("add", "--subjects", "test", "project", "mugs", "Mugs for tea", "--kind", "idea");

        using var index = SearchIndex.Open(brain.IndexPath);
        Assert.Single(index.Vectors(brain.Embedder.Profile.Name), row => row.Key.EndsWith(":cups", StringComparison.Ordinal));
        Assert.Single(index.Texts(), row => row.Key.EndsWith(":cups", StringComparison.Ordinal));
    }

    /// <summary>The lines from the first nearest heading on, the block a write prints under its pending line.</summary>
    private static string[] After(string output) =>
        output[output.IndexOf("nearest to", StringComparison.Ordinal)..].Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
