using Brain.Commands;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// brain serve starts where its caller ran, and falls back when that directory or project was removed since, so a
/// restart always gets a successor that can start.
/// </summary>
public sealed class ServeProcessTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ItStartsInTheCallersDirectoryAndProject()
    {
        var repo = brain.MakeRepo("studio");

        var (cwd, arguments) = ServeProcess.Where(Env() with { Cwd = repo, ProjectRepo = repo });

        Assert.Equal(repo, cwd);
        Assert.Equal([ProjectFlag.Flag, repo, ServeCommand.Name], arguments);
    }

    [Fact]
    public void ARemovedDirectoryFallsBackToTheDataRepo()
    {
        var (cwd, arguments) = ServeProcess.Where(Env() with { Cwd = Path.Combine(brain.Root, "removed-worktree") });

        Assert.Equal(brain.Home, cwd);
        Assert.Equal([ServeCommand.Name], arguments);
    }

    [Fact]
    public void ARemovedProjectIsDropped()
    {
        var (cwd, arguments) = ServeProcess.Where(Env() with { ProjectRepo = Path.Combine(brain.Root, "removed-project") });

        Assert.Equal(brain.Cwd, cwd);
        Assert.Equal([ServeCommand.Name], arguments);
    }

    private BrainEnv Env() => brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null);
}
