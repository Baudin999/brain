using System.Diagnostics;
using Brain.Staging;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// brain never swallows an error: a command that fails prints one line and no stack, git that fails is a failure and
/// never a directory in no repo, a write with several files lands whole or not at all, every error is an event in the
/// usage log, and the session start tells the user of each once.
/// </summary>
public sealed class FailureTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose()
    {
        foreach (var directory in Directory.EnumerateDirectories(brain.Root, "*", SearchOption.AllDirectories))
        {
            OmarchyThemeTests.Chmod(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        brain.Dispose();
    }

    [Fact]
    public void AnUnexpectedFailurePrintsOneLineAndNoStackAndIsAnErrorInTheUsageLog()
    {
        OmarchyThemeTests.Chmod(Path.Combine(brain.Home, "graph", "global"), UnixFileMode.None);

        var (code, _, errors) = brain.Run("context");

        Assert.Equal(ExitCodes.Failure, code);
        var line = Assert.Single(errors.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("brain: brain context failed: ", line, StringComparison.Ordinal);
        Assert.Contains("the usage log keeps the whole error in " + Path.Combine(brain.Home, "usage"), line, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", errors[..errors.IndexOf("; the usage log", StringComparison.Ordinal)], StringComparison.Ordinal);
        var error = Assert.Single(Errors());
        Assert.Equal("brain context", error.Failed);
        Assert.Contains(nameof(UnauthorizedAccessException), error.Detail, StringComparison.Ordinal);
        Assert.Contains(" at ", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureBeforeTheCommandRunsMeetsTheSameBoundary()
    {
        using var errors = new StringWriter();

        var code = Cli.Guard(["--project", "studio", "lookup", "tea"], brain.Home, "s-1", errors, () => throw new FileNotFoundException("the working directory is gone"));

        Assert.Equal(ExitCodes.Failure, code);
        var line = Assert.Single(errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("brain: brain lookup failed: the working directory is gone; the usage log keeps the whole error in ", line, StringComparison.Ordinal);
        var error = Assert.Single(Errors());
        Assert.Equal(("brain lookup", "s-1"), (error.Failed, error.Session));
        Assert.Contains(nameof(FileNotFoundException), error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AHistoryGitCannotReadIsAFailure()
    {
        var repo = brain.MakeRepo("history");
        TempBrain.CommitAll(repo);

        var failure = Assert.Throws<BrainException>(() => Git.Changes(repo, "0123456789abcdef0123456789abcdef01234567"));
        var blobs = Git.Blobs(repo, ["HEAD:nothing.md"]);

        Assert.True(failure.IsFailure);
        Assert.StartsWith($"git could not read the history in {repo}: fatal: ", failure.Message, StringComparison.Ordinal);
        Assert.Equal([null], blobs);
        Assert.False(Git.IsAncestor(repo, "0123456789abcdef0123456789abcdef01234567", "HEAD"));
    }

    [Fact]
    public void GitThatFailsIsAFailureAndNeverADirectoryInNoRepo()
    {
        var repo = brain.MakeRepo("broken");
        File.AppendAllText(Path.Combine(repo, ".git", "config"), "[core\n");

        var (code, _, errors) = brain.Run("context");

        Assert.Equal(ExitCodes.Failure, code);
        Assert.StartsWith($"brain: brain context failed: git could not find the repo's top in {repo}: fatal: bad config", errors, StringComparison.Ordinal);
        Assert.Equal("brain context", Assert.Single(Errors()).Failed);
        Assert.Null(Git.TopLevel(brain.Transcripts));
    }

    [Fact]
    public void GitThatRunsPastItsLimitIsStoppedAndFails()
    {
        using var process = Process.Start("sleep", "30");
        using var watch = new Git.Watch(process, TimeSpan.FromMilliseconds(50));

        var ended = process.WaitForExit(TimeSpan.FromSeconds(10));

        Assert.True(ended);
        var failure = Assert.Throws<BrainException>(() => watch.ThrowIfStopped(brain.Root, "log"));
        Assert.True(failure.IsFailure);
        Assert.StartsWith($"git log in {brain.Root} ran for 0 seconds and was stopped", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSessionStartTellsOfANewErrorOnceAndThenNoMore()
    {
        var quiet = brain.Ok("context");
        Failures.Record(brain.Home, "brain lookup", new IOException("the disk is full"), session: null, TextWriter.Null);
        Failures.Record(brain.Home, "usage.sh", new IOException("brain record exited 134"), session: null, TextWriter.Null);

        var told = brain.Ok("context");
        var again = brain.Ok("context");

        Assert.DoesNotContain("errors ", quiet, StringComparison.Ordinal);
        var last = told.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        Assert.StartsWith("errors    2 errors since you were last told; the latest, at ", last, StringComparison.Ordinal);
        Assert.Contains(": usage.sh failed: brain record exited 134; each is an error event in " + Path.Combine(brain.Home, "usage"), last, StringComparison.Ordinal);
        Assert.Equal(quiet, again);
    }

    [Fact]
    public void AnErrorWrittenAfterTheReadAndBeforeTheToldIsToldAndNoneTwice()
    {
        Failures.Record(brain.Home, "brain lookup", new IOException("first"), session: null, TextWriter.Null);
        Assert.True(Failures.HasUntold(brain.Home));
        // Another session's hook writes an error between this start's read and its told.
        Failures.Record(brain.Home, "usage.sh", new IOException("second"), session: null, TextWriter.Null);

        var told = Failures.Tell(brain.Home, session: null, TextWriter.Null);
        Failures.Record(brain.Home, "refusal.sh", new IOException("third"), session: null, TextWriter.Null);
        var next = Failures.Untold(brain.Home, session: null, TextWriter.Null);
        var none = Failures.Untold(brain.Home, session: null, TextWriter.Null);

        Assert.StartsWith("errors    2 errors since you were last told; ", told, StringComparison.Ordinal);
        Assert.Contains("usage.sh failed: second", told, StringComparison.Ordinal);
        Assert.StartsWith("errors    1 error since you were last told; ", next, StringComparison.Ordinal);
        Assert.Contains("refusal.sh failed: third", next, StringComparison.Ordinal);
        Assert.Null(none);
    }

    [Fact]
    public void TwoStartsAtOnceTellDisjointErrors()
    {
        Failures.Record(brain.Home, "brain lookup", new IOException("first"), session: null, TextWriter.Null);
        Failures.Record(brain.Home, "brain node", new IOException("second"), session: null, TextWriter.Null);

        var one = Failures.Tell(brain.Home, session: "a", TextWriter.Null);
        var other = Failures.Tell(brain.Home, session: "b", TextWriter.Null);

        Assert.StartsWith("errors    2 errors since you were last told; ", one, StringComparison.Ordinal);
        Assert.Null(other);
    }

    [Fact]
    public void AWriteOfSeveralFilesThatFailsPartwayLeavesTheGraphAsItWas()
    {
        brain.WriteNode("global", "tea", "kind: idea\n", "The user drinks tea");
        var tea = Path.Combine(brain.Home, "graph", "global", "tea.md");
        var before = File.ReadAllText(tea);
        // A directory where the second node would go: its text is written beside it, but cannot replace it.
        var blocked = Path.Combine(brain.Home, "graph", "global", "coffee.md");
        Directory.CreateDirectory(blocked);
        var fresh = Path.Combine(brain.Home, "graph", "fresh", "cocoa.md");
        var store = new PendingStore(brain.Home);
        var write = store.Stage(
            ["changed global:tea", "added fresh:cocoa", "added global:coffee"],
            [
                new StagedChange("global:tea", tea, before, before + "more\n"),
                new StagedChange("fresh:cocoa", fresh, null, "cocoa\n"),
                new StagedChange("global:coffee", blocked, null, "coffee\n"),
            ],
            new WriteOrigin("s-1"));
        store.Ask(write.Id);

        var answer = store.Answer(write.Id, store.Asked().Single().Digest, yes: true, reason: null);

        Assert.Equal(PendingAnswer.Refused, answer.Verdict);
        Assert.StartsWith($"pending write {write.Id} failed: ", answer.Refusal, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(tea));
        Assert.False(Directory.Exists(Path.GetDirectoryName(fresh)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(blocked));
        Assert.Equal(["coffee.md", "tea.md"], Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(tea)!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var error = Assert.Single(Errors());
        Assert.Equal(($"pending write {write.Id}", "s-1"), (error.Failed, error.Session));
    }

    [Fact]
    public void AWriteThatCannotBeginLeavesNothingBehind()
    {
        var personal = Path.Combine(brain.Home, "graph", "personal");
        var store = new PendingStore(brain.Home);
        var write = store.Stage(
            ["added global:tea", "added personal:milk"],
            [
                new StagedChange("global:tea", Path.Combine(brain.Home, "graph", "global", "tea.md"), null, "tea\n"),
                new StagedChange("personal:milk", Path.Combine(personal, "milk.md"), null, "milk\n"),
            ],
            new WriteOrigin(null));
        store.Ask(write.Id);
        OmarchyThemeTests.Chmod(personal, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var answer = store.Answer(write.Id, store.Asked().Single().Digest, yes: true, reason: null);

        Assert.Equal(PendingAnswer.Refused, answer.Verdict);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(brain.Home, "graph", "global")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(personal));
        Assert.Single(Errors());
    }

    private List<UsageEvent> Errors() => [.. UsageLog.Read(brain.Home).Events.Where(usage => usage.Event == UsageEvent.Error)];
}
