namespace Brain.Tests;

public sealed class FadedTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void FadedListsTheWeakestEdgesFirstAcrossScopes()
    {
        brain.Date = TempBrain.Today.AddDays(-180);
        brain.Ok("add", "--subjects", "test", "global", "old", "An old node", "--kind", "idea");
        brain.Ok("link", "old", "see", "url:https://old.example");
        brain.Date = TempBrain.Today;
        brain.Ok("add", "--subjects", "test", "personal", "fresh", "A fresh node", "--kind", "idea");
        brain.Ok("link", "fresh", "see", "url:https://fresh.example");

        var lines = brain.Ok("faded", "1").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var all = brain.Ok("faded").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["0.25  global:old -see-> url:https://old.example"], lines);
        Assert.Equal("1.00  personal:fresh -see-> url:https://fresh.example", all[^1]);
        Assert.Equal(1, brain.Run("faded", "zero").Code);
    }
}
