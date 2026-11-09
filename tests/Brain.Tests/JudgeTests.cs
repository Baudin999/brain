using System.Text.Json.Nodes;
using Brain.Judging;
using Brain.Models;

namespace Brain.Tests;

public sealed class JudgeTests : IDisposable
{
    private const string Refusal = "entry 1 is a work log; write the subject that lasts";

    private const string Skill = """
        # Brain

        ## Writing

        - The brain is not a work log.
          What happened lives in git.
        - Write the why.
        - A decision rarely needs superseding.
        - A node is a subject that lasts.
        - The conversation draws the nodes.

        ## Next
        - Not a rule.
        """;

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AddPassesTheEntryToTheJudgeAndStaysSilentWhenItPasses()
    {
        var output = brain.Ok("add", "--subjects", "test", "global", "binding", "Binding resolves every name once", "--kind", "idea");

        Assert.Equal("added global:binding\n", output);
        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal(("global", "binding", "idea", "Binding resolves every name once", string.Empty, string.Empty), (entry.Scope, entry.Slug, entry.Kind, entry.Title, entry.Body, entry.Why));
        Assert.Equal(["test"], entry.Subjects);
        Assert.True(entry.NodeWrite);
        Assert.Empty(entry.Edges);
    }

    [Fact]
    public void AddRefusedWritesNothingAndPrintsTheReason()
    {
        brain.Judge.Refusal = Refusal;

        var (code, _, errors) = brain.Run("add", "--subjects", "test", "global", "review", "Binding review applied", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Equal($"brain: the judge refused: {Refusal}\n", errors);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "review.md")));
    }

    [Fact]
    public void AddRefusesABadSlugBeforeItAsksTheJudge()
    {
        var (code, _, _) = brain.Run("add", "--subjects", "test", "global", "Bad Slug", "A label", "--kind", "idea");

        Assert.Equal(1, code);
        Assert.Empty(brain.Judge.Calls);
    }

    [Fact]
    public void UpdateRefusedKeepsTheOldTitle()
    {
        brain.Ok("add", "--subjects", "test", "global", "binding", "Binding resolves every name once", "--kind", "idea");
        brain.Judge.Refusal = Refusal;

        var (code, _, errors) = brain.Run("update", "global:binding", "Binding review applied");

        Assert.Equal(1, code);
        Assert.Contains(Refusal, errors, StringComparison.Ordinal);
        Assert.Contains("Binding resolves every name once", brain.NodeText("global", "binding"), StringComparison.Ordinal);
        Assert.Equal("Binding review applied", brain.Judge.Calls[^1][0].Title);
    }

    [Fact]
    public void MergeAsksTheJudgeOnceForAllNewNodes()
    {
        brain.Ok("add", "--subjects", "test", "global", "old", "Already here", "--kind", "idea");
        brain.Judge.Calls.Clear();
        const string report = "+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:old | Again | idea | agent\n+ global:a [test]\n+ global:b [test]\n+ global:a -rel-> global:b\n";

        brain.RunWithInput(report, "merge");

        var call = Assert.Single(brain.Judge.Calls);
        Assert.Equal(["a", "b"], call.Select(entry => entry.Slug));
        Assert.Equal([new Edge("rel", "b")], call[0].Edges);
    }

    [Fact]
    public void MergeRefusedAppliesNothing()
    {
        brain.Judge.Refusal = Refusal;
        const string report = "+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:a [test]\n+ global:b [test]\n+ global:a -rel-> global:b\n";

        var (code, output, errors) = brain.RunWithInput(report, "merge");

        Assert.Equal(1, code);
        Assert.Equal(string.Empty, output);
        Assert.Contains(Refusal, errors, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(brain.Home, "graph", "global")));
    }

    [Fact]
    public void MergeOfOnlyEdgesDoesNotAskTheJudge()
    {
        brain.Ok("add", "--subjects", "test", "global", "a", "First", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "b", "Second", "--kind", "idea");
        brain.Judge.Calls.Clear();

        brain.RunWithInput("+ global:a -rel-> global:b\n", "merge");

        Assert.Empty(brain.Judge.Calls);
    }

    [Fact]
    public void WritingRulesAreTheFirstFourBulletsUnderWriting()
    {
        Assert.Equal(
            "- The brain is not a work log. What happened lives in git.\n- Write the why.\n- A decision rarely needs superseding.\n- A node is a subject that lasts.",
            WritingRules.From(Skill));
        Assert.Null(WritingRules.From("# Brain\n\n## Writing\n\n- Only one rule.\n"));
        Assert.Null(WritingRules.From("# Brain\n"));
    }

    [Fact]
    public void ThePromptCarriesTheRulesTheEntriesAndEachPartApart()
    {
        var entry = new JudgedEntry("global", "bind-once", "information", "Bind every name once", "One pass binds.", "Flow looks a callee up once.");

        var prompt = JudgePrompt.Build("- Rule one.", [entry, entry with { Slug = "bare", Body = string.Empty }]);

        Assert.Contains("- Rule one.\n", prompt, StringComparison.Ordinal);
        Assert.Contains(
            "Entry 1\nref: global:bind-once\nscope: global\nslug: bind-once\nkind: information\ntitle: Bind every name once\nbody:\nOne pass binds.\nwhy:\nFlow looks a callee up once.\n"
            + "subjects: []\nedges: none\nnearest entries: none\n\n",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "Entry 2\nref: global:bare\nscope: global\nslug: bare\nkind: information\ntitle: Bind every name once\nwhy:\nFlow looks a callee up once.\nsubjects: []\nedges: none\n",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains("never read a reason into the title or the body", prompt, StringComparison.Ordinal);
        Assert.Contains("Never suggest a reason", prompt, StringComparison.Ordinal);
        Assert.Contains("Examples are good.", prompt, StringComparison.Ordinal);
        Assert.Contains("Never refuse an entry for carrying examples.", prompt, StringComparison.Ordinal);
        Assert.Contains("Never refuse an entry for listing what we rejected and why.", prompt, StringComparison.Ordinal);
        Assert.Contains("A log is bad.", prompt, StringComparison.Ordinal);
        Assert.Contains("Never refuse for structure.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("missing edge", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("show no edges", prompt, StringComparison.Ordinal);
        Assert.Contains("{\"ok\":false,\"reason\":", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"reason\":\"\"}", true, "")]
    [InlineData("```json\n{\"ok\":false,\"reason\":\"a work log\"}\n```", false, "a work log")]
    public void VerdictParsesAOneLineAnswer(string answer, bool ok, string reason)
    {
        var verdict = Verdict.Parse(answer)!;

        Assert.Equal((ok, reason), (verdict.Ok, verdict.Reason));
        Assert.Empty(verdict.Duplicates);
        Assert.Empty(verdict.Contradictions);
        Assert.Empty(verdict.Edges);
    }

    [Theory]
    [InlineData("I think it is fine.")]
    [InlineData("{\"ok\":\"yes\",\"reason\":\"\"}")]
    [InlineData("{\"ok\":false,\"reason\":\"\"}")]
    [InlineData("{\"ok\":true}")]
    [InlineData("{not json}")]
    public void VerdictRefusesAnAnswerThatIsNotOne(string answer) => Assert.Null(Verdict.Parse(answer));

    [Fact]
    public void ClaudeJudgeAsksTheModelAndEffortOfTheJudgeSetting()
    {
        var rules = Path.Combine(brain.Root, "SKILL.md");
        File.WriteAllText(rules, Skill);
        var judge = new ClaudeJudge(rules, brain.Models, brain.Home);
        brain.Models.Drafts["sonnet"] = "{\"ok\":true,\"reason\":\"\"}";
        brain.Models.Drafts["opus"] = "{\"ok\":false,\"reason\":\"a work log\"}";
        var entries = (IReadOnlyList<JudgedEntry>)[JudgedEntry.Of(new NewNode("global", "a", "A", "idea", "agent", Session: null))];

        var first = judge.Judge(entries);
        TempBrain.CommitAll(brain.Home);
        BrainSettings.Change(brain.Home, "judge", JsonNode.Parse("""{"model":"opus","effort":"xhigh"}"""));
        var second = judge.Judge(entries);

        Assert.Equal(((true, string.Empty), (false, "a work log")), ((first.Ok, first.Reason), (second.Ok, second.Reason)));
        Assert.Equal([("sonnet", "medium"), ("opus", "xhigh")], brain.Models.Calls.Select(call => (call.Model, call.Effort)));
        Assert.All(brain.Models.Calls, call => Assert.Equal(JudgePrompt.System, call.System));
    }

    [Fact]
    public void ClaudeJudgeFailsClosedWhenTheSettingsCannotBeRead()
    {
        var rules = Path.Combine(brain.Root, "SKILL.md");
        File.WriteAllText(rules, Skill);
        File.WriteAllText(Path.Combine(brain.Home, BrainSettings.FileName), """{"judge":{"model":"gemma4","effort":"medium"}}""");
        var judge = new ClaudeJudge(rules, brain.Models, brain.Home);

        var refusal = Assert.Throws<BrainException>(() => judge.Judge([JudgedEntry.Of(new NewNode("global", "a", "A", "idea", "agent", Session: null))]));

        Assert.StartsWith("the judge is unreachable: settings.json in ", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(brain.Models.Calls);
    }

    [Fact]
    public void AnAskPassesTheModelAndTheEffortToClaude() =>
        Assert.Equal(
            ["-p", "--model", "opus", "--effort", "xhigh", "--tools", "", "--no-session-persistence", "--setting-sources", "", "--system-prompt", "system"],
            ClaudeModels.Arguments("opus", "xhigh", "system"));

    [Fact]
    public void ClaudeJudgeFailsClosedWhenTheRulesAreMissing()
    {
        var judge = new ClaudeJudge(Path.Combine(brain.Root, "missing.md"), new ClaudeModels(ClaudeModels.Command), brain.Home);

        var refusal = Assert.Throws<BrainException>(() => judge.Judge([JudgedEntry.Of(new NewNode("global", "a", "A", "idea", "agent", Session: null))]));

        Assert.StartsWith("the judge is unreachable: ", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeJudgeFailsClosedWhenTheCliIsMissing()
    {
        var rules = Path.Combine(brain.Root, "SKILL.md");
        File.WriteAllText(rules, Skill);
        var judge = new ClaudeJudge(rules, new ClaudeModels("brain-tests-no-such-claude"), brain.Home);

        var refusal = Assert.Throws<BrainException>(() => judge.Judge([JudgedEntry.Of(new NewNode("global", "a", "A", "idea", "agent", Session: null))]));

        Assert.Equal("the judge is unreachable: brain-tests-no-such-claude is not on PATH", refusal.Message);
    }
}
