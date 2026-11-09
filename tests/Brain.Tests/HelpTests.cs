using Xunit;

namespace Brain.Tests;

public sealed class HelpTests
{
    private const int Width = 100;

    private static IEnumerable<string> AllCommands => Cli.Commands.Keys;

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void OverviewListsEveryCommandWithinTheWidth(string flag)
    {
        using var brain = new TempBrain();
        var result = brain.Run(flag);
        Assert.Equal(0, result.Item1);
        Assert.All(result.Item2.Split('\n'), line => Assert.True(line.Length <= Width, line));
        Assert.All(AllCommands, name => Assert.Contains($"  {name}", result.Item2, StringComparison.Ordinal));
        Assert.Contains("--project <name|path>", result.Item2, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCommandHasHelpWithinTheWidth()
    {
        using var brain = new TempBrain();
        foreach (var name in AllCommands)
        {
            var result = brain.Run(name, "--help");
            Assert.Equal(0, result.Item1);
            Assert.StartsWith("usage: brain " + name, result.Item2, StringComparison.Ordinal);
            Assert.All(result.Item2.Split('\n'), line => Assert.True(line.Length <= Width, line));
        }
    }

    [Fact]
    public void AnUnknownCommandPrintsAShortErrorAndTheHint()
    {
        using var brain = new TempBrain();
        var result = brain.Run("frobnicate");
        Assert.Equal(2, result.Item1);
        Assert.Equal("brain: unknown command: frobnicate\nrun brain --help for the commands", result.Item3.Trim().Replace("\r", string.Empty));
    }

    [Fact]
    public void AWrongArgumentPrintsTheUsageAndTheCommandHint()
    {
        using var brain = new TempBrain();
        var result = brain.Run("link", "only-one");
        Assert.Equal(1, result.Item1);
        Assert.Contains("usage: link <from-ref> <rel> <to-ref>", result.Item3, StringComparison.Ordinal);
        Assert.Contains("run brain link --help", result.Item3, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryCommandHasOneEntryAndOneGroup()
    {
        Assert.Equal(Cli.Commands.Keys.Order(StringComparer.Ordinal), Help.Entries.Select(entry => entry.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Cli.Commands.Keys.Order(StringComparer.Ordinal), Help.Groups.SelectMany(group => group.Names).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnknownHelpTopicIsAnUnknownCommand()
    {
        using var brain = new TempBrain();
        var result = brain.Run("help", "frob");
        Assert.Equal(2, result.Item1);
        Assert.StartsWith("brain: unknown command: frob", result.Item3, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrappedUsageNeverBreaksInsideABracket()
    {
        using var brain = new TempBrain();
        var lines = brain.Run("add", "--help").Item2.Split('\n').TakeWhile(line => line.Length > 0);
        Assert.All(lines, line => Assert.Equal(line.Count(c => c == '['), line.Count(c => c == ']')));
    }

    [Fact]
    public void TheCliDocHoldsTheOverviewVerbatim()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "README.md")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        Assert.Contains(Help.Overview().Replace("\r", string.Empty), File.ReadAllText(Path.Combine(root, "docs", "cli.md")), StringComparison.Ordinal);
    }
}
