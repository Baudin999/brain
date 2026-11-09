namespace Brain.Tests;

/// <summary>
/// brain edit: a node's title, body, why, subjects and edges as markdown from stdin or a file, through the free rules
/// and the judge.
/// </summary>
public sealed class EditTests : IDisposable
{
    private readonly TempBrain brain = new();

    public EditTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea", "--named-by", "user");
        brain.Ok("add", "--subjects", "test", "global", "ground", "The ground", "--kind", "idea");
        brain.Ok("link", "meadow", "part-of", "ground");
        brain.Judge.Calls.Clear();
    }

    public void Dispose() => brain.Dispose();

    private const string Front = "---\nsubjects: [test]\n---\n";

    [Fact]
    public void EditRewritesTitleBodyWhySubjectsAndEdgesAndKeepsTheFields()
    {
        var (code, output, errors) = brain.RunWithInput(
            "---\r\nsubjects: [cover]\r\n---\r\n# Fine ground cover\r\n\r\nGrass first.\r\n\r\nThen moss.\r\n\r\n## Why\r\nBare ground read as unfinished.\r\n\r\n"
            + "## Edges\r\n- respects -> global:ground\r\n", "edit", "meadow");

        Assert.True(code == 0, errors);
        Assert.Equal("edited global:meadow\n", output);
        Assert.Equal(
            "---\nkind: idea\nsubjects: [cover]\nnamed-by: user\nborn: 2026-10-03\n---\n# Fine ground cover\n\nGrass first.\n\nThen moss.\n\n"
            + "## Why\nBare ground read as unfinished.\n\n## Edges\n- respects -> ground\n",
            brain.NodeText("global", "meadow"));
        var entry = Assert.Single(Assert.Single(brain.Judge.Calls));
        Assert.Equal(("Fine ground cover", "Grass first.\n\nThen moss.", "Bare ground read as unfinished."), (entry.Title, entry.Body, entry.Why));
        Assert.Equal(["cover"], entry.Subjects);
        Assert.Equal([new Edge("respects", "ground")], entry.Edges);
    }

    [Fact]
    public void AnEdgeTheEditDoesNotCarryGoesAndShowsInTheDiff()
    {
        brain.SaysYes = false;

        var output = brain.RunWithInput(Front + "# Ground cover\n", "edit", "meadow").Out;

        var id = System.Text.RegularExpressions.Regex.Match(output, "pending ([0-9a-f]{12})").Groups[1].Value;
        var store = new Brain.Staging.PendingStore(brain.Home);
        store.Ask(id);
        var change = Assert.Single(store.Asked().Single(asked => asked.Write.Id == id).Write.Changes);
        Assert.Contains("- part-of -> ground\n", change.Before, StringComparison.Ordinal);
        Assert.EndsWith("## Edges\n", change.After, StringComparison.Ordinal);
        Assert.Contains("- part-of -> ground\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteWithoutASubjectOrWithABadEdgeIsRefusedBeforeTheJudge()
    {
        var before = brain.NodeText("global", "meadow");

        var bare = brain.RunWithInput("# Ground cover\n", "edit", "meadow");
        var dangling = brain.RunWithInput(Front + "# Ground cover\n\n## Edges\n- part-of -> nowhere\n", "edit", "meadow");
        var foreign = brain.RunWithInput("---\nkind: idea\nsubjects: [test]\n---\n# Ground cover\n", "edit", "meadow");

        Assert.Equal("brain: refused global:meadow: a node needs at least one subject, a keyword a search finds it by. Run brain subjects global to reuse a word already in use\n", bare.Err);
        Assert.Equal("brain: no node nowhere\n", dangling.Err);
        Assert.StartsWith("brain: the frontmatter of a write holds only subjects, not kind", foreign.Err, StringComparison.Ordinal);
        Assert.Empty(brain.Judge.Calls);
        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void EditReadsAFileWithAPlainTitleLineAndEmptyPartsDropOut()
    {
        Assert.Equal(0, brain.RunWithInput(Front + "Ground cover\nOld body\n## Why\nOld why", "edit", "meadow").Code);
        var file = Path.Combine(brain.Root, "content.md");
        File.WriteAllText(file, Front + "Ground cover, lit\n");

        Assert.Equal("edited global:meadow\n", brain.Ok("edit", "meadow", "--file", file));

        var text = brain.NodeText("global", "meadow");
        Assert.DoesNotContain("## Why", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Old body", text, StringComparison.Ordinal);
        Assert.Equal(("", ""), (brain.Judge.Calls[^1][0].Body, brain.Judge.Calls[^1][0].Why));
    }

    [Fact]
    public void UnchangedContentWritesNothingAndSkipsTheJudge()
    {
        var before = brain.NodeText("global", "meadow");

        var (code, output, _) = brain.RunWithInput(Front + "#  Ground cover  \n\n## Edges\n- part-of -> global:ground\n", "edit", "meadow");

        Assert.Equal(0, code);
        Assert.Equal("unchanged global:meadow\n", output);
        Assert.Empty(brain.Judge.Calls);
        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void FreeRulesAndTheJudgeRefuseBeforeAnythingIsWritten()
    {
        brain.Ok("update", "meadow", "Ground cover first", "--why", "The eye reads the ground before the sky.", "--kind", "information");
        var before = brain.NodeText("global", "meadow");
        brain.Judge.Calls.Clear();

        var noWhy = brain.RunWithInput(Front + "Ground cover first\n\nThe body stays.\n", "edit", "meadow");
        brain.Judge.Refusal = "the body is a work log";
        var judged = brain.RunWithInput(Front + "Ground cover first\nDid it today.\n## Why\nThe eye reads the ground first.", "edit", "meadow");

        Assert.Equal(1, noWhy.Code);
        Assert.StartsWith("brain: refused global:meadow: information needs a why", noWhy.Err, StringComparison.Ordinal);
        Assert.Equal(1, judged.Code);
        Assert.Equal("brain: the judge refused: the body is a work log\n", judged.Err);
        Assert.Single(brain.Judge.Calls);
        Assert.Equal(before, brain.NodeText("global", "meadow"));
    }

    [Fact]
    public void EditRefusesAnEmptyOrLongTitleASectionInTheBodyAMissingNodeAndBadArguments()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("word", 26));

        Assert.Equal("brain: title is empty\n", brain.RunWithInput("\nBody only", "edit", "meadow").Err);
        Assert.Equal(1, brain.RunWithInput(longTitle, "edit", "meadow").Code);
        Assert.Equal(1, brain.RunWithInput("Title\n\nBody\n## Edges\nnot an edge", "edit", "meadow").Code);
        Assert.Equal(1, brain.RunWithInput("Title", "edit", "nowhere").Code);
        Assert.Equal(1, brain.RunWithInput("Title", "edit").Code);
        Assert.Equal(1, brain.Run("edit", "meadow", "--file", Path.Combine(brain.Root, "missing.txt")).Code);
        Assert.Empty(brain.Judge.Calls);
        Assert.Contains("# Ground cover\n", brain.NodeText("global", "meadow"), StringComparison.Ordinal);
    }
}
