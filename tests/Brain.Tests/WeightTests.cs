namespace Brain.Tests;

public sealed class WeightTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(90, 0.5)]
    [InlineData(180, 0.25)]
    public void ATraversalHalvesEveryNinetyDays(int daysAgo, double expected)
    {
        var today = TempBrain.Today;

        Assert.Equal(expected, Decay.Of(today.AddDays(-daysAgo), today), precision: 9);
    }

    [Fact]
    public void BornIsOneImplicitTraversalAndUserNamedNodesCountDouble()
    {
        brain.Date = TempBrain.Today.AddDays(-90);
        brain.Ok("add", "--subjects", "test", "global", "theirs", "Named by the user", "--named-by", "user", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "mine", "Named by claude", "--kind", "idea");
        File.WriteAllText(
            Path.Combine(brain.Home, "graph", "global", "theirs.md"),
            brain.NodeText("global", "theirs") + "- see -> url:https://example.org\n");
        File.AppendAllText(
            Path.Combine(brain.Home, "graph", "global", "mine.md"),
            "- see -> url:https://example.org\n");
        brain.Date = TempBrain.Today;

        Assert.Contains("  1.00  see -> url:https://example.org", brain.Ok("node", "theirs"), StringComparison.Ordinal);
        Assert.Contains("  0.50  see -> url:https://example.org", brain.Ok("node", "mine"), StringComparison.Ordinal);
    }
}
