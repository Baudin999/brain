using System.Text.RegularExpressions;
using Brain.Staging;

namespace Brain.Tests;

/// <summary>
/// The answers the user gave, kept for two days in the data repo's cache: each write exactly as it was asked, its answer
/// and its time. Undo on a yes reverses exactly what the write did, all or nothing, and refuses when a node changed since
/// or another node links to one it would remove, and commits exactly the files it changed in the repo that holds them.
/// Undo on a no puts the write before the user again. An answer is undone once.
/// </summary>
public sealed partial class AnswerHistoryTests : IDisposable
{
    private readonly TempBrain brain = new();

    public AnswerHistoryTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "noise", "A finding that restates a commit", "--kind", "idea");
        brain.Ok("link", "meadow", "see", "noise");
    }

    public void Dispose() => brain.Dispose();

    private PendingStore Store => new(brain.Home);

    private string History => Path.Combine(brain.Home, "cache", "pending", "history");

    private bool Exists(string slug) => File.Exists(Path.Combine(brain.Home, "graph", "global", slug + ".md"));

    private Graph Graph => Graph.Load(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null));

    /// <summary>The answered write the last command staged, the newest answer.</summary>
    private AnsweredWrite Last => Store.Answered()[0];

    private static string Id(string output) => PendingId().Match(output).Groups[1].Value;

    private Undone Undo(AnsweredWrite answered) => Store.Undo(answered.Record.Write.Id, answered.Digest, Graph);

    [Fact]
    public void EveryAnswerIsKeptWithTheWriteAsItWasAskedTheNewestFirst()
    {
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--why", "It shows in every session.");
        brain.SaysYes = false;
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "coffee", "The user drinks coffee", "--kind", "idea"));
        Store.Ask(id);
        Store.Answer(id, Store.Asked().Single().Digest, yes: false, reason: "Call it espresso.");

        var answered = Store.Answered();

        Assert.Equal(id, answered[0].Record.Write.Id);
        Assert.Equal(PendingAnswer.No, answered[0].Record.Answer.Verdict);
        Assert.Equal("Call it espresso.", answered[0].Record.Answer.Reason);
        Assert.Equal(PendingAnswer.Yes, answered[1].Record.Answer.Verdict);
        Assert.Equal(brain.NodeText("global", "tea"), answered[1].Record.Write.Changes.Single().After);
        Assert.Equal(["added global:tea"], answered[1].Record.Write.Lines);
        Assert.True(answered[0].Record.AnsweredAt >= answered[1].Record.AnsweredAt);
    }

    [Fact]
    public void AnUndoneAddGoesWithTheEdgesItBrought()
    {
        brain.RunWithInput("+ global:tea | The user drinks tea | idea | agent\n+ global:tea [test]\n+ global:tea -see-> global:meadow\n", "merge");
        var added = Last;
        Assert.Contains("see", brain.NodeText("global", "tea"), StringComparison.Ordinal);

        Assert.Equal(PendingAnswer.Yes, Undo(added).Undid);

        Assert.False(Exists("tea"));
        Assert.NotNull(Last.Record.UndoneAt);
        Assert.Equal("check: 2 nodes, 1 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void AnUndoneYesCommitsTheFilesItChangedAndNothingElse()
    {
        File.WriteAllText(Path.Combine(brain.Home, ".gitignore"), "cache/\n");
        File.WriteAllText(Path.Combine(brain.Home, "staged.txt"), "staged\n");
        TempBrain.CommitAll(brain.Home);
        brain.RunWithInput("---\nsubjects: [test]\n---\n# Fine ground cover\n\nGrass first.\n", "edit", "meadow");
        var edit = Last;
        TempBrain.Git(brain.Home, "commit", "-q", "-am", "edited meadow");
        File.WriteAllText(Path.Combine(brain.Home, "staged.txt"), "staged, changed\n");
        TempBrain.Git(brain.Home, "add", "staged.txt");
        File.WriteAllText(Path.Combine(brain.Home, "loose.txt"), "loose\n");
        brain.WriteNode("global", "draft", "kind: idea\n", "A node nobody committed");

        var undone = Undo(edit);

        var commit = Assert.Single(undone.Commits);
        Assert.Null(undone.NotCommitted);
        Assert.Equal(commit, TempBrain.Git(brain.Home, "rev-parse", "HEAD").Trim());
        Assert.Equal($"Undo of brain write {edit.Record.Write.Id}\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s"));
        Assert.Equal("M\tgraph/global/meadow.md\n", TempBrain.Git(brain.Home, "show", "--format=", "--name-status", "HEAD"));
        Assert.Equal("M  staged.txt\n?? graph/global/draft.md\n?? loose.txt\n", TempBrain.Git(brain.Home, "status", "--porcelain"));
    }

    [Fact]
    public void AnUndoneAddOfAProjectNodeCommitsInTheProjectRepo()
    {
        brain.MakeProject();
        var repo = brain.Cwd;
        TempBrain.Git(repo, "config", "user.name", "t");
        TempBrain.Git(repo, "config", "user.email", "t@t");
        TempBrain.Git(repo, "config", "commit.gpgsign", "false");
        brain.Ok("add", "--subjects", "test", "project", "kiln", "The kiln fires at dawn", "--kind", "idea");
        TempBrain.Git(repo, "add", "-A");
        TempBrain.Git(repo, "commit", "-q", "-m", "added kiln");

        var undone = Undo(Last);

        Assert.Null(undone.NotCommitted);
        Assert.Equal(Assert.Single(undone.Commits), TempBrain.Git(repo, "rev-parse", "HEAD").Trim());
        Assert.Equal("D\tdocs/brain/graph/kiln.md\n", TempBrain.Git(repo, "show", "--format=", "--name-status", "HEAD"));
        Assert.Equal(string.Empty, TempBrain.Git(repo, "status", "--porcelain"));
    }

    [Fact]
    public void AnUndoneYesThatWasNeverCommittedHasNothingToCommit()
    {
        File.WriteAllText(Path.Combine(brain.Home, ".gitignore"), "cache/\n");
        TempBrain.CommitAll(brain.Home);
        var head = TempBrain.Git(brain.Home, "rev-parse", "HEAD");
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea");

        var undone = Undo(Last);

        Assert.Empty(undone.Commits);
        Assert.Null(undone.NotCommitted);
        Assert.Equal(head, TempBrain.Git(brain.Home, "rev-parse", "HEAD"));
    }

    [Fact]
    public void AnUndoThatCannotCommitStandsAndSaysWhy()
    {
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea");

        var undone = Undo(Last);

        Assert.False(Exists("tea"));
        Assert.Empty(undone.Commits);
        Assert.Equal("global:tea lies in no git repo", undone.NotCommitted);
        Assert.NotNull(Last.Record.UndoneAt);
    }

    [Fact]
    public void AnUndoneYesNoLongerReadsAsLanded()
    {
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));

        Undo(Last);

        Assert.Null(Store.AnswerOf(id));
        var (code, output, _) = brain.Run("land", id);
        Assert.NotEqual(0, code);
        Assert.DoesNotContain("landed " + id, output, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUndoneEditGetsBackTheTextBeforeIt()
    {
        var before = brain.NodeText("global", "meadow");
        brain.RunWithInput("---\nsubjects: [test]\n---\n# Fine ground cover\n\nGrass first.\n", "edit", "meadow");

        Undo(Last);

        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void AnUndoneForgetBringsBackTheNodeAndTheEdgesItRemoved()
    {
        var meadow = brain.NodeText("global", "meadow");
        var noise = brain.NodeText("global", "noise");
        brain.Ok("forget", "noise");
        Assert.False(Exists("noise"));

        Undo(Last);

        Assert.Equal(noise, brain.NodeText("global", "noise"));
        Assert.Equal(meadow, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void AnUndoneMergeReversesAllItsLines()
    {
        brain.RunWithInput("+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:a [test]\n+ global:b [test]\n+ global:a -see-> global:b\n", "merge");

        Undo(Last);

        Assert.False(Exists("a"));
        Assert.False(Exists("b"));
    }

    [Fact]
    public void ANodeChangedSinceTheYesRefusesTheWholeUndo()
    {
        brain.RunWithInput("+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:a [test]\n+ global:b [test]\n", "merge");
        var merged = Last;
        brain.Ok("update", "b", "Second, renamed");
        var renamed = brain.NodeText("global", "b");

        var refusal = Assert.Throws<BrainException>(() => Undo(merged));

        Assert.Equal($"global:b changed since your yes to {merged.Record.Write.Id}; nothing was undone", refusal.Message);
        Assert.True(Exists("a"));
        Assert.Equal(renamed, brain.NodeText("global", "b"));
        Assert.Null(Store.Answered().Single(one => one.Record.Write.Id == merged.Record.Write.Id).Record.UndoneAt);
    }

    [Fact]
    public void ALinkIntoANodeTheUndoRemovesRefusesIt()
    {
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea");
        var added = Last;
        brain.Ok("link", "noise", "see", "tea");

        var refusal = Assert.Throws<BrainException>(() => Undo(added));

        Assert.StartsWith("global:noise -see-> global:tea since your yes", refusal.Message, StringComparison.Ordinal);
        Assert.True(Exists("tea"));
    }

    [Fact]
    public void AnUndoneNoPutsTheWriteBeforeTheUserAgain()
    {
        brain.SaysYes = false;
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));
        Store.Ask(id);
        var asked = Store.Asked().Single().Write;
        Store.Answer(id, Store.Asked().Single().Digest, yes: false, reason: null);

        var undone = Undo(Last);

        Assert.Equal(PendingAnswer.No, undone.Undid);
        Assert.Empty(undone.Commits);
        Assert.Null(undone.NotCommitted);

        Assert.Equal(asked, Store.Asked().Single().Write, new WriteComparer());
        Assert.Null(Store.AnswerOf(id));
        Store.Answer(id, Store.Asked().Single().Digest, yes: true, reason: null);
        Assert.True(Exists("tea"));
        Assert.Null(Last.Record.UndoneAt);
    }

    [Fact]
    public void AnAnswerIsUndoneOnceAndOnlyAsTheWindowShowedIt()
    {
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea");
        var added = Last;

        var stale = Assert.Throws<BrainException>(() => Store.Undo(added.Record.Write.Id, new string('0', 64), Graph));
        Undo(added);
        var again = Assert.Throws<BrainException>(() => Store.Undo(added.Record.Write.Id, Last.Digest, Graph));

        Assert.Contains("is not the one the window showed", stale.Message, StringComparison.Ordinal);
        Assert.Contains("was undone already", again.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerIsKeptForTwoDaysThenSwept()
    {
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea");
        var record = Path.Combine(History, Last.Record.Write.Id + ".json");
        var old = Last.Record with { AnsweredAt = DateTime.UtcNow - TimeSpan.FromDays(2.5) };
        File.WriteAllText(record, System.Text.Json.JsonSerializer.Serialize(old, PendingJson.Default.AnswerRecord));
        File.SetLastWriteTimeUtc(record, old.AnsweredAt);

        Assert.DoesNotContain(Store.Answered(), one => one.Record.Write.Id == old.Write.Id);
        brain.SaysYes = false;
        brain.Ok("add", "--subjects", "test", "global", "coffee", "The user drinks coffee", "--kind", "idea");

        Assert.False(File.Exists(record));
    }

    [GeneratedRegex("pending ([0-9a-f]{12}): nothing is written yet")]
    private static partial Regex PendingId();

    /// <summary>Two writes are the same when their id, lines and changes are: a record compares its lists by reference.</summary>
    private sealed class WriteComparer : IEqualityComparer<PendingWrite>
    {
        public bool Equals(PendingWrite? x, PendingWrite? y) =>
            x is not null && y is not null && x.Id == y.Id && x.Origin == y.Origin && x.Lines.SequenceEqual(y.Lines) && x.Changes.SequenceEqual(y.Changes);

        public int GetHashCode(PendingWrite obj) => obj.Id.GetHashCode(StringComparison.Ordinal);
    }
}
