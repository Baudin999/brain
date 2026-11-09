namespace Brain.Tests;

public sealed class KindTests : IDisposable
{
    private const string Title = "We bind every name once";
    private const string Why = "Flow looks a callee up once.";

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string ProjectNode(string slug) => File.ReadAllText(Path.Combine(brain.Cwd, "docs", "brain", "graph", slug + ".md"));

    [Fact]
    public void ANewNodeIsInformationWithNoPhase()
    {
        brain.Ok("add", "--subjects", "test", "global", "bind-once", Title, "--why", Why);

        Assert.StartsWith("---\nkind: information\nsubjects: [test]\nnamed-by: claude\n", brain.NodeText("global", "bind-once"), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Fact]
    public void AnIdeaNeedsNoWhy()
    {
        brain.Ok("add", "--subjects", "test", "global", "rows", "Rows could carry their own layout", "--kind", "idea");

        Assert.StartsWith("---\nkind: idea\nsubjects: [test]\n", brain.NodeText("global", "rows"), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Theory]
    [InlineData("--kind", "decision", "kind decision is not one of information, idea")]
    [InlineData("--kind", "information", "information needs a why")]
    public void AFreeRuleRefusesWithoutCallingTheJudge(string option, string value, string reason)
    {
        var (code, _, errors) = brain.Run("add", "--subjects", "test", "global", "entry", Title, option, value);

        Assert.Equal(1, code);
        Assert.StartsWith($"brain: refused global:entry: {reason}", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Judge.Calls);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "entry.md")));
    }

    [Fact]
    public void ThePhaseOptionIsGone()
    {
        var (code, _, errors) = brain.Run("add", "--subjects", "test", "global", "entry", Title, "--why", Why, "--phase", "concept");

        Assert.Equal(1, code);
        Assert.Contains("unknown option or missing value: --phase", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void InformationWithItsWhyReachesTheJudge()
    {
        brain.Ok("add", "--subjects", "test", "global", "bind-once", Title, "--why", Why, "--body", "Every name binds in one pass.");

        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal(("information", Title, "Every name binds in one pass.", Why), (entry.Kind, entry.Title, entry.Body, entry.Why));
    }

    [Fact]
    public void AddReadsAWholeDocumentFromStdin()
    {
        const string document = "---\nsubjects: [test]\n---\n# We bind every name once\n\nOne pass binds every name.\n\nA second pass would find nothing new.\n\n## Why\nFlow looks a callee up once.\n";

        var (code, output, errors) = brain.RunWithInput(document, "add", "global", "bind-once", "--named-by", "user");

        Assert.True(code == 0, errors);
        Assert.Equal("added global:bind-once\n", output);
        Assert.EndsWith(
            "named-by: user\nborn: 2026-10-03\n---\n# We bind every name once\n\nOne pass binds every name.\n\nA second pass would find nothing new.\n\n## Why\nFlow looks a callee up once.\n\n## Edges\n",
            brain.NodeText("global", "bind-once"),
            StringComparison.Ordinal);
        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal("One pass binds every name.\n\nA second pass would find nothing new.", entry.Body);
    }

    [Fact]
    public void AddReadsADocumentFromAFile()
    {
        File.WriteAllText(Path.Combine(brain.Root, "idea.md"), "---\nsubjects: [test]\n---\nRows could carry their own layout\n\nWe talked it through and kept columns.\n");

        brain.Ok("add", "global", "rows", "--kind", "idea", "--file", "idea.md");

        Assert.Contains("# Rows could carry their own layout\n\nWe talked it through and kept columns.\n\n## Edges\n", brain.NodeText("global", "rows"), StringComparison.Ordinal);
    }

    [Fact]
    public void AddRefusesATitleArgumentWithAFileAndBodyOptionsWithADocument()
    {
        var both = brain.Run("add", "--subjects", "test", "global", "a", Title, "--file", "x.md");
        var mixed = brain.RunWithInput("# A title\n", "add", "global", "b", "--why", Why);

        Assert.Equal(1, both.Code);
        Assert.Equal(1, mixed.Code);
        Assert.StartsWith("brain: usage: add ", mixed.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyDocumentIsRefused()
    {
        var (code, _, _) = brain.RunWithInput(string.Empty, "add", "global", "a");

        Assert.Equal(1, code);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "a.md")));
    }

    [Fact]
    public void ATitleThatRepeatsACommitSubjectIsRefused()
    {
        var repo = brain.MakeProject();
        TempBrain.Git(brain.Cwd, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "Bind every name once in the binder");

        var exact = brain.Run("add", "--subjects", "test", "project", "a", "bind every name once, in the binder!", "--kind", "idea");
        var holds = brain.Run("add", "--subjects", "test", "project", "b", "Bind every name once in the binder for flow", "--why", Why);
        var held = brain.Run("add", "--subjects", "test", "project", "c", "Every name once", "--kind", "idea");
        var brief = brain.Run("add", "--subjects", "test", "project", "d", "The first idea", "--kind", "idea");

        Assert.Contains("the title repeats the commit subject \"Bind every name once in the binder\"", exact.Err, StringComparison.Ordinal);
        Assert.Equal(1, holds.Code);
        Assert.Equal(1, held.Code);
        Assert.Equal(0, brief.Code);
        Assert.NotEmpty(repo);
        Assert.Single(brain.Judge.Calls);
    }

    [Fact]
    public void AWriteThatKeepsAnEchoingTitleIsNotRefused()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "a", "Ground first", "--kind", "idea");
        TempBrain.Git(brain.Cwd, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "Ground first");

        brain.Ok("update", "project:a", "--subjects", "terrain");
        var retitled = brain.Run("update", "project:a", "Ground first!");

        Assert.Contains("subjects: [terrain]\n", ProjectNode("a"), StringComparison.Ordinal);
        Assert.Contains("the title repeats the commit subject \"Ground first\"", retitled.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateTurnsAnIdeaIntoInformationOnlyWithAWhy()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "bind-once", Title, "--kind", "idea");

        var refused = brain.Run("update", "project:bind-once", "--kind", "information");
        brain.Ok("update", "project:bind-once", "--kind", "information", "--why", Why);

        Assert.Contains("information needs a why", refused.Err, StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: information\nsubjects: [test]\n", ProjectNode("bind-once"), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Fact]
    public void UpdateRefusesAnOldKindUntilItIsMigrated()
    {
        brain.WriteNode("global", "old", "kind: belief\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "An old belief");

        var refused = brain.Run("update", "global:old", "A newer belief");
        brain.Ok("update", "global:old", "--kind", "idea");

        Assert.Contains("kind belief is not one of", refused.Err, StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: idea\nsubjects: []\nnamed-by: user\n", brain.NodeText("global", "old"), StringComparison.Ordinal);
    }

    [Fact]
    public void MergeTakesAKindAndAWhyColumn()
    {
        brain.MakeProject();
        var report = $"+ project:a | {Title} | information | agent | {Why}\n+ project:b | Rows could carry their own layout | idea | agent\n+ project:a [test]\n+ project:b [test]\n";

        var (code, _, errors) = brain.RunWithInput(report, "merge");

        Assert.True(code == 0, errors);
        Assert.Contains("## Why\n" + Why + "\n", ProjectNode("a"), StringComparison.Ordinal);
        Assert.StartsWith("---\nkind: idea\n", ProjectNode("b"), StringComparison.Ordinal);
        Assert.Equal(["information", "idea"], Assert.Single(brain.Judge.Calls).Select(entry => entry.Kind));
    }

    [Fact]
    public void MergeRefusesInformationWithoutAWhyAndAppliesNothing()
    {
        brain.MakeProject();

        var (code, _, errors) = brain.RunWithInput($"+ project:a | {Title} | information | agent\n", "merge");

        Assert.Equal(1, code);
        Assert.Contains("refused project:a: information needs a why", errors, StringComparison.Ordinal);
        Assert.Empty(brain.Judge.Calls);
    }

    [Fact]
    public void LookupTagsAnIdeaAndNothingElse()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "ground-idea", "Ground ground ground idea", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "project", "ground-cover", "Ground cover", "--why", "Bare ground read as unfinished.");

        var lines = brain.Ok("lookup", "ground").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["project:ground-cover  sure  Ground cover", "project:ground-idea  sure [idea]  Ground ground ground idea"], lines);
    }

    [Fact]
    public void ACommitIsNoEdgeTarget()
    {
        var head = brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "bind-once", Title, "--why", Why);

        var (code, _, errors) = brain.Run("link", "project:bind-once", "built", "commit:" + head[..8]);

        Assert.Equal(1, code);
        Assert.Contains("a commit is not an edge target; git holds what happened", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void UnlinkRemovesACommitEdgeLeftFromTheOldModel()
    {
        brain.WriteNode("global", "old", "kind: idea\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "An old idea", "- built -> commit:b0e944e\n");

        brain.Ok("unlink", "global:old", "built", "commit:b0e944e");

        Assert.DoesNotContain("commit:", brain.NodeText("global", "old"), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsTheOldModelAndListsInformationWithoutAWhy()
    {
        brain.WriteNode("global", "old", "kind: belief\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "An old belief");
        brain.WriteNode("global", "old-call", "kind: decision\nphase: concept\nsuperseded-by: old\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "An old call", "- built -> commit:b0e944e\n");
        brain.WriteNode("global", "fact", "kind: information\nsubjects: []\nnamed-by: user\nborn: 2026-10-01\n", "A fact without its why");

        var (code, _, errors) = brain.Run("check");

        Assert.Equal(1, code);
        string[] expected =
        [
            "global:old: kind belief is not one of information, idea",
            "global:old-call: kind decision is from the old model; brain migrate moves it",
            "global:old-call: phase is from the old model; brain migrate drops it",
            "global:old-call: superseded-by is from the old model; brain migrate drops it",
            "global:old-call: commit:b0e944e: a commit is not an edge target; git holds what happened",
            "migration: 1 information nodes have no why: global:fact",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
    }
}
