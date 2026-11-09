namespace Brain.Tests;

/// <summary>
/// Subjects as free search keywords: setting a node's subjects without the judge, listing the ones in use, and every
/// reader.
/// </summary>
public sealed class SubjectsTests : IDisposable
{
    private readonly TempBrain brain = new();

    public SubjectsTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "judge", "Haiku judges the meaning of every entry", "--kind", "idea");
        brain.Judge.Calls.Clear();
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void UpdateSetsAnyWellFormedSubjectsWithoutTheJudgeAndClearsThem()
    {
        brain.Ok("update", "meadow", "--subjects", "roads, judging");

        Assert.Contains("subjects: [roads, judging]\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
        Assert.Empty(brain.Judge.Calls);
        Assert.Contains("subjects: [roads, judging]", brain.Ok("node", "meadow"), StringComparison.Ordinal);

        brain.Ok("update", "meadow", "--subjects", "");
        Assert.Contains("subjects: []\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRefusesASubjectThatIsNotLowercaseHyphenated()
    {
        var before = brain.NodeText("global", "meadow");

        var (code, _, errors) = brain.Run("update", "meadow", "--subjects", "roads,Big Roads");

        Assert.Equal(1, code);
        Assert.Contains("subject Big Roads is not lowercase-hyphenated", errors, StringComparison.Ordinal);
        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void UpdateWithProseAndSubjectsMeetsTheJudge()
    {
        brain.Ok("update", "meadow", "Fine ground cover", "--subjects", "roads");

        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal(["roads"], entry.Subjects);
    }

    [Fact]
    public void ListCountsTheNodesOfEachSubjectInUseTheMostUsedFirst()
    {
        brain.Ok("update", "meadow", "--subjects", "roads");
        brain.Ok("update", "judge", "--subjects", "roads,judging");

        var listed = brain.Ok("subjects", "global");

        Assert.Equal("global: 2 subjects in use\n  roads      2\n  judging    1\n", listed);
        Assert.Contains("personal: 0 subjects in use\n", brain.Ok("subjects"), StringComparison.Ordinal);
    }

    [Fact]
    public void ListReadsNoSubjectsFileAndRefusesAnAdd()
    {
        brain.MakeProject();
        File.WriteAllText(Path.Combine(brain.Cwd, "docs", "brain", "subjects.md"), "- weather: The sky\nnot a subject line\n");
        Directory.CreateDirectory(Path.Combine(brain.Home, "subjects"));
        File.WriteAllText(Path.Combine(brain.Home, "subjects", "global.md"), "- weather: The sky\nnot a subject line\n");

        Assert.DoesNotContain("weather", brain.Ok("subjects"), StringComparison.Ordinal);
        Assert.Equal(1, brain.Run("subjects", "add", "global", "weather", "The sky").Code);
        brain.Ok("check");
    }

    [Fact]
    public void MergeSetsSubjectsOnNewAndExistingNodes()
    {
        const string report = "+ global:fence | Fences mark the edge of the world | idea | agent\n+ global:fence [roads]\n+ global:meadow [judging, roads]\n";

        Assert.Contains("subjects global:meadow [judging, roads]", brain.RunWithInput(report, "merge").Out, StringComparison.Ordinal);

        Assert.Contains("subjects: [roads]\n", brain.NodeText("global", "fence"), StringComparison.Ordinal);
        Assert.Contains("subjects: [judging, roads]\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
        Assert.Equal(["global:fence"], Assert.Single(brain.Judge.Calls).Select(entry => entry.Ref));
    }

    [Fact]
    public void LookupFiltersBySubject()
    {
        brain.Ok("update", "judge", "--subjects", "judging");

        Assert.Equal("global:judge [idea]  Haiku judges the meaning of every entry\n", brain.Ok("lookup", "--subject", "judging"));
        Assert.Equal("no node is sure or likely\n", brain.Ok("lookup", "ground", "--subject", "judging"));
        Assert.Contains("global:meadow", brain.Ok("lookup", "ground"), StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectListsTheSubjectsItsNodesCarry()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "wg-1", "Ground first", "--kind", "idea");
        brain.Ok("update", "project:wg-1", "--subjects", "terrain");

        Assert.Equal("project: 1 subject in use\n  terrain    1\n", brain.Ok("subjects", "project"));
    }
}
