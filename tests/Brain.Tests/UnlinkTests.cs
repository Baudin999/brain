namespace Brain.Tests;

public sealed class UnlinkTests : IDisposable
{
    private readonly TempBrain brain = new();

    public UnlinkTests()
    {
        brain.Ok("add", "--subjects", "test", "personal", "likes-tea", "The user drinks tea", "--named-by", "user", "--session", "abc-1", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "personal", "kettle", "The kettle", "--kind", "idea");
        brain.Ok("link", "likes-tea", "see", "global:meadow");
        brain.Ok("link", "likes-tea", "uses", "kettle");
        brain.Ok("link", "likes-tea", "defined-in", "file:docs/meadow.md");
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void UnlinkRemovesOnlyThatEdgeLine()
    {
        Assert.Equal("unlinked personal:likes-tea uses personal:kettle\n", brain.Ok("unlink", "likes-tea", "uses", "personal:kettle"));
        Assert.Equal("unlinked personal:likes-tea defined-in file:docs/meadow.md\n", brain.Ok("unlink", "personal:likes-tea", "defined-in", "file:docs/meadow.md"));

        Assert.Equal(
            "---\nkind: idea\nsubjects: [test]\nnamed-by: user\nborn: 2026-10-03\nsession: abc-1\n---\n# The user drinks tea\n\n## Edges\n- see -> global:meadow\n",
            brain.NodeText("personal", "likes-tea"));
    }

    [Fact]
    public void RepointingIsUnlinkThenLink()
    {
        brain.Ok("unlink", "likes-tea", "see", "global:meadow");
        brain.Ok("link", "likes-tea", "see", "kettle");

        Assert.EndsWith("## Edges\n- uses -> kettle\n- defined-in -> file:docs/meadow.md\n- see -> kettle\n", brain.NodeText("personal", "likes-tea"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnlinkRefusesAMissingEdgeAndChangesNothing()
    {
        var before = brain.NodeText("personal", "likes-tea");

        var (code, _, errors) = brain.Run("unlink", "likes-tea", "see", "kettle");

        Assert.Equal(1, code);
        Assert.Equal("brain: no edge personal:likes-tea see kettle\n", errors);
        Assert.Equal(1, brain.Run("unlink", "likes-tea", "see", "nowhere").Code);
        Assert.Equal(1, brain.Run("unlink", "nowhere", "see", "kettle").Code);
        Assert.Equal(1, brain.Run("unlink", "likes-tea", "see").Code);
        Assert.Equal(before, brain.NodeText("personal", "likes-tea"));
    }

    [Fact]
    public void UnlinkRemovesAnEdgeWhoseTargetIsGone()
    {
        brain.Ok("add", "--subjects", "test", "global", "gone", "A node that will vanish", "--kind", "idea");
        brain.Ok("link", "likes-tea", "see", "global:gone");
        brain.Ok("link", "global:meadow", "see", "gone");
        File.Delete(Path.Combine(brain.Home, "graph", "global", "gone.md"));

        Assert.Equal("unlinked personal:likes-tea see global:gone\n", brain.Ok("unlink", "likes-tea", "see", "global:gone"));
        Assert.Equal("unlinked global:meadow see global:gone\n", brain.Ok("unlink", "meadow", "see", "global:gone"));

        Assert.DoesNotContain("gone", brain.NodeText("personal", "likes-tea"), StringComparison.Ordinal);
        Assert.DoesNotContain("gone", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
        Assert.Equal(1, brain.Run("unlink", "likes-tea", "see", "global:gone").Code);
        Assert.Equal(0, brain.Run("check").Code);
    }
}
