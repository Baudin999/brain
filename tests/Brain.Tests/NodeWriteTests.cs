using System.Text.RegularExpressions;
using Brain.Judging;
using Brain.Staging;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// A node write carries its subjects and its edges: add, edit and merge take them in the write, the free rules refuse a
/// node write without a subject, the edges land only with the user's yes, and the judge reads each write with the
/// subjects in use in its scope and its nearest entries, so the gate refuses a duplicate, a contradiction and a write
/// that carries no edge while the judge names one, and logs each.
/// </summary>
public sealed partial class NodeWriteTests : IDisposable
{
    private const string Chai = "---\nsubjects: [test]\n---\n# The user drinks chai\n\nSpiced, with milk.\n";

    private readonly TempBrain brain = new();

    public NodeWriteTests()
    {
        brain.Embedder.Meanings["tea"] = 0;
        brain.Embedder.Meanings["chai"] = 0;
        brain.Embedder.Meanings["grass"] = 1;
        brain.Ok("add", "--subjects", "drinks", "personal", "tea", "The user drinks tea", "--kind", "idea", "--body", "Green tea in the morning.");
        brain.Ok("add", "--subjects", "ground", "global", "meadow", "Grass grows first on open ground", "--kind", "idea");
        brain.Judge.Calls.Clear();
    }

    public void Dispose() => brain.Dispose();

    private string ChaiPath => Path.Combine(brain.Home, "graph", "global", "chai.md");

    private List<UsageEvent> Logged(string kind) => [.. UsageLog.Read(brain.Home).Events.Where(usage => usage.Event == kind)];

    [Fact]
    public void AnAddCarriesItsSubjectsAndEdgesAndTheyLandWithTheYes()
    {
        var output = brain.RunWithInput(Chai + "\n## Edges\n- refines -> personal:tea\n", "add", "global", "chai", "--kind", "idea").Out;

        Assert.Equal("added global:chai\n", output);
        Assert.Equal(
            "---\nkind: idea\nsubjects: [test]\nnamed-by: claude\nborn: 2026-10-03\n---\n# The user drinks chai\n\nSpiced, with milk.\n\n## Edges\n- refines -> personal:tea\n",
            File.ReadAllText(ChaiPath));
        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal([new Edge("refines", "personal:tea")], entry.Edges);
        Assert.Equal("check: 3 nodes, 1 edges, 0 errors in global, personal\n", brain.Ok("check"));
    }

    [Fact]
    public void ANoLinksNoneOfTheEdges()
    {
        brain.SaysYes = false;
        var tea = brain.NodeText("personal", "tea");

        var output = brain.RunWithInput(Chai + "\n## Edges\n- refines -> personal:tea\n", "add", "global", "chai", "--kind", "idea").Out;
        var id = PendingId().Match(output).Groups[1].Value;
        Assert.Contains("## Edges\n- refines -> personal:tea\n", output, StringComparison.Ordinal);
        Assert.False(File.Exists(ChaiPath));

        var store = new PendingStore(brain.Home);
        store.Ask(id);
        store.Answer(id, store.Asked().Single(asked => asked.Write.Id == id).Digest, yes: false, reason: null);
        brain.Run("land", id);

        Assert.False(File.Exists(ChaiPath));
        Assert.Equal(tea, brain.NodeText("personal", "tea"));
    }

    [Fact]
    public void AnEdgeLinkWouldRefuseIsRefusedBeforeTheJudge()
    {
        var missing = brain.RunWithInput(Chai + "\n## Edges\n- refines -> personal:nothing\n", "add", "global", "chai", "--kind", "idea");
        var commit = brain.RunWithInput(Chai + "\n## Edges\n- built -> commit:b0e944e\n", "add", "global", "chai", "--kind", "idea");
        var relation = brain.RunWithInput(Chai + "\n## Edges\n- Refines -> personal:tea\n", "add", "global", "chai", "--kind", "idea");

        Assert.Equal("brain: no node personal:nothing\n", missing.Err);
        Assert.Equal(1, commit.Code);
        Assert.Equal("brain: relation Refines is not lowercase-hyphenated\n", relation.Err);
        Assert.Empty(brain.Judge.Calls);
        Assert.False(File.Exists(ChaiPath));
    }

    [Fact]
    public void ANodeWriteWithoutASubjectIsRefusedBeforeTheJudge()
    {
        var plain = brain.Run("add", "global", "chai", "The user drinks chai", "--kind", "idea");
        var markdown = brain.RunWithInput("# The user drinks chai\n", "add", "global", "chai", "--kind", "idea");
        var merged = brain.RunWithInput("+ global:chai | The user drinks chai | idea | agent\n", "merge");

        const string refusal = "brain: refused global:chai: a node needs at least one subject, a keyword a search finds it by. Run brain subjects global to reuse a word already in use\n";
        Assert.Equal(refusal, plain.Err);
        Assert.Equal(refusal, markdown.Err);
        Assert.Equal(refusal, merged.Err);
        Assert.Empty(brain.Judge.Calls);
        Assert.False(File.Exists(ChaiPath));
    }

    [Fact]
    public void UpdateOfABareNodeNeedsNoSubject()
    {
        brain.WriteNode("global", "bare", "kind: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "A bare idea");

        brain.Ok("update", "bare", "A bare idea still");

        Assert.Contains("# A bare idea still\n", brain.NodeText("global", "bare"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheJudgeReadsTheSubjectsInUseAndTheNearestEntriesInAnyScope()
    {
        brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal("personal:tea", entry.Nearest[0].Ref);
        Assert.True(entry.Nearest.Count <= 5);
        Assert.Equal(["ground"], entry.InUse);
        var prompt = JudgePrompt.Build("- Rule one.", [entry]);
        Assert.Contains("\nSubjects in use in global: ground\n", prompt, StringComparison.Ordinal);
        Assert.Contains("Nearest entry 1 of entry 1\nref: personal:tea\nkind: idea\ntitle: The user drinks tea\nbody:\nGreen tea in the morning.\nsubjects: [drinks]\n", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateIsRefusedNamingTheNodeAndLogged()
    {
        brain.Judge.Answer = new Verdict(Ok: false, "global:chai says what personal:tea says") { Duplicates = [new Duplicate("global:chai", "personal:tea")] };

        var (code, output, errors) = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Equal("brain: the judge refused: global:chai says what personal:tea says global:chai says the same as personal:tea, which the graph holds already.\n", errors);
        var logged = Assert.Single(Logged(UsageEvent.Duplicate));
        Assert.Equal(("global:chai", "The user drinks chai", "personal:tea"), (logged.Ref, logged.Title, logged.Match));
        Assert.False(File.Exists(ChaiPath));
    }

    [Fact]
    public void AContradictionIsRefusedEvenWhenTheJudgeSaysOkAndLogged()
    {
        brain.Judge.Answer = Verdict.Accept with { Contradictions = [new Contradiction("global:chai", "personal:tea", "tea is green, chai is black")] };

        var (code, _, errors) = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Equal("brain: the judge refused: global:chai contradicts personal:tea: tea is green, chai is black\n", errors);
        var logged = Assert.Single(Logged(UsageEvent.Contradiction));
        Assert.Equal(("global:chai", "personal:tea", "tea is green, chai is black"), (logged.Ref, logged.Match, logged.Reason));
    }

    [Fact]
    public void ADuplicateTheJudgeAlsoNamesAsAContradictionCountsAsTheContradictionOnly()
    {
        brain.Judge.Answer = Verdict.Accept with
        {
            Duplicates = [new Duplicate("global:chai", "personal:tea")],
            Contradictions = [new Contradiction("global:chai", "personal:tea", "tea is green, chai is black")],
        };

        var (code, _, errors) = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Equal("brain: the judge refused: global:chai contradicts personal:tea: tea is green, chai is black\n", errors);
        Assert.Empty(Logged(UsageEvent.Duplicate));
        var logged = Assert.Single(Logged(UsageEvent.Contradiction));
        Assert.Equal(("global:chai", "personal:tea"), (logged.Ref, logged.Match));
        var section = new Brain.Stats.JudgeMetric().Measure(UsageLog.Read(brain.Home).Events);
        Assert.Equal(["  1 writes refused against the nearest entries: 0 duplicates, 1 contradictions, 0 without edges"], section.Lines);
        Assert.False(File.Exists(ChaiPath));
    }

    [Fact]
    public void AFindingOnARefTheJudgeWasNotShownCountsForNothing()
    {
        brain.Judge.Answer = Verdict.Accept with
        {
            Duplicates = [new Duplicate("global:chai", "global:invented"), new Duplicate("global:other", "personal:tea")],
            Contradictions = [new Contradiction("global:chai", "global:invented", "made up")],
        };

        var accepted = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.True(accepted.Code == 0, accepted.Err);
        Assert.Empty(Logged(UsageEvent.Duplicate));
        Assert.Empty(Logged(UsageEvent.Contradiction));

        brain.Judge.Answer = new Verdict(Ok: false, "it is a work log") { Duplicates = [new Duplicate("global:tisane", "global:invented")] };
        var refused = brain.RunWithInput(Chai.Replace("chai", "tisane", StringComparison.Ordinal), "add", "global", "tisane", "--kind", "idea");

        Assert.Equal("brain: the judge refused: it is a work log\n", refused.Err);
        Assert.Empty(Logged(UsageEvent.Duplicate));
    }

    [Fact]
    public void AWriteWithoutEdgesIsRefusedWithTheEdgesTheJudgeNames()
    {
        brain.Judge.Answer = Verdict.Accept with { Edges = [new NamedEdge("global:chai", "refines", "personal:tea")] };

        var (code, _, errors) = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Equal("brain: the judge refused: global:chai carries no edge, and the judge names its edges: refines -> personal:tea. Carry them in the write and write it again.\n", errors);
        var logged = Assert.Single(Logged(UsageEvent.MissingEdges));
        Assert.Equal(("global:chai", "The user drinks chai"), (logged.Ref, logged.Title));
        Assert.Equal(["refines -> personal:tea"], logged.Edges);

        var again = brain.RunWithInput(Chai + "\n## Edges\n- refines -> personal:tea\n", "add", "global", "chai", "--kind", "idea");

        Assert.Equal(0, again.Code);
        Assert.Single(Logged(UsageEvent.MissingEdges));
    }

    [Fact]
    public void AnEdgeToAnEntryThatIsNotNearOrThatLinkRefusesIsNoReasonToRefuse()
    {
        brain.Judge.Answer = Verdict.Accept with
        {
            Edges = [new NamedEdge("global:chai", "refines", "global:nowhere"), new NamedEdge("global:chai", "Refines", "personal:tea")],
        };

        var (code, _, errors) = brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        Assert.True(code == 0, errors);
        Assert.Empty(Logged(UsageEvent.MissingEdges));
    }

    [Fact]
    public void AMergedNodeCarriesItsSubjectLineAndEdgeLinesToTheJudge()
    {
        brain.Judge.Answer = Verdict.Accept with { Edges = [new NamedEdge("global:chai", "refines", "personal:tea")] };

        var bare = brain.RunWithInput("+ global:chai | The user drinks chai | idea | agent\n+ global:chai [test]\n", "merge");
        var linked = brain.RunWithInput("+ global:chai | The user drinks chai | idea | agent\n+ global:chai [test]\n+ global:chai -refines-> personal:tea\n", "merge");

        Assert.Contains("global:chai carries no edge", bare.Err, StringComparison.Ordinal);
        Assert.True(linked.Code == 0, linked.Err);
        Assert.EndsWith("## Edges\n- refines -> personal:tea\n", File.ReadAllText(ChaiPath), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEvidenceCountsEveryCaughtWrite()
    {
        brain.Judge.Answer = new Verdict(Ok: false, "a duplicate") { Duplicates = [new Duplicate("global:chai", "personal:tea")] };
        brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");
        brain.Judge.Answer = Verdict.Accept with { Edges = [new NamedEdge("global:chai", "refines", "personal:tea")] };
        brain.RunWithInput(Chai, "add", "global", "chai", "--kind", "idea");

        var output = brain.Ok("stats");

        Assert.Contains(
            "  Without brain's judge, 1 duplicate and 0 contradictions would have entered the graph and 1 node would have landed with no edges.\n"
            + "    - The user drinks chai: global:chai says the same as personal:tea\n"
            + "    - The user drinks chai: global:chai carried no edge; the judge named refines -> personal:tea",
            output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"reason\":\"\",\"duplicates\":{}}")]
    [InlineData("{\"ok\":true,\"reason\":\"\",\"edges\":[{\"entry\":\"global:a\",\"rel\":\"refines\"}]}")]
    [InlineData("{\"ok\":true,\"reason\":\"\",\"contradictions\":[\"global:a\"]}")]
    public void AVerdictWithAListThatIsNotOneIsNoVerdict(string answer) => Assert.Null(Verdict.Parse(answer));

    [Fact]
    public void AVerdictReadsItsDuplicatesContradictionsAndEdges()
    {
        var verdict = Verdict.Parse(
            "{\"ok\":false,\"reason\":\"r\",\"duplicates\":[{\"entry\":\"global:a\",\"node\":\"global:b\"}],"
            + "\"contradictions\":[{\"entry\":\"global:a\",\"node\":\"personal:c\",\"what\":\"w\"}],\"edges\":[{\"entry\":\"global:a\",\"rel\":\"refines\",\"to\":\"global:b\"}]}")!;

        Assert.Equal([new Duplicate("global:a", "global:b")], verdict.Duplicates);
        Assert.Equal([new Contradiction("global:a", "personal:c", "w")], verdict.Contradictions);
        Assert.Equal([new NamedEdge("global:a", "refines", "global:b")], verdict.Edges);
    }

    [GeneratedRegex("pending ([0-9a-f]{12}): nothing is written yet")]
    private static partial Regex PendingId();
}
