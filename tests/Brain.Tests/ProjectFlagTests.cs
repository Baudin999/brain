namespace Brain.Tests;

public sealed class ProjectFlagTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly string repo;

    /// <summary>A registered project studio with one node, wg-19, and the cwd moved out of every repo.</summary>
    public ProjectFlagTests()
    {
        repo = brain.MakeRepo("studio");
        brain.Init();
        brain.Ok("add", "--subjects", "test", "project", "wg-19", "The worldgen phase", "--kind", "idea");
        brain.Cwd = brain.Root;
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void WithoutTheFlagTheProjectScopeIsNotInEffectOutsideItsRepo()
    {
        Assert.Contains("project   none here", brain.Ok("context"), StringComparison.Ordinal);
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "worldgen"));
        Assert.Contains("no node", brain.Run("node", "wg-19").Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredNamePutsThatProjectsScopeInEffect()
    {
        Assert.Contains("project:wg-19", brain.Ok("--project", "studio", "lookup", "worldgen"), StringComparison.Ordinal);
        Assert.Contains("The worldgen phase", brain.Ok("--project", "studio", "node", "wg-19"), StringComparison.Ordinal);
        Assert.Contains("project:wg-19", brain.Ok("--project", "studio", "walk", "project:wg-19"), StringComparison.Ordinal);
        Assert.Contains("project   1 nodes in", brain.Ok("--project", "studio", "context"), StringComparison.Ordinal);
    }

    [Fact]
    public void APathPutsThatProjectsScopeInEffectAndWritesLandThere()
    {
        brain.Ok("--project", Path.Combine(repo, "docs"), "add", "--subjects", "test", "project", "wg-20", "The second worldgen phase", "--kind", "idea");

        Assert.True(File.Exists(Path.Combine(repo, "docs", "brain", "graph", "wg-20.md")));
        Assert.Contains("project:wg-20", brain.Ok("--project", "studio", "lookup", "second"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownNameARepoWithoutABrainOrAMissingValueIsRefused()
    {
        var unknown = brain.Run("--project", "elsewhere", "lookup", "worldgen");
        Assert.Equal((1, "brain: elsewhere is neither a project in projects.tsv nor a git repo\n"), (unknown.Code, unknown.Err));

        var bare = brain.MakeRepo("bare");
        brain.Cwd = brain.Root;
        var noBrain = brain.Run("--project", bare, "lookup", "worldgen");
        Assert.Equal((1, $"brain: {bare} has no project brain; run brain init there\n"), (noBrain.Code, noBrain.Err));

        var missing = brain.Run("--project");
        Assert.Equal(1, missing.Code);
        Assert.Contains("--project needs a project name", missing.Err, StringComparison.Ordinal);
    }
}
