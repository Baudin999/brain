using Brain.Code;

namespace Brain.Tests;

/// <summary>
/// The second flow of brain code's conversation as the server reads it: a Bash call that runs one of brain's reading
/// commands is a brain query with the nodes its result lists, any other call keeps its input, and the window's choice of
/// which kinds to draw is kept across restarts.
/// </summary>
public sealed class CodeToolCallTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Theory]
    [InlineData("brain lookup window panel", "lookup", "window panel")]
    [InlineData("brain nearest project:stylebook 2>&1 | head -5", "nearest", "project:stylebook")]
    [InlineData("cd /home/x/brain && brain walk project:stylebook", "walk", "project:stylebook")]
    [InlineData("/usr/bin/brain node \"project:brain-code\"", "node", "project:brain-code")]
    public void ABrainReadingCommandIsABrainQueryThatSaysWhatItAsked(string command, string verb, string asked)
    {
        Assert.Equal(new BrainQuery(verb, asked), BrainQuery.Of(command));
    }

    [Theory]
    [InlineData("ls -la")]
    [InlineData("brain ask \"which folder?\"")]
    [InlineData("brain update project:x \"title\"")]
    [InlineData("echo brain lookup x")]
    public void AnyOtherCommandIsNoBrainQuery(string command)
    {
        Assert.Null(BrainQuery.Of(command));
    }

    [Fact]
    public void EachReadingCommandListsItsNodesByRefAndTitle()
    {
        var lookup = "project:stylebook  sure  The look lives in one stylebook\npersonal:thinks  likely [idea]  I think in shapes\n";
        var nearest = "nearest to project:stylebook:\n  brain:window-lit-html  The window is ES modules  (brain)\n";
        var walk = "project:stylebook  The look\n  11.80  project:workspace-shared-stylebook refines -> project:stylebook  Shared\n  7.83  project:brain-workspace respects -> project:other  An IDE\n";
        var node = "project:stylebook  The look lives in one stylebook\nkind: information\n  4.00  respects -> x\n";

        Assert.Equal([("project:stylebook", "The look lives in one stylebook"), ("personal:thinks", "I think in shapes")], Items("lookup", "stylebook", lookup));
        Assert.Equal([("brain:window-lit-html", "The window is ES modules")], Items("nearest", "project:stylebook", nearest));
        Assert.Equal([("project:workspace-shared-stylebook", "Shared"), ("project:other", "An IDE")], Items("walk", "project:stylebook", walk));
        Assert.Equal([("project:stylebook", "The look lives in one stylebook")], Items("node", "project:stylebook", node));
    }

    /// <summary>The parser reads the CLI's own output, so a change to a command's format fails here and not in the window.</summary>
    [Fact]
    public void TheRealCommandsPrintNodesTheParserFinds()
    {
        brain.Embedder.Meanings["tea"] = 0;
        brain.Embedder.Meanings["chai"] = 0;
        brain.Ok("add", "--subjects", "drinks", "personal", "tea", "The user drinks tea", "--kind", "idea", "--body", "Green tea in the morning.");
        brain.Ok("add", "--subjects", "drinks", "personal", "chai", "The user drinks chai", "--kind", "idea");
        brain.Ok("link", "personal:chai", "relates-to", "personal:tea");

        var lookup = new BrainQuery("lookup", "tea").Items(brain.Ok("lookup", "tea"));
        var nearest = new BrainQuery("nearest", "personal:tea").Items(brain.Ok("nearest", "personal:tea"));
        var walk = new BrainQuery("walk", "personal:tea").Items(brain.Ok("walk", "personal:tea"));
        var node = new BrainQuery("node", "personal:tea").Items(brain.Ok("node", "personal:tea"));

        Assert.Contains(new BrainItem("personal:tea", "The user drinks tea"), lookup);
        Assert.Equal([new BrainItem("personal:chai", "The user drinks chai")], nearest);
        Assert.Equal([new BrainItem("personal:chai", "The user drinks chai")], walk);
        Assert.Equal([new BrainItem("personal:tea", "The user drinks tea")], node);
    }

    [Fact]
    public void ATranscriptKeepsACallsInputAndGivesABrainQueryItsItems()
    {
        var path = Path.Combine(brain.Root, "t.jsonl");
        var call = """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"brain lookup stylebook"}},{"type":"tool_use","id":"t2","name":"Edit","input":{"file_path":"a.cs","old_string":"x","new_string":"y"}}]}}""";
        var result = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"project:stylebook  sure  The look\n"},{"type":"tool_result","tool_use_id":"t2","content":"edited"}]}}""";
        File.WriteAllText(path, call + "\n" + result + "\n");

        var events = new Transcript(path).All();

        Assert.Equal(("lookup", "stylebook"), (events[0].Brain, events[0].Subject));
        Assert.Equal(new Dictionary<string, string> { ["file_path"] = "a.cs", ["old_string"] = "x", ["new_string"] = "y" }, events[1].Input);
        Assert.Null(events[1].Brain);
        Assert.Equal([new BrainItem("project:stylebook", "The look")], events[2].Items);
        Assert.Null(events[3].Items);
    }

    [Fact]
    public void TheWindowsChoiceOfKindsStartsOnAndIsKeptAcrossRestarts()
    {
        var first = new CodeSettings(brain.Home);
        Assert.Equal(new CodeSettingsView(true, true), first.Read());

        first.Save(new CodeSettingsView(Tools: false, Brain: true));

        Assert.Equal(new CodeSettingsView(false, true), new CodeSettings(brain.Home).Read());
    }

    private static IEnumerable<(string Ref, string Title)> Items(string verb, string asked, string output) =>
        new BrainQuery(verb, asked).Items(output).Select((item) => (item.Ref, item.Title));
}
