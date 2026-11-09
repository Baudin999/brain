namespace Brain.Tests;

public sealed class MergeTests : IDisposable
{
    private const string Report = """
        Worker report. Prose lines are ignored.
        nodes:
        + project:phase-3 | The third phase | idea | agent
        + project:phase-2 | The second phase | idea | agent
        + project:phase-3 [test]
        edges:
        + project:phase-3 -after-> project:phase-2
        """;

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void MergeAppliesNodesThenEdgesAndSkipsExistingNodes()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "phase-2", "Already here", "--kind", "idea");

        var (code, output, errors) = brain.RunWithInput(Report, "merge");

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, errors);
        Assert.Equal(
            [
                "added project:phase-3",
                "skipped project:phase-2: it exists",
                "subjects project:phase-3 [test]",
                "linked project:phase-3 -after-> project:phase-2",
                "merge: 4 applied, 0 failed",
            ],
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("  1.00  after -> phase-2\n", brain.Ok("node", "project:phase-3"), StringComparison.Ordinal);
        brain.Ok("check");
    }

    [Fact]
    public void MergeReadsTheWhyColumnWholeAndLeavesTheWhyOptional()
    {
        const string report = "+ global:a | First | information | agent | Users asked; a | b stays one why.\n"
            + "+ global:b | Second | idea | agent | \n+ global:c | Third | idea | agent\n+ global:a [test]\n+ global:b [test]\n+ global:c [test]\n";

        var (code, _, errors) = brain.RunWithInput(report, "merge");

        Assert.True(code == 0, errors);
        var judged = Assert.Single(brain.Judge.Calls);
        Assert.Equal(
            [("information", "Users asked; a | b stays one why."), ("idea", string.Empty), ("idea", string.Empty)],
            judged.Select(entry => (entry.Kind, entry.Why)));
        Assert.Contains("## Why\nUsers asked; a | b stays one why.\n", brain.NodeText("global", "a"), StringComparison.Ordinal);
    }

    [Fact]
    public void MergeReportsUnreadableAndFailingLinesAndExitsOne()
    {
        const string report = "+ nonsense\n+ global:x | Label | idea | robot\n+ global:nobody -rel-> global:nothing\n";

        var (code, _, errors) = brain.RunWithInput(report, "merge");

        Assert.Equal(1, code);
        Assert.Equal(
            ["line 1: cannot read + nonsense", "line 2: named-by must be user, claude, agent", "line 3: no node global:nobody"],
            errors.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void MergeWithOneFailingLineAppliesNothing()
    {
        const string report = "+ global:a | First | idea | agent\n+ global:b | Second | idea | agent\n+ global:a -rel-> global:b\n+ global:a -rel-> global:nothing\n";

        var (code, output, errors) = brain.RunWithInput(report, "merge");

        Assert.Equal(1, code);
        Assert.Equal("line 4: no node global:nothing\n", errors);
        Assert.Equal("merge: 1 failed, nothing applied\n", output);
        Assert.Empty(Directory.GetFiles(Path.Combine(brain.Home, "graph", "global")));
        Assert.False(File.Exists(brain.LogPath));
        Assert.Empty(brain.Judge.Calls);
    }
}
