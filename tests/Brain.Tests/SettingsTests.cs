using System.Text.Json.Nodes;
using Brain.Search;

namespace Brain.Tests;

/// <summary>
/// The settings: settings.json at the data repo's root. A missing file or key is the default, a change is written and
/// committed with nothing else staged, and a value a key cannot hold is refused, in the file as in a change.
/// </summary>
public sealed class SettingsTests : IDisposable
{
    private static readonly string Defaults =
        $$"""{"builder-easy":{"model":"sonnet","effort":"medium"},"builder-difficult":{"model":"opus","effort":"medium"},"validator":{"model":"opus","effort":"medium"},"readiness":{"model":"sonnet","effort":"medium"},"judge":{"model":"sonnet","effort":"medium"},"dreamer":[{"model":"haiku","effort":"medium"},{"model":"sonnet","effort":"medium"}],"deep-dream":{"model":"opus","effort":"medium"},"chat":{"model":"sonnet","effort":"medium"},"sitting":{"model":"sonnet","effort":"low"},"starfield":true,"motion":true,"glow":true,"sparks":true,"newstars":true,"fading":true,"glide":true,"atlas":true,"embedder":"{{EmbedderProfiles.Default.Name}}"}""";

    private const string Model = "a model of haiku, sonnet, opus, fable with an effort of low, medium, high, xhigh, max";

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    private string Stored => Path.Combine(brain.Home, BrainSettings.FileName);

    [Fact]
    public void AMissingFileOrKeyIsTheDefault()
    {
        var none = BrainSettings.Read(brain.Home).ToJson();
        File.WriteAllText(Stored, """{"judge":{"model":"opus","effort":"high"},"chat":{"model":"opus","effort":"max"},"later":1,"nebula":false,"twinkle":true,"drift":"x"}""");

        var some = BrainSettings.Read(brain.Home);

        Assert.Equal(Defaults, none);
        Assert.Equal(
            Defaults
                .Replace("\"judge\":{\"model\":\"sonnet\",\"effort\":\"medium\"}", "\"judge\":{\"model\":\"opus\",\"effort\":\"high\"}", StringComparison.Ordinal)
                .Replace("\"chat\":{\"model\":\"sonnet\",\"effort\":\"medium\"}", "\"chat\":{\"model\":\"opus\",\"effort\":\"max\"}", StringComparison.Ordinal),
            some.ToJson());
        Assert.Equal(("opus", "max"), some.Chat);
        Assert.Equal(("sonnet", "low"), some.Sitting);
    }

    [Fact]
    public void AFileThatStillNamesTheEffortDefaultReadsItAsMedium()
    {
        File.WriteAllText(
            Stored,
            """{"validator":{"model":"haiku","effort":"default"},"judge":{"model":"opus","effort":"default"},"dreamer":[{"model":"opus","effort":"default"},{"model":"haiku","effort":"max"}],"chat":{"model":"opus","effort":"default"}}""");

        var read = BrainSettings.Read(brain.Home);

        Assert.Equal(("opus", "medium"), read.Judge);
        Assert.Equal([("opus", "medium"), ("haiku", "max")], read.Dreamer);
        Assert.Equal(("opus", "medium"), read.Chat);
        Assert.Contains("\"validator\":{\"model\":\"haiku\",\"effort\":\"medium\"}", read.ToJson(), StringComparison.Ordinal);
        Assert.DoesNotContain("default", read.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void AChangeIsWrittenAndCommittedWithNothingElseStaged()
    {
        TempBrain.CommitAll(brain.Home);
        File.WriteAllText(Path.Combine(brain.Home, "other.txt"), "x");

        var changed = BrainSettings.Change(brain.Home, "dreamer", JsonNode.Parse("""[{"model":"opus","effort":"high"},{"model":"sonnet","effort":"low"}]"""));
        BrainSettings.Change(brain.Home, "starfield", false);
        var head = TempBrain.Git(brain.Home, "rev-parse", "HEAD");
        BrainSettings.Change(brain.Home, "starfield", false);

        Assert.Contains("\"dreamer\":[{\"model\":\"opus\",\"effort\":\"high\"},{\"model\":\"sonnet\",\"effort\":\"low\"}]", changed.ToJson(), StringComparison.Ordinal);
        Assert.Equal(head, TempBrain.Git(brain.Home, "rev-parse", "HEAD"));
        Assert.Equal("Settings: starfield is off\nsettings.json\nSettings: dreamer is opus at high and sonnet at low\nsettings.json\n", Log());
        Assert.Equal("?? other.txt\n", TempBrain.Git(brain.Home, "status", "--porcelain"));
        var file = JsonNode.Parse(File.ReadAllText(Stored))!.AsObject();
        Assert.Equal(["dreamer", "starfield"], file.Select(each => each.Key));
    }

    [Fact]
    public void AChangeRefusedByGitPutsTheFileBack()
    {
        TempBrain.CommitAll(brain.Home);
        BrainSettings.Change(brain.Home, "judge", Choice("opus", "low"));
        var before = File.ReadAllText(Stored);
        File.WriteAllText(Path.Combine(brain.Home, "other.txt"), "x");
        TempBrain.Git(brain.Home, "add", "other.txt");

        var refused = Assert.Throws<BrainException>(() => BrainSettings.Change(brain.Home, "judge", Choice("haiku", "low")));

        Assert.Contains("staged", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Stored));
    }

    [Fact]
    public void AChangeWhoseCommitFailsLeavesTheFileAndTheIndexAsTheyWere()
    {
        TempBrain.CommitAll(brain.Home);
        BrainSettings.Change(brain.Home, "judge", Choice("opus", "low"));
        var before = File.ReadAllText(Stored);
        var hook = Path.Combine(brain.Home, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
        OmarchyThemeTests.Chmod(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var refused = Assert.Throws<BrainException>(() => BrainSettings.Change(brain.Home, "judge", Choice("haiku", "low")));
        var file = File.ReadAllText(Stored);
        var staged = TempBrain.Git(brain.Home, "diff", "--cached", "--name-only");
        File.Delete(hook);
        var next = BrainSettings.Change(brain.Home, "starfield", false);

        Assert.Contains("git commit failed", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, file);
        Assert.Equal(string.Empty, staged);
        Assert.Contains("\"judge\":{\"model\":\"opus\",\"effort\":\"low\"}", next.ToJson(), StringComparison.Ordinal);
        Assert.Equal("Settings: starfield is off\n", TempBrain.Git(brain.Home, "log", "-1", "--format=%s"));
    }

    [Theory]
    [InlineData("theme", "\"dark\"", "theme is no setting")]
    [InlineData("judge", "{\"model\":\"gemma4\",\"effort\":\"medium\"}", "judge is {\"model\":\"gemma4\",\"effort\":\"medium\"}, not " + Model)]
    [InlineData("judge", "{\"model\":\"opus\",\"effort\":\"default\"}", "judge is {\"model\":\"opus\",\"effort\":\"default\"}, not " + Model)]
    [InlineData("judge", "\"opus\"", "judge is \"opus\", not " + Model)]
    [InlineData("validator", "{\"model\":\"opus\",\"effort\":\"medium\",\"thinking\":true}", "validator is {\"model\":\"opus\",\"effort\":\"medium\",\"thinking\":true}, not " + Model)]
    [InlineData("dreamer", "[{\"model\":\"opus\",\"effort\":\"medium\"}]", "dreamer is [{\"model\":\"opus\",\"effort\":\"medium\"}], not two, each " + Model)]
    [InlineData("chat", "{\"model\":\"opus\",\"effort\":\"default\"}", "chat is {\"model\":\"opus\",\"effort\":\"default\"}, not " + Model)]
    [InlineData("sitting", "{\"model\":\"opus\",\"effort\":\"huge\"}", "sitting is {\"model\":\"opus\",\"effort\":\"huge\"}, not " + Model)]
    [InlineData("motion", "\"yes\"", "motion is \"yes\", not true or false")]
    [InlineData("validator", "null", "validator is null, not " + Model)]
    public void AValueTheKeyCannotHoldIsRefusedAndNothingIsWritten(string key, string value, string problem)
    {
        TempBrain.CommitAll(brain.Home);

        var refused = Assert.Throws<BrainException>(() => BrainSettings.Change(brain.Home, key, JsonNode.Parse(value)));

        Assert.Equal(problem, refused.Message);
        Assert.False(File.Exists(Stored));
    }

    [Fact]
    public void AFileWithAValueItsKeyCannotHoldIsRefusedWhenRead()
    {
        File.WriteAllText(Stored, """{"sitting":{"model":"gpt","effort":"low"}}""");
        var wrong = Assert.Throws<BrainException>(() => BrainSettings.Read(brain.Home));
        File.WriteAllText(Stored, "[1]");
        var array = Assert.Throws<BrainException>(() => BrainSettings.Read(brain.Home));
        File.WriteAllText(Stored, "{");
        var broken = Assert.Throws<BrainException>(() => BrainSettings.Read(brain.Home));

        Assert.Contains("sitting is {\"model\":\"gpt\",\"effort\":\"low\"}, not a model of", wrong.Message, StringComparison.Ordinal);
        Assert.Contains("is not a JSON object", array.Message, StringComparison.Ordinal);
        Assert.Contains("is not JSON", broken.Message, StringComparison.Ordinal);
    }

    private static JsonObject Choice(string model, string effort) => new() { ["model"] = model, ["effort"] = effort };

    private string Log() => TempBrain.Git(brain.Home, "log", "-2", "--name-only", "--format=%s").Replace("\n\n", "\n", StringComparison.Ordinal);
}
