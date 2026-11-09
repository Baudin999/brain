using Brain.Windows;
using Brain.Workspace;

namespace Brain.Tests;

/// <summary>
/// Git for a workspace's root, against a real repo: what is uncommitted against HEAD, staged and unstaged together, with
/// what is untracked; a file's hunks with the lines they removed; staging and unstaging a whole file; a commit of what is
/// staged; a push of the current branch to its upstream that never forces; a root that is a folder of its repo; a root in
/// no repo; and a record for every git call.
/// </summary>
public sealed class WorkspaceGitTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly string repo;
    private readonly Records records;

    public WorkspaceGitTests()
    {
        repo = Path.Combine(brain.Root, "lens");
        Directory.CreateDirectory(Path.Combine(repo, "src"));
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\ntwo\nthree\nfour\n");
        File.WriteAllText(Path.Combine(repo, "src", "Old.cs"), "gone\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# Lens\n");
        TempBrain.CommitAll(repo);
        TempBrain.Git(repo, "branch", "-M", "master");
        records = new Records(new WindowLog(brain.Home, WorkspaceServer.LogDir, "lens", TimeProvider.System), TimeProvider.System);
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void TheStatusSaysEachFileAddedChangedDeletedOrUntracked()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\nTWO\nthree\nfour\n");
        File.Delete(Path.Combine(repo, "src", "Old.cs"));
        File.WriteAllText(Path.Combine(repo, "src", "Lens.cs"), "new\n");
        TempBrain.Git(repo, "add", "src/Lens.cs");
        File.WriteAllText(Path.Combine(repo, "notes.txt"), "loose\n");

        var status = Git(repo).Status();

        Assert.True(status.Repo);
        Assert.Equal("master", status.Branch);
        Assert.Equal(
            ["notes.txt untracked False True", "src/Fold.cs changed False True", "src/Lens.cs added True False", "src/Old.cs deleted False True"],
            status.Files.Select((file) => $"{file.Path} {file.State} {file.Staged} {file.Unstaged}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADiffGivesWhereEachHunkSits()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "zero\none\nTWO\nfour\n");

        var diff = Git(repo).Diff("src/Fold.cs");

        Assert.Equal(
            ["0,0 1,1", "2,2 3,1"],
            diff.Hunks.Select((hunk) => $"{hunk.OldStart},{hunk.OldCount} {hunk.NewStart},{hunk.NewCount}"));
    }

    [Fact]
    public void AHunkThatOnlyRemovesSaysTheLineItFollows()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\nfour\n");

        var hunk = Assert.Single(Git(repo).Diff("src/Fold.cs").Hunks);

        Assert.Equal((2, 2, 1, 0), (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount));
    }

    [Fact]
    public void AnUnstagedPatchHoldsEveryLineOfTheFileWithTheRemovedOnesInPlace()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\nTWO\nfour\nfive\n");

        var patch = Git(repo).Patch("src/Fold.cs", false);

        Assert.Null(patch.Problem);
        Assert.Equal(["same one", "removed two", "removed three", "added TWO", "same four", "added five"], Lines(patch));
    }

    [Fact]
    public void AStagedPatchIsTheIndexAgainstHeadAndTheUnstagedOneWhatIsLeft()
    {
        var git = Git(repo);
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\nTWO\nthree\nfour\n");
        git.Stage("src/Fold.cs");
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "one\nTWO\nthree\nFOUR\n");

        Assert.Equal(["same one", "removed two", "added TWO", "same three", "same four"], Lines(git.Patch("src/Fold.cs", true)));
        Assert.Equal(["same one", "same TWO", "same three", "removed four", "added FOUR"], Lines(git.Patch("src/Fold.cs", false)));
    }

    [Fact]
    public void AnUntrackedFileIsAllAddedAndADeletedOneAllRemoved()
    {
        File.WriteAllText(Path.Combine(repo, "notes.txt"), "loose\nends\n");
        File.Delete(Path.Combine(repo, "src", "Old.cs"));
        var git = Git(repo);

        Assert.Equal(["added loose", "added ends"], Lines(git.Patch("notes.txt", false)));
        Assert.Equal(["removed gone"], Lines(git.Patch("src/Old.cs", false)));
    }

    [Fact]
    public void ABinaryFileHasNoLines()
    {
        File.WriteAllBytes(Path.Combine(repo, "pic.png"), [0, 1, 2, 0, 255]);

        var patch = Git(repo).Patch("pic.png", false);

        Assert.Equal("binary", patch.Problem);
        Assert.Empty(patch.Lines);
    }

    [Fact]
    public void AFileOverTheReadersLimitHasNoLinesAndSaysSo()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), new string('x', 2 * 1024 * 1024 + 1) + "\n");
        File.WriteAllText(Path.Combine(repo, "big.txt"), new string('y', 2 * 1024 * 1024 + 1) + "\n");
        var git = Git(repo);

        var tracked = git.Patch("src/Fold.cs", false);
        var untracked = git.Patch("big.txt", false);

        Assert.Equal(("too large", 0), (tracked.Problem, tracked.Lines.Count));
        Assert.Equal(("too large", 0), (untracked.Problem, untracked.Lines.Count));
    }

    [Fact]
    public void TheRemoteSaysTheCommitsAPushWouldSendAndTheOnesBehind()
    {
        var remote = Remote();
        var other = Path.Combine(brain.Root, "other");
        TempBrain.Git(brain.Root, "clone", "-q", remote, other);
        TempBrain.Git(other, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "theirs");
        TempBrain.Git(other, "push", "-q", "origin", "master");
        TempBrain.Git(repo, "fetch", "-q");
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        var git = Git(repo);
        git.Stage("src/Fold.cs");
        git.Commit("Fold changed");

        var sides = git.Remote();

        Assert.Equal(["Fold changed"], sides.Ahead.Select((commit) => commit.Subject));
        Assert.Equal(["theirs"], sides.Behind.Select((commit) => commit.Subject));
    }

    private static IEnumerable<string> Lines(GitPatch patch) => patch.Lines.Select((line) => $"{line.Kind} {line.Text}");

    [Fact]
    public void AWholeFileStagesAndUnstages()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        File.Delete(Path.Combine(repo, "src", "Old.cs"));
        var git = Git(repo);

        git.Stage("src/Fold.cs");
        git.Stage("src/Old.cs");
        var staged = git.Status();
        git.Unstage("src/Fold.cs");
        var unstaged = git.Status();

        Assert.All(staged.Files, (file) => Assert.True(file.Staged && !file.Unstaged, file.Path));
        var fold = Assert.Single(unstaged.Files, (file) => file.Path == "src/Fold.cs");
        Assert.False(fold.Staged);
        Assert.True(fold.Unstaged);
        Assert.Equal("changed\n", File.ReadAllText(Path.Combine(repo, "src", "Fold.cs")));
    }

    [Fact]
    public void AFileOutsideTheRootIsRefused()
    {
        var refusal = Assert.Throws<BrainException>(() => Git(repo).Stage("../secret.txt"));

        Assert.False(refusal.IsFailure);
    }

    [Fact]
    public void ACommitTakesWhatIsStagedAndNothingElse()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# Lens, changed\n");
        var git = Git(repo);
        git.Stage("src/Fold.cs");

        var done = git.Commit("Fold changed");

        Assert.StartsWith("Committed ", done.Said, StringComparison.Ordinal);
        Assert.EndsWith(" Fold changed", done.Said, StringComparison.Ordinal);
        Assert.Equal("Fold changed\n", TempBrain.Git(repo, "log", "-1", "--format=%s"));
        Assert.Equal(["README.md"], git.Status().Files.Select((file) => file.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ACommitWithoutAMessageIsRefused(string? message)
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        var git = Git(repo);
        git.Stage("src/Fold.cs");

        var refusal = Assert.Throws<BrainException>(() => git.Commit(message));

        Assert.False(refusal.IsFailure);
        Assert.Contains("message", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommitWithNothingStagedSaysWhyGitRefused()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");

        var refusal = Assert.Throws<BrainException>(() => Git(repo).Commit("Nothing staged"));

        Assert.False(refusal.IsFailure);
        Assert.Contains("did not commit", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("no changes added to commit", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APushSendsTheBranchToItsUpstream()
    {
        var remote = Remote();
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        var git = Git(repo);
        git.Stage("src/Fold.cs");
        git.Commit("Fold changed");

        var done = git.Push();

        Assert.Equal("Pushed master to origin master", done.Said);
        Assert.Equal(TempBrain.Git(repo, "rev-parse", "HEAD"), TempBrain.Git(remote, "rev-parse", "master"));
        Assert.Equal(0, git.Status().Ahead);
    }

    [Fact]
    public void APushNeverForces()
    {
        var remote = Remote();
        var other = Path.Combine(brain.Root, "other");
        TempBrain.Git(brain.Root, "clone", "-q", remote, other);
        TempBrain.Git(other, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "theirs");
        TempBrain.Git(other, "push", "-q", "origin", "master");
        var theirs = TempBrain.Git(remote, "rev-parse", "master");
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        var git = Git(repo);
        git.Stage("src/Fold.cs");
        git.Commit("Ours");

        var refusal = Assert.Throws<BrainException>(git.Push);

        Assert.False(refusal.IsFailure);
        Assert.Contains("rejected", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(theirs, TempBrain.Git(remote, "rev-parse", "master"));
    }

    [Fact]
    public void APushWithoutAnUpstreamIsRefused()
    {
        var refusal = Assert.Throws<BrainException>(Git(repo).Push);

        Assert.False(refusal.IsFailure);
        Assert.Contains("no upstream", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A diff of a file whose stat git has not seen leaves the index as it was: no rewrite, no lock.</summary>
    [Fact]
    public void ADiffLeavesTheIndexAlone()
    {
        var fold = Path.Combine(repo, "src", "Fold.cs");
        File.WriteAllText(fold, "one\nTWO\nthree\nfour\n");
        File.SetLastWriteTimeUtc(fold, DateTime.UtcNow.AddMinutes(1));
        var index = Path.Combine(repo, ".git", "index");
        var bytes = File.ReadAllBytes(index);
        var written = File.GetLastWriteTimeUtc(index);
        var git = Git(repo);

        Assert.Single(git.Diff("src/Fold.cs").Hunks);
        git.Patch("src/Fold.cs", false);
        git.Patch("src/Fold.cs", true);
        git.Status();

        Assert.Equal(written, File.GetLastWriteTimeUtc(index));
        Assert.Equal(bytes, File.ReadAllBytes(index));
    }

    /// <summary>
    /// A root that is one folder of its repo commits what is staged under it and nothing staged outside it, which stays
    /// staged; the repo's index then says only the outside file is staged.
    /// </summary>
    [Fact]
    public void ACommitFromAFolderLeavesWhatIsStagedOutsideIt()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# Lens, changed\n");
        File.WriteAllText(Path.Combine(repo, "outside.txt"), "new\n");
        TempBrain.Git(repo, "add", "README.md", "outside.txt");
        var before = TempBrain.Git(repo, "rev-parse", "HEAD");
        var git = Git(Path.Combine(repo, "src"));
        git.Stage("Fold.cs");

        var done = git.Commit("Fold changed");

        Assert.EndsWith(" Fold changed", done.Said, StringComparison.Ordinal);
        Assert.Equal("src/Fold.cs\n", TempBrain.Git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"));
        Assert.Equal(before, TempBrain.Git(repo, "rev-parse", "HEAD~1"));
        Assert.Equal("README.md\noutside.txt\n", TempBrain.Git(repo, "diff", "--cached", "--name-only"));
        Assert.Equal(string.Empty, TempBrain.Git(repo, "status", "--porcelain", "--", "src"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(repo, ".git"), "brain-workspace-index-*"));
    }

    [Fact]
    public void ARootInsideItsRepoSeesItsOwnFilesFromItself()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        File.WriteAllText(Path.Combine(repo, "README.md"), "# Lens, changed\n");
        var git = Git(Path.Combine(repo, "src"));

        var status = git.Status();
        git.Stage("Fold.cs");

        Assert.Equal(["Fold.cs"], status.Files.Select((file) => file.Path));
        Assert.Single(git.Diff("Fold.cs").Hunks);
        Assert.Contains(git.Status().Files, (file) => file.Path == "Fold.cs" && file.Staged);
        Assert.Contains(Path.Combine(repo, ".git"), git.Watched);
    }

    [Fact]
    public void ARootInNoRepoHasNoStatusAndRefusesTheRest()
    {
        var loose = Path.Combine(Path.GetTempPath(), "brain-tests-loose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(loose);
        try
        {
            var git = Git(loose);

            Assert.False(git.Status().Repo);
            Assert.Empty(git.Watched);
            Assert.False(Assert.Throws<BrainException>(() => git.Stage("a.txt")).IsFailure);
        }
        finally
        {
            Directory.Delete(loose, recursive: true);
        }
    }

    [Fact]
    public void EveryGitCallIsARecordWithItsBudget()
    {
        File.WriteAllText(Path.Combine(repo, "src", "Fold.cs"), "changed\n");
        var git = Git(repo);

        git.Status();
        git.Diff("src/Fold.cs");
        git.Stage("src/Fold.cs");

        var kept = records.Since(0).Records.Where((record) => record.Name == "git").ToList();
        Assert.Contains(kept, (record) => record.Args == "status" && record.Budget == WorkspaceGit.StatusBudget);
        Assert.Contains(kept, (record) => record.Args == "diff-index src/Fold.cs" && record.Budget == WorkspaceGit.DiffBudget);
        Assert.Contains(kept, (record) => record.Args == "add src/Fold.cs");
    }

    private WorkspaceGit Git(string root) => new(new ProjectFiles(root, records), records);

    /// <summary>A bare repo the checkout pushes to, its master the checkout's upstream.</summary>
    private string Remote()
    {
        var remote = Path.Combine(brain.Root, "remote.git");
        TempBrain.Git(brain.Root, "init", "-q", "--bare", remote);
        TempBrain.Git(repo, "remote", "add", "origin", remote);
        TempBrain.Git(repo, "push", "-q", "-u", "origin", "master");
        return remote;
    }
}
