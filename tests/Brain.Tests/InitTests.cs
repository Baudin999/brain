namespace Brain.Tests;

public sealed class InitTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string Registry => Path.Combine(brain.Home, "projects.tsv");

    [Fact]
    public void InitLaysOutDocsBrainAndRegistersTheProject()
    {
        var repo = brain.MakeRepo("My_Game");
        TempBrain.Git(repo, "remote", "add", "origin", "git@example.com:me/my-game.git");

        var output = brain.Ok("init");

        var root = Path.Combine(repo, "docs", "brain");
        Assert.Equal(
            $"created {root}\nregistered my-game ~/My_Game git@example.com:me/my-game.git\n",
            output);
        Assert.True(File.Exists(Path.Combine(root, "graph", ".gitkeep")));
        Assert.Equal(["graph"], Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
        Assert.Equal(
            "name\tpath\tremote\tdate\nmy-game\t~/My_Game\tgit@example.com:me/my-game.git\t2026-10-03\n",
            File.ReadAllText(Registry));
        Assert.Contains("project   0 nodes in", brain.Ok("context"), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Fact]
    public void ASecondRunChangesNothingAndSaysSo()
    {
        var repo = brain.MakeRepo("game");
        brain.Ok("init");
        var registry = File.ReadAllText(Registry);

        var output = brain.Ok("init");

        Assert.Equal($"exists {Path.Combine(repo, "docs", "brain")}\nalready registered game ~/game -\n", output);
        Assert.Equal(registry, File.ReadAllText(Registry));
    }

    [Fact]
    public void WithoutAnOriginTheRemoteIsADash()
    {
        brain.MakeRepo("game");

        brain.Ok("init", "custom-name");

        Assert.EndsWith("custom-name\t~/game\t-\t2026-10-03\n", File.ReadAllText(Registry), StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideAGitRepoInitIsRefused()
    {
        var (code, _, errors) = brain.Run("init");

        Assert.Equal(1, code);
        Assert.Contains("not inside a git repo", errors, StringComparison.Ordinal);
        Assert.False(File.Exists(Registry));
    }

    [Fact]
    public void InsideTheBrainInitIsRefused()
    {
        TempBrain.Git(brain.Home, "init", "-q");
        brain.Cwd = Path.Combine(brain.Home, "graph", "global");

        var (code, _, errors) = brain.Run("init");

        Assert.Equal(1, code);
        Assert.Contains("this is the brain itself", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "docs")));
        Assert.False(File.Exists(Registry));
    }

    [Fact]
    public void ANameRegisteredForAnotherPathIsRefused()
    {
        brain.MakeRepo("game");
        brain.Ok("init");
        var other = brain.MakeRepo("other");

        var (code, _, errors) = brain.Run("init", "game");

        Assert.Equal(1, code);
        Assert.Contains("the name game is registered for ~/game", errors, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(other, "docs")));
        Assert.Single(File.ReadAllLines(Registry).Skip(1));
    }

    [Fact]
    public void ARepoRegisteredUnderAnotherNameIsRefused()
    {
        brain.MakeRepo("game");
        brain.Ok("init");

        var (code, _, errors) = brain.Run("init", "renamed");

        Assert.Equal(1, code);
        Assert.Contains("~/game is registered as game", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameThatIsNotASlugIsRefused()
    {
        brain.MakeRepo("game");

        var (code, _, errors) = brain.Run("init", "Not A Slug");

        Assert.Equal(1, code);
        Assert.Contains("not lowercase-hyphenated", errors, StringComparison.Ordinal);
    }
}
