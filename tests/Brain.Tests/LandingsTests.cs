using Brain.Code;
using Brain.Windows;
using Brain.Workspace;

namespace Brain.Tests;

/// <summary>
/// Whether a piece of work landed, asked of a real repo's git and never of what an agent printed: a branch the
/// validator rebased, fast-forwarded the default branch with, then removed with its worktree, landed with the rebased
/// commits; a branch that never reached the default branch did not land; a landing of the same branch name before the
/// builder started is another work's.
/// </summary>
public sealed class LandingsTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly string repo;
    private readonly string tree;
    private readonly Records records;

    public LandingsTests()
    {
        repo = Path.Combine(brain.Root, "lens");
        Directory.CreateDirectory(repo);
        TempBrain.CommitAll(repo);
        TempBrain.Git(repo, "branch", "-M", "main");
        tree = Path.Combine(repo, ".claude", "worktrees", "lens");
        records = new Records(new WindowLog(brain.Home, WorkspaceServer.LogDir, "lens", TimeProvider.System), TimeProvider.System);
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ARebasedBranchLandedWithItsRebasedCommitsAfterItsWorktreeAndBranchAreGone()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        TempBrain.Git(repo, "worktree", "add", "-q", tree, "-b", "lens", "main");
        Commit(tree, "Draw the lens");
        Commit(tree, "Test the lens");
        var built = Hashes(tree, "main..lens");
        Commit(repo, "Something else on main");
        var before = Short(repo, "main");
        TempBrain.Git(tree, "rebase", "-q", "main");
        TempBrain.Git(repo, "merge", "-q", "--ff-only", "lens");
        TempBrain.Git(repo, "worktree", "remove", tree);
        TempBrain.Git(repo, "branch", "-q", "-d", "lens");

        var told = new Landings(repo, records).Told(Steps(), "lens", since);

        var landed = Hashes(repo, before + "..main");
        Assert.Equal($"{before}..{Short(repo, "main")}", told[1].Landed);
        Assert.Equal(landed, told[1].Commits.Select((commit) => commit.Hash));
        Assert.Equal(["Draw the lens", "Test the lens"], told[1].Commits.Select((commit) => commit.Subject));
        Assert.Empty(landed.Intersect(built));
        Assert.Null(told[0].Landed);
    }

    [Fact]
    public void ABranchThatNeverReachedTheDefaultBranchDidNotLand()
    {
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        TempBrain.Git(repo, "worktree", "add", "-q", tree, "-b", "lens", "main");
        Commit(tree, "Draw the lens");

        var told = new Landings(repo, records).Told(Steps(), "lens", since);

        Assert.All(told, (step) => Assert.Null(step.Landed));
        Assert.All(told, (step) => Assert.Empty(step.Commits));
    }

    [Fact]
    public void ALandingOfTheSameBranchBeforeTheBuilderStartedIsAnotherWorks()
    {
        TempBrain.Git(repo, "worktree", "add", "-q", tree, "-b", "lens", "main");
        Commit(tree, "Draw the lens");
        TempBrain.Git(repo, "merge", "-q", "--ff-only", "lens");

        var told = new Landings(repo, records).Told(Steps(), "lens", DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.All(told, (step) => Assert.Null(step.Landed));
    }

    /// <summary>A work's steps: the builder's report and a validator's report.</summary>
    private static List<CodeStep> Steps() =>
    [
        new CodeStep(Trail.Report, "b1", null, "Built the lens.", [], Landed: null, Round: 0),
        new CodeStep(Trail.Verdict, "v1", null, "Done.", [], Landed: null, Round: null),
    ];

    /// <summary>Commits a new file in <paramref name="directory"/>, named for <paramref name="subject"/>.</summary>
    private static void Commit(string directory, string subject)
    {
        var file = subject.Replace(' ', '-') + ".txt";
        File.WriteAllText(Path.Combine(directory, file), subject);
        TempBrain.Git(directory, "add", file);
        TempBrain.Git(directory, "commit", "-q", "-m", subject);
    }

    private static string Short(string directory, string commit) => TempBrain.Git(directory, "rev-parse", "--short", commit).Trim();

    private static List<string> Hashes(string directory, string range) =>
        [.. TempBrain.Git(directory, "log", "--reverse", "--format=%h", range).Split('\n', StringSplitOptions.RemoveEmptyEntries)];
}
