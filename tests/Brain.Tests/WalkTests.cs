namespace Brain.Tests;

public sealed class WalkTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void WalkShowsNeighbourLabelsAndLogsNothing()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "wg-12", "The ground phase", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "wg-1", "The first phase", "--kind", "idea");
        brain.Ok("link", "meadow", "part-of", "wg-12");
        brain.Ok("link", "wg-12", "after", "wg-1");
        brain.Ok("link", "meadow", "defined-in", "file:docs/ground.md");

        var one = brain.Ok("walk", "meadow").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var two = brain.Ok("walk", "meadow", "--hops", "2").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["global:meadow  Ground cover", "  1.00  global:meadow part-of -> global:wg-12  The ground phase", "  1.00  global:meadow defined-in -> file:docs/ground.md"], one);
        Assert.Equal("    1.00  global:wg-12 after -> global:wg-1  The first phase", two[3]);
        Assert.False(File.Exists(brain.LogPath));
    }

    [Fact]
    public void WalkReachesTheNodesThatPointAtIt()
    {
        brain.Ok("add", "--subjects", "test", "global", "door", "The door", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "yes", "The yes", "--kind", "idea");
        brain.Ok("link", "yes", "refines", "door");

        var lines = brain.Ok("walk", "door").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["global:door  The door", "  1.00  global:yes refines -> global:door  The yes"], lines);
    }

    [Fact]
    public void WalkShowsAnEdgeBothWaysOncePerEdge()
    {
        brain.Ok("add", "--subjects", "test", "global", "left", "The left", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "right", "The right", "--kind", "idea");
        brain.Ok("link", "left", "before", "right");
        brain.Ok("link", "right", "after", "left");

        var lines = brain.Ok("walk", "left", "--hops", "2").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Single(lines, line => line.Contains("left before -> global:right"));
        Assert.Single(lines, line => line.Contains("right after -> global:left"));
    }

    [Fact]
    public void WalkCutsAtTheBudgetAndSaysHowManyItLeftOut()
    {
        brain.Ok("add", "--subjects", "test", "global", "hub", "The hub", "--kind", "idea");
        var label = string.Join(' ', Enumerable.Repeat("longword", 25));
        for (var i = 0; i < 40; i++)
        {
            brain.Ok("add", "--subjects", "test", "global", $"spoke-{i}", label, "--kind", "idea");
            brain.Ok("link", "hub", "has", $"spoke-{i}");
        }

        var output = brain.Ok("walk", "hub");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var kept = lines.Length - 2;
        Assert.InRange(kept, 10, 39);
        Assert.Equal($"... {40 - kept} more left out", lines[^1]);
        Assert.True(output.Length - lines[^1].Length <= 4001);
    }

    [Fact]
    public void WalkRefusesMoreThanTwoHops() => Assert.Equal(1, brain.Run("walk", "x", "--hops", "3").Code);
}
