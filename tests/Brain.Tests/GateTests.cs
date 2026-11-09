using System.Text.RegularExpressions;
using Brain.Staging;

namespace Brain.Tests;

/// <summary>
/// The user's gate on every node write from the command line: add, edit, update, merge and forget pass their own gates,
/// then wait as a pending write. <c>land</c> puts it before the user and returns the answer; only an answer through the
/// store, which brain's window gives, lands it, and it lands byte for byte what the window showed.
/// </summary>
public sealed partial class GateTests : IDisposable
{
    private readonly TempBrain brain = new();

    public GateTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "noise", "A finding that restates a commit", "--kind", "idea");
        brain.Ok("link", "meadow", "see", "noise");
        brain.SaysYes = false;
    }

    public void Dispose() => brain.Dispose();

    private string Pending => Path.Combine(brain.Home, "cache", "pending");

    private PendingStore Store => new(brain.Home);

    /// <summary>True when a write waits or was asked about: the seed's writes landed through the gate and left none.</summary>
    private bool AnyStaged => Directory.Exists(Pending) && Directory.EnumerateFiles(Pending, "*.json", SearchOption.TopDirectoryOnly).Any()
        || (Directory.Exists(Path.Combine(Pending, "asked")) && Directory.EnumerateFiles(Path.Combine(Pending, "asked")).Any());

    private bool Exists(string scope, string slug) => File.Exists(Path.Combine(brain.Home, "graph", scope, slug + ".md"));

    private static string Id(string output) => PendingId().Match(output).Groups[1].Value;

    /// <summary>Does what brain's window does on an answer, then runs land, which returns it.</summary>
    private (int Code, string Out, string Err) Answer(string id, bool yes, string? reason = null)
    {
        var store = Store;
        store.Ask(id);
        store.Answer(id, store.Asked().Single(asked => asked.Write.Id == id).Digest, yes, reason);
        return brain.Run("land", id);
    }

    [Fact]
    public void AddWaitsForTheUserAndShowsTheWholeNode()
    {
        var output = brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--why", "It shows in every session.", "--body", "Green, mostly.");

        Assert.False(Exists("global", "tea"));
        Assert.Contains("brain asks before it writes:\n  added global:tea\n\nglobal:tea, new:\n---\nkind: information\n", output, StringComparison.Ordinal);
        Assert.Contains("# The user drinks tea\n\nGreen, mostly.\n\n## Why\nIt shows in every session.\n", output, StringComparison.Ordinal);
        Assert.Contains($"Run `brain land {Id(output)}` alone, as its own command, in the background", output, StringComparison.Ordinal);
        Assert.Single(brain.Judge.Calls.Skip(2));
        Assert.Equal("check: 2 nodes, 1 edges, 0 errors in global, personal\n", brain.Ok("check"));
        Assert.DoesNotContain("global:tea", brain.Ok("lookup", "tea"), StringComparison.Ordinal);
    }

    [Fact]
    public void AYesLandsByteForByteWhatTheWriteHeld()
    {
        var output = brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--why", "It shows in every session.");
        var id = Id(output);
        Store.Ask(id);
        var asked = Store.Asked().Single();

        var landed = Answer(id, yes: true);

        Assert.Equal((0, "added global:tea\nlanded " + id + " on the user's yes\n", string.Empty), landed);
        var text = brain.NodeText("global", "tea");
        Assert.Equal(text, asked.Write.Changes.Single().After);
        Assert.Contains("global:tea, new:\n" + text, output, StringComparison.Ordinal);
        Assert.Empty(Store.Asked());
        brain.Ok("check");
    }

    [Fact]
    public async Task LandBlocksUntilTheUserAnswers()
    {
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));

        var land = Task.Run(() => brain.Run("land", id));
        while (!Store.IsAsked(id))
        {
            await Task.Delay(20);
        }

        Assert.NotSame(land, await Task.WhenAny(land, Task.Delay(400)));
        Assert.False(Exists("global", "tea"));
        Store.Answer(id, Store.Asked().Single().Digest, yes: true, reason: null);
        var (code, output, _) = await land.ConfigureAwait(true);

        Assert.Equal(0, code);
        Assert.StartsWith($"pending {id} is before the user in brain's window", output, StringComparison.Ordinal);
        Assert.EndsWith($"added global:tea\nlanded {id} on the user's yes\n", output, StringComparison.Ordinal);
        Assert.True(Exists("global", "tea"));
    }

    [Fact]
    public void ANoLandsNothingAndAReasonGoesBackInTheUsersWords()
    {
        var plain = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));
        var changed = Id(brain.Ok("add", "--subjects", "test", "global", "coffee", "The user drinks coffee", "--kind", "idea"));

        var no = Answer(plain, yes: false);
        var change = Answer(changed, yes: false, reason: "  Say it is green tea, mostly.\n");

        Assert.Equal(0, no.Code);
        Assert.Contains($"the user said no to {plain}: nothing landed", no.Out, StringComparison.Ordinal);
        Assert.Equal(0, change.Code);
        Assert.Contains($"the user said no to {changed} and said what to change, in their own words:\nSay it is green tea, mostly.\nNothing landed.", change.Out, StringComparison.Ordinal);
        Assert.False(Exists("global", "tea"));
        Assert.False(Exists("global", "coffee"));
        Assert.Empty(Store.Asked());
    }

    [Fact]
    public void TheCliAloneLandsNothing()
    {
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));
        Store.Ask(id);

        var gate = brain.Run("gate", "land", id);
        var wrongDigest = Assert.Throws<BrainException>(() => Store.Answer(id, new string('0', 64), yes: true, reason: null));

        Assert.Equal(2, gate.Code);
        Assert.Contains("is not the text the window showed", wrongDigest.Message, StringComparison.Ordinal);
        Assert.False(Exists("global", "tea"));
        Assert.Single(Store.Asked());
    }

    [Fact]
    public void AWriteIsAnsweredOnce()
    {
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));
        Store.Ask(id);
        var digest = Store.Asked().Single().Digest;
        Store.Answer(id, digest, yes: false, reason: null);

        var again = Assert.Throws<BrainException>(() => Store.Answer(id, digest, yes: true, reason: null));
        var land = brain.Run("land", id + "0");

        Assert.Contains($"pending write {id} is not before the user", again.Message, StringComparison.Ordinal);
        Assert.Equal(1, land.Code);
        Assert.False(Exists("global", "tea"));
        Assert.Contains("said no", brain.Ok("land", id), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAWriteFiledUnderItsOwnIdIsBeforeTheUser()
    {
        var id = Id(brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea"));
        Store.Ask(id);
        var asked = Path.Combine(Pending, "asked");
        File.Copy(Path.Combine(asked, id + ".json"), Path.Combine(asked, "toolu_old.json"));

        Assert.Equal([id], Store.Asked().Select(write => write.Write.Id));
    }

    [Fact]
    public void ANodeChangedSinceStagingRefusesTheWholeWrite()
    {
        var id = Id(brain.RunWithInput("---\nsubjects: [test]\n---\n# Fine ground cover\n\n## Why\nBare ground read as unfinished.\n", "edit", "meadow").Out);
        brain.SaysYes = true;
        brain.Ok("update", "meadow", "Ground cover first");
        brain.SaysYes = false;
        var before = brain.NodeText("global", "meadow");

        var landed = Answer(id, yes: true);

        Assert.Equal(1, landed.Code);
        Assert.Contains("global:meadow changed since pending write", landed.Err, StringComparison.Ordinal);
        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void EditAndUpdateWaitButSubjectsAloneLandAtOnce()
    {
        var before = brain.NodeText("global", "meadow");

        var edit = brain.RunWithInput("---\nsubjects: [test]\n---\n# Fine ground cover\n\nGrass first.\n", "edit", "meadow").Out;
        var update = brain.Ok("update", "meadow", "--kind", "information", "--why", "Bare ground read as unfinished.");
        brain.Ok("update", "meadow", "--subjects", "ground");

        Assert.Contains("global:meadow, rewritten:\n", edit, StringComparison.Ordinal);
        Assert.Contains("# Fine ground cover\n\nGrass first.\n", edit, StringComparison.Ordinal);
        Assert.Contains("kind: information\n", update, StringComparison.Ordinal);
        Assert.Contains("## Why\nBare ground read as unfinished.\n", update, StringComparison.Ordinal);
        Assert.Equal(before.Replace("subjects: [test]", "subjects: [ground]", StringComparison.Ordinal), brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void AMergeBlockIsOneWriteAndLandsWhole()
    {
        const string report = "+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:a [test]\n+ global:b [test]\n+ global:a -see-> global:b\n";

        var output = brain.RunWithInput(report, "merge").Out;
        var landed = Answer(Id(output), yes: true);

        Assert.Contains("global:a, new:\n", output, StringComparison.Ordinal);
        Assert.Contains("global:b, new:\n", output, StringComparison.Ordinal);
        Assert.Contains("  linked global:a -see-> global:b\n", output, StringComparison.Ordinal);
        Assert.Equal(0, landed.Code);
        Assert.Contains("- see -> b\n", brain.NodeText("global", "a"), StringComparison.Ordinal);
        Assert.True(Exists("global", "b"));
        brain.Ok("check");
    }

    [Fact]
    public void ForgetShowsEachNodeWholeAndRemovesNothingUntilTheYes()
    {
        var noise = brain.NodeText("global", "noise");

        var output = brain.Ok("forget", "noise");
        var stillThere = Exists("global", "noise");
        var landed = Answer(Id(output), yes: true);

        Assert.Contains("  forgot global:noise\n    unlinked global:meadow -see-> global:noise\n", output, StringComparison.Ordinal);
        Assert.Contains("global:noise, forgotten:\n" + noise, output, StringComparison.Ordinal);
        Assert.DoesNotContain("global:meadow, rewritten", output, StringComparison.Ordinal);
        Assert.True(stillThere);
        Assert.Equal(0, landed.Code);
        Assert.False(Exists("global", "noise"));
        Assert.Equal("check: 1 nodes, 0 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void ALongWriteStagesWhole()
    {
        var body = new string('a', 40000);

        var output = brain.Ok("add", "--subjects", "test", "global", "long", "A long node", "--kind", "idea", "--body", body);

        Assert.Contains(body, Store.IsWaiting(Id(output)) ? output : string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgesAreNotGated()
    {
        brain.Ok("link", "noise", "see", "meadow");

        Assert.Contains("- see -> meadow\n", brain.NodeText("global", "noise"), StringComparison.Ordinal);
        Assert.False(AnyStaged);
    }

    [GeneratedRegex("pending ([0-9a-f]{12}):")]
    private static partial Regex PendingId();
}
