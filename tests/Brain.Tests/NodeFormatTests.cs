namespace Brain.Tests;

public sealed class NodeFormatTests
{
    private const string Sample =
        "---\nkind: decision\nphase: concept\nsubjects: [ground, ui]\nnamed-by: user\nborn: 2026-10-03\nsession: f1d7\n---\n"
        + "# Fine ground cover\n\nGrass first.\n\nThen moss.\n\n## Why\nBare ground read as unfinished.\n\n"
        + "## Edges\n- built -> commit:b09256f9\n- part-of -> wg-12\n";

    [Fact]
    public void ParseReadsEveryPart()
    {
        var node = NodeParser.Parse("meadow", Sample);

        Assert.Equal("Fine ground cover", node.Title);
        Assert.Equal("Grass first.\n\nThen moss.", node.Body);
        Assert.Equal("Bare ground read as unfinished.", node.Why);
        Assert.Equal("user", node.Field("named-by"));
        Assert.Equal("[ground, ui]", node.Field("subjects"));
        Assert.Equal(new DateOnly(2026, 10, 3), node.Born);
        Assert.Equal([new Edge("built", "commit:b09256f9"), new Edge("part-of", "wg-12")], node.Edges);
        Assert.Empty(node.Malformed);
    }

    [Fact]
    public void RenderRoundTripsTheCanonicalLayout() =>
        Assert.Equal(Sample, NodeRenderer.Render(NodeParser.Parse("meadow", Sample)));

    [Fact]
    public void RenderLeavesOutAnEmptyBodyAndWhy()
    {
        const string bare = "---\nkind: tool\nsubjects: []\nnamed-by: agent\nborn: 2026-10-03\n---\n# Ollama\n\n## Edges\n";

        var node = NodeParser.Parse("ollama", bare);

        Assert.Equal((string.Empty, string.Empty), (node.Body, node.Why));
        Assert.Equal(bare, NodeRenderer.Render(node));
    }

    [Fact]
    public void ParseKeepsLinesItCannotPlace()
    {
        var node = NodeParser.Parse("x", "---\nkind: tool\nno separator\n---\n# X\n\n## Edges\n- no arrow here\nstray prose\n");

        Assert.Equal(["no separator", "- no arrow here", "stray prose"], node.Malformed);
    }

    [Fact]
    public void ParsePlacesNothingOfAnOldFormatFile()
    {
        var node = NodeParser.Parse("x", "# x\n\nA label\n\n- Kind: tool\n\n## Edges\n- see -> y\n");

        Assert.Equal([NodeParser.OldFormat], node.Malformed);
        Assert.Null(node.Title);
        Assert.Empty(node.Edges);
    }
}
