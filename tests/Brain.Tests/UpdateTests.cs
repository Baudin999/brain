namespace Brain.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void UpdateRewritesOnlyTheTitle()
    {
        brain.Ok("add", "--subjects", "test", "personal", "likes-tea", "The user drinks tea", "--named-by", "user", "--session", "abc-1", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("link", "personal:likes-tea", "see", "global:meadow");

        Assert.Equal("updated personal:likes-tea\n", brain.Ok("update", "likes-tea", "  The user drinks green tea  "));

        Assert.Equal(
            "---\nkind: idea\nsubjects: [test]\nnamed-by: user\nborn: 2026-10-03\nsession: abc-1\n---\n# The user drinks green tea\n\n## Edges\n- see -> global:meadow\n",
            brain.NodeText("personal", "likes-tea"));
    }

    [Fact]
    public void UpdateWritesTheBodyAndTheWhyAndKeepsTheTitle()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");

        brain.Ok("update", "meadow", "--body", "Grass and moss.", "--why", "Bare ground read as unfinished.", "--kind", "information");

        Assert.EndsWith(
            "---\n# Ground cover\n\nGrass and moss.\n\n## Why\nBare ground read as unfinished.\n\n## Edges\n",
            brain.NodeText("global", "meadow"),
            StringComparison.Ordinal);
        Assert.Equal(1, brain.Run("update", "meadow", "--why", "## Edges").Code);
    }

    [Fact]
    public void UpdateRefusesALongTitleAMissingNodeAndAMissingTitle()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        var longTitle = string.Join(' ', Enumerable.Repeat("word", 26));

        Assert.Equal(1, brain.Run("update", "meadow", longTitle).Code);
        Assert.Equal(1, brain.Run("update", "meadow", "  ").Code);
        Assert.Equal(1, brain.Run("update", "nowhere", "Label").Code);
        Assert.Equal(1, brain.Run("update", "meadow").Code);
        Assert.Contains("# Ground cover\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteReplacesTheNodeFileWholeSoAReaderNeverSeesHalfOfIt()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        var path = Path.Combine(brain.Home, "graph", "global", "meadow.md");
        var before = File.ReadAllText(path);
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        brain.Ok("update", "meadow", "Ground cover grows back");

        using var text = new StreamReader(reader);
        Assert.Equal(before, text.ReadToEnd());
        Assert.Contains("# Ground cover grows back", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }
}
