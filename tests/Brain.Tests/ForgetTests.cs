namespace Brain.Tests;

public sealed class ForgetTests : IDisposable
{
    private readonly TempBrain brain = new();

    public ForgetTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "noise", "A finding that restates a commit", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "personal", "likes-tea", "The user drinks tea", "--kind", "idea");
        brain.Ok("link", "meadow", "see", "noise");
        brain.Ok("link", "meadow", "defined-in", "file:docs/meadow.md");
        brain.Ok("link", "likes-tea", "see", "global:noise");
        brain.Ok("link", "noise", "see", "meadow");
    }

    public void Dispose() => brain.Dispose();

    private bool Exists(string scope, string slug) => File.Exists(Path.Combine(brain.Home, "graph", scope, slug + ".md"));

    [Fact]
    public void ForgetRemovesTheNodeAndEveryEdgeIntoItAndCheckStaysGreen()
    {
        Assert.Equal(
            "forgot global:noise\n  unlinked global:meadow -see-> global:noise\n  unlinked personal:likes-tea -see-> global:noise\n",
            brain.Ok("forget", "noise"));

        Assert.False(Exists("global", "noise"));
        Assert.EndsWith("## Edges\n- defined-in -> file:docs/meadow.md\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
        Assert.EndsWith("## Edges\n", brain.NodeText("personal", "likes-tea"), StringComparison.Ordinal);
        Assert.Equal("check: 2 nodes, 1 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void ForgetTakesSeveralRefsAndListsNoEdgeBetweenThem()
    {
        Assert.Equal(
            "forgot global:meadow\nforgot global:noise\n  unlinked personal:likes-tea -see-> global:noise\n",
            brain.Ok("forget", "noise", "global:meadow", "meadow"));

        Assert.False(Exists("global", "meadow"));
        Assert.Equal("check: 1 nodes, 0 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void ForgetRefusesAMissingNodeAndChangesNothing()
    {
        var before = brain.NodeText("global", "meadow");

        var (code, _, errors) = brain.Run("forget", "noise", "nowhere");

        Assert.Equal(1, code);
        Assert.Equal("brain: no node nowhere\n", errors);
        Assert.True(Exists("global", "noise"));
        Assert.Equal(before, brain.NodeText("global", "meadow"));
        Assert.Equal(1, brain.Run("forget").Code);
    }

    [Fact]
    public void AProjectNodeLeavesWithTheEdgesGlobalNodesNameItBy()
    {
        var repo = brain.MakeRepo("afterlife");
        brain.Init();
        brain.Ok("add", "--subjects", "test", "project", "wg-19", "The worldgen phase", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "project", "wg-20", "The next phase", "--kind", "idea");
        brain.Ok("link", "wg-20", "after", "wg-19");
        brain.Ok("link", "global:meadow", "for", "afterlife:wg-19");
        brain.Cwd = brain.Root;
        var outside = brain.Run("forget", "afterlife:wg-19");
        brain.Cwd = repo;

        var output = brain.Ok("forget", "wg-19");

        Assert.Equal((1, "brain: afterlife:wg-19 is outside the scopes in effect; forget it from inside its repo\n"), (outside.Code, outside.Err));
        Assert.Equal("forgot project:wg-19\n  unlinked global:meadow -for-> afterlife:wg-19\n  unlinked project:wg-20 -after-> project:wg-19\n", output);
        Assert.False(File.Exists(Path.Combine(repo, "docs", "brain", "graph", "wg-19.md")));
        Assert.DoesNotContain("afterlife:wg-19", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
        Assert.Equal(0, brain.Run("check").Code);
    }

    [Fact]
    public void LookupNoLongerFindsAForgottenNodeByMeaning()
    {
        brain.Embedder.Meanings["restates"] = 0;
        brain.Embedder.Meanings["repeats"] = 0;
        // The adds embedded every node for their nearest entries before these meanings existed; a real model never changes under its name.
        File.Delete(brain.IndexPath);
        Assert.StartsWith("global:noise", brain.Ok("lookup", "repeats"), StringComparison.Ordinal);

        brain.Ok("forget", "noise");

        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "repeats"));
    }
}
