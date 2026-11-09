namespace Brain.Tests;

public sealed class GraphCommandTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ContextListsBrainScopesWithCounts()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        var lines = brain.Ok("context").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.InRange(lines.Length, 1, 8);
        Assert.Contains(lines, line => line.StartsWith("global    1 nodes", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("project   none here", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextNamesTheModelAndEffortTheSettingsGiveEachClosedAgent()
    {
        var none = brain.Ok("context");
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), """{"builder-easy":{"model":"haiku","effort":"low"},"validator":{"model":"fable","effort":"max"}}""");

        var some = brain.Ok("context");

        Assert.Contains("\nagents    builder-easy sonnet medium, builder-difficult opus medium, validator opus medium, readiness sonnet medium\n", none, StringComparison.Ordinal);
        Assert.Contains("\nagents    builder-easy haiku low, builder-difficult opus medium, validator fable max, readiness sonnet medium\n", some, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextSaysWhyTheSettingsCannotBeReadAndStillShowsTheScopes()
    {
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), "{");

        var output = brain.Ok("context");

        Assert.Contains("\nagents    ", output, StringComparison.Ordinal);
        Assert.Contains("is not JSON", output, StringComparison.Ordinal);
        Assert.Contains("global    0 nodes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextSaysWhyTheSettingsCannotBeOpenedAndStillShowsTheScopes()
    {
        var path = Path.Combine(brain.Home, BrainSettings.FileName);
        File.WriteAllText(path, "{}");
        OmarchyThemeTests.Chmod(path, UnixFileMode.None);

        var output = brain.Ok("context");

        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Contains($"\nagents    {path} cannot be opened: ", output, StringComparison.Ordinal);
        Assert.Contains("global    0 nodes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextShowsProjectScopeInsideARepoWithABrain()
    {
        brain.MakeProject();

        var output = brain.Ok("context");

        Assert.Contains("project   0 nodes in", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUpdateDoesNotUndoALinkAnotherSessionMadeWhileTheJudgeRead()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "acre", "Acre", "--kind", "idea");
        brain.Judge.During = () =>
        {
            brain.Judge.During = null;
            brain.Ok("link", "global:meadow", "see", "acre");
        };

        var (code, _, errors) = brain.Run("update", "global:meadow", "Fine ground cover");

        Assert.Equal(1, code);
        Assert.Contains("global:meadow changed on disk", errors, StringComparison.Ordinal);
        Assert.Contains("- see -> acre", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
    }

    [Fact]
    public void AddWritesTheNodeFile()
    {
        brain.Ok("add", "--subjects", "test", "personal", "likes-tea", "The user drinks tea", "--named-by", "user", "--session", "abc-1", "--kind", "idea");

        var text = brain.NodeText("personal", "likes-tea");

        Assert.Equal(
            "---\nkind: idea\nsubjects: [test]\nnamed-by: user\nborn: 2026-10-03\nsession: abc-1\n---\n# The user drinks tea\n\n## Edges\n",
            text);
    }

    [Fact]
    public void NodeShowsTheBodyAndTheWhyUnderTheFields()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--why", "Bare ground read as unfinished.\nIt still does.", "--body", "Grass first.");

        var lines = brain.Ok("node", "global:meadow").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["global:meadow  Ground cover", "kind: information | subjects: [test] | named-by: claude | born: 2026-10-03", "Grass first.", "why: Bare ground read as unfinished.  It still does."], lines);
    }

    [Fact]
    public void AddRefusesAnExistingNodeALongTitleAndABadSlug()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        var longTitle = string.Join(' ', Enumerable.Repeat("word", 26));

        Assert.Equal(1, brain.Run("add", "--subjects", "test", "global", "meadow", "Again", "--kind", "idea").Code);
        Assert.Equal(1, brain.Run("add", "--subjects", "test", "global", "long", longTitle, "--kind", "idea").Code);
        Assert.Equal(1, brain.Run("add", "--subjects", "test", "global", "sectioned", "Title", "--body", "## Why", "--kind", "idea").Code);
        Assert.Equal(1, brain.Run("add", "--subjects", "test", "global", "Bad_Slug", "Label", "--kind", "idea").Code);
        Assert.Equal(1, brain.Run("add", "--subjects", "test", "project", "x", "No project here", "--kind", "idea").Code);
    }

    [Fact]
    public void LinkAddsAnEdgeOnceAndLogsNoTravel()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "personal", "user", "The user", "--kind", "idea");

        brain.Ok("link", "meadow", "named-by", "personal:user");
        brain.Ok("link", "meadow", "named-by", "personal:user");

        var text = brain.NodeText("global", "meadow");
        Assert.Single(text.Split('\n'), line => line == "- named-by -> personal:user");
        Assert.False(File.Exists(brain.LogPath));
    }

    [Fact]
    public void LinkRefusesAMissingTargetAndAMalformedRef()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");

        Assert.Equal(1, brain.Run("link", "meadow", "part-of", "nowhere").Code);
        Assert.Equal(1, brain.Run("link", "meadow", "built", "commit:XYZ").Code);
        Assert.Equal(1, brain.Run("link", "meadow", "built", "thing:1").Code);
    }

    [Fact]
    public void NodeListsEdgesStrongestFirst()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "wg-12", "The ground phase", "--kind", "idea");
        brain.Ok("link", "meadow", "defined-in", "file:docs/ground.md");
        brain.Ok("link", "meadow", "part-of", "wg-12");
        brain.Session = "s1";
        brain.Ok("node", "meadow");
        brain.Ok("node", "wg-12");

        var lines = brain.Ok("node", "global:meadow").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("global:meadow  Ground cover", lines[0]);
        Assert.Equal("kind: idea | subjects: [test] | named-by: claude | born: 2026-10-03", lines[1]);
        Assert.Equal("  2.00  part-of -> wg-12", lines[2]);
        Assert.Equal("  1.00  defined-in -> file:docs/ground.md", lines[3]);
    }

    [Fact]
    public void BareRefsSearchProjectThenGlobalThenPersonal()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "personal", "same", "Personal one", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "same", "Global one", "--kind", "idea");

        Assert.StartsWith("global:same", brain.Ok("node", "same"), StringComparison.Ordinal);

        brain.Ok("add", "--subjects", "test", "project", "same", "Project one", "--kind", "idea");
        Assert.StartsWith("project:same", brain.Ok("node", "same"), StringComparison.Ordinal);
    }

    [Fact]
    public void LookupRanksSlugHitsAboveLabelHitsAndDoesNotLog()
    {
        brain.Ok("add", "--subjects", "test", "global", "ground-pass", "A phase", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Cover on open ground", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "unrelated", "Nothing here", "--kind", "idea");

        var lines = brain.Ok("lookup", "ground").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["global:ground-pass  sure [idea]  A phase", "global:meadow  likely [idea]  Cover on open ground"], lines);
        Assert.False(File.Exists(brain.LogPath));
    }
}
