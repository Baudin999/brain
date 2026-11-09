namespace Brain.Tests;

public sealed class NamedProjectTests : IDisposable
{
    private readonly TempBrain brain = new();

    /// <summary>A registered project afterlife with one node, wg-19, and the cwd moved out of every repo.</summary>
    public NamedProjectTests()
    {
        brain.MakeRepo("afterlife");
        brain.Init();
        brain.Ok("add", "--subjects", "test", "project", "wg-19", "The worldgen phase", "--kind", "idea");
        brain.Cwd = brain.Root;
        brain.Ok("add", "--subjects", "test", "personal", "generate-then-edit", "Generate first, then edit", "--kind", "idea");
    }

    public void Dispose() => brain.Dispose();

    private void Edge(string scope, string slug, string line) =>
        File.AppendAllText(Path.Combine(brain.Home, "graph", scope, slug + ".md"), line + "\n");

    [Fact]
    public void ANamedProjectTargetLinksChecksAndWalksFromOutsideThatProject()
    {
        Assert.Equal(
            "linked personal:generate-then-edit for afterlife:wg-19\n",
            brain.Ok("link", "generate-then-edit", "for", "afterlife:wg-19"));

        Assert.EndsWith("- for -> afterlife:wg-19\n", brain.NodeText("personal", "generate-then-edit"), StringComparison.Ordinal);
        Assert.Equal("check: 1 nodes, 1 edges, 0 errors in global, personal\n", brain.Ok("check"));
        brain.Session = "s1";
        Assert.Contains("  1.00  for -> afterlife:wg-19\n", brain.Ok("node", "generate-then-edit"), StringComparison.Ordinal);
        brain.Ok("node", "afterlife:wg-19");
        Assert.Contains("  2.00  for -> afterlife:wg-19\n", brain.Ok("node", "generate-then-edit"), StringComparison.Ordinal);
        Assert.Contains("  2.00  personal:generate-then-edit for -> afterlife:wg-19  The worldgen phase\n", brain.Ok("walk", "generate-then-edit"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnregisteredProjectOrAMissingSlugIsAnError()
    {
        Edge("personal", "generate-then-edit", "- for -> afterlife:wg-20\n- for -> elsewhere:wg-19");

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(1, code);
        Assert.Equal(
            "personal:generate-then-edit: edge target afterlife:wg-20 does not exist\n"
            + "personal:generate-then-edit: edge target elsewhere:wg-19: elsewhere is neither a ref kind nor a registered project\n",
            errors);
        Assert.Contains("no node afterlife:wg-20", brain.Run("link", "generate-then-edit", "for", "afterlife:wg-20").Err, StringComparison.Ordinal);
        Assert.Contains("elsewhere is neither", brain.Run("link", "generate-then-edit", "for", "elsewhere:wg-19").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectNotClonedHereIsAWarning()
    {
        File.AppendAllText(Path.Combine(brain.Home, "projects.tsv"), "gone\t~/gone\t-\t2026-10-03\n");
        Edge("personal", "generate-then-edit", "- for -> gone:wg-1");

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(0, code);
        Assert.Equal($"warning: personal:generate-then-edit: gone:wg-1: project gone is not cloned at {Path.Combine(brain.Root, "gone")}\n", errors);
    }

    [Fact]
    public void PersonalAndGlobalNodesMayNotTargetTheCurrentProject()
    {
        Edge("personal", "generate-then-edit", "- for -> project:wg-19");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Cwd = Path.Combine(brain.Root, "afterlife");

        var (code, _, errors) = brain.Run("check");
        var refused = brain.Run("link", "global:meadow", "for", "project:wg-19");

        Assert.Equal(1, code);
        Assert.Equal("personal:generate-then-edit: a personal node may not target project:wg-19; name the project from projects.tsv: <name>:wg-19\n", errors);
        Assert.Equal(1, refused.Code);
        Assert.Contains("a global node may not target project:wg-19; name the project from projects.tsv: <name>:wg-19", refused.Err, StringComparison.Ordinal);
        brain.Ok("unlink", "generate-then-edit", "for", "project:wg-19");
        brain.Ok("link", "generate-then-edit", "for", "afterlife:wg-19");
        brain.Ok("check");
    }

    [Fact]
    public void ProjectNodesKeepProjectAndBareTargets()
    {
        brain.Cwd = Path.Combine(brain.Root, "afterlife");
        brain.Ok("add", "--subjects", "test", "project", "world-plan", "The world plan", "--kind", "idea");
        brain.Ok("link", "project:world-plan", "after", "wg-19");
        brain.Ok("link", "project:world-plan", "see", "project:wg-19");

        Assert.EndsWith("- after -> wg-19\n- see -> wg-19\n", File.ReadAllText(Path.Combine(brain.Root, "afterlife", "docs", "brain", "graph", "world-plan.md")), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("global")]
    [InlineData("personal")]
    [InlineData("commit")]
    [InlineData("session")]
    [InlineData("file")]
    [InlineData("url")]
    public void AProjectNameMayNotBeAScopeOrARefKind(string name)
    {
        brain.MakeRepo("other");

        var (code, _, errors) = brain.Run("init", name);

        Assert.Equal(1, code);
        Assert.Contains($"name {name} is a scope or a ref kind", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AScopeOrKindPrefixWinsOverAProjectName()
    {
        brain.Ok("link", "generate-then-edit", "defined-in", "file:docs/ground.md");
        brain.Ok("link", "generate-then-edit", "for", "personal:generate-then-edit");

        Assert.EndsWith("- defined-in -> file:docs/ground.md\n- for -> generate-then-edit\n", brain.NodeText("personal", "generate-then-edit"), StringComparison.Ordinal);
        Assert.Contains("bad project name in Afterlife:wg-19", brain.Run("link", "generate-then-edit", "for", "Afterlife:wg-19").Err, StringComparison.Ordinal);
    }

    /// <summary>Another project, studio, with the cwd in it; afterlife's wg-19 holds an edge into personal.</summary>
    private string StudioWithAfterlifeEdge()
    {
        var afterlife = Path.Combine(brain.Root, "afterlife");
        brain.MakeRepo("studio");
        brain.Init();
        File.AppendAllText(Path.Combine(afterlife, "docs", "brain", "graph", "wg-19.md"), "- refines -> personal:generate-then-edit\n");
        return afterlife;
    }

    [Fact]
    public void CheckFromAnotherProjectFindsADanglingEdgeInAProject()
    {
        StudioWithAfterlifeEdge();
        Assert.Equal(0, brain.Run("check").Code);
        File.Delete(Path.Combine(brain.Home, "graph", "personal", "generate-then-edit.md"));

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(1, code);
        Assert.Equal("afterlife:wg-19: edge target personal:generate-then-edit does not exist\n", errors);
    }

    [Fact]
    public void ForgetUnlinksTheEdgeInAnotherProjectAndNamesItsDocs()
    {
        var afterlife = StudioWithAfterlifeEdge();

        var output = brain.Ok("forget", "personal:generate-then-edit");

        Assert.Equal(
            $"forgot personal:generate-then-edit\n  unlinked afterlife:wg-19 -refines-> personal:generate-then-edit\nchanged {Path.Combine(afterlife, "docs", "brain")}; commit it\n",
            output);
        Assert.DoesNotContain("refines", File.ReadAllText(Path.Combine(afterlife, "docs", "brain", "graph", "wg-19.md")), StringComparison.Ordinal);
        Assert.Equal(0, brain.Run("check").Code);
    }
}
