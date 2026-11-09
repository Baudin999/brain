namespace Brain.Tests;

public sealed class CheckTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void CheckPassesAValidGraph()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "personal", "user", "The user", "--kind", "idea");
        brain.Ok("link", "meadow", "for", "personal:user");
        brain.Ok("link", "meadow", "see", "url:https://example.org");

        Assert.Equal("check: 2 nodes, 2 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void CheckReportsEveryKindOfProblem()
    {
        File.WriteAllText(
            Path.Combine(brain.Home, "graph", "global", "broken.md"),
            "---\nnamed-by: robot\nborn: soon\nsubjects: [Not OK]\n---\n\n## Edges\n- part-of -> ghost\n- Bad Rel -> url:https://x.org\n"
            + "- see -> url:x\n- see -> thing:1\n- odd line\n");

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(1, code);
        string[] expected =
        [
            "title is empty",
            "kind is missing",
            "named-by is not one of user, claude, agent",
            "born is missing or not yyyy-MM-dd",
            "subjects [Not OK] is not a list of lowercase-hyphenated words like [ui, judge]",
            "cannot place line: - odd line",
            "edge target ghost does not exist",
            "relation Bad Rel is not lowercase-hyphenated",
            "malformed url ref x",
            "edge target thing:1: thing is neither a ref kind nor a registered project",
        ];
        Assert.All(expected, problem => Assert.Contains($"global:broken: {problem}", errors, StringComparison.Ordinal));
        Assert.Equal(expected.Length, errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void CheckFailsAnOldFormatFileAndListsInformationWithoutAWhyWithoutFailing()
    {
        File.WriteAllText(Path.Combine(brain.Home, "graph", "global", "old.md"), "# old\n\nAn old label\n\n- Kind: decision\n- Named-by: user\n- Born: 2026-10-01\n\n## Edges\n");
        File.WriteAllText(
            Path.Combine(brain.Home, "graph", "global", "bare.md"),
            "---\nkind: information\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n---\n# A bare fact\n\n## Edges\n");

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(1, code);
        Assert.Contains("global:old: cannot place line: " + NodeParser.OldFormat, errors, StringComparison.Ordinal);
        Assert.Contains("migration: 1 information nodes have no why: global:bare\n", errors, StringComparison.Ordinal);
        File.Delete(Path.Combine(brain.Home, "graph", "global", "old.md"));
        Assert.Equal(0, brain.Run("check").Code);
    }
}
