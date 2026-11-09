using System.Text.RegularExpressions;
using Brain.Staging;

namespace Brain.Tests;

/// <summary>
/// A staged write keeps the session that staged it, never the session a node was born in, and no project: which project
/// a write is for is the graph it lands in. A write staged when the origin kept a project still reads.
/// </summary>
public sealed partial class WriteOriginTests : IDisposable
{
    private readonly TempBrain brain = new();

    /// <summary>The cwd outside every repo, and the session s-1.</summary>
    public WriteOriginTests()
    {
        brain.Cwd = brain.Root;
        brain.Session = "s-1";
        brain.SaysYes = false;
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AWriteKeepsTheSessionThatStagedIt()
    {
        Assert.Equal(new WriteOrigin("s-1"), Staged("add", "--subjects", "test", "personal", "tea", "The user drinks tea", "--kind", "idea"));
    }

    [Fact]
    public void AWriteStagedWhenTheOriginKeptAProjectStillReads()
    {
        const string Old = "0123456789ab";
        var asked = Path.Combine(brain.Home, "cache", "pending", "asked");
        Directory.CreateDirectory(asked);
        File.WriteAllText(Path.Combine(asked, Old + ".json"), $$$"""{"Id":"{{{Old}}}","Lines":["added global:old"],"Changes":[],"Origin":{"Session":"s-0","Project":"studio"}}""");

        Assert.Equal(new WriteOrigin("s-0"), new PendingStore(brain.Home).Asked().Single(write => write.Write.Id == Old).Write.Origin);
    }

    [Fact]
    public void TheSessionOptionNamesTheSessionAndAnEditKeepsTheStagingSession()
    {
        Assert.Equal("s-2", Staged("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea", "--session", "s-2").Session);
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "global", "chai", "The user drinks chai", "--kind", "idea", "--session", "s-0");
        brain.SaysYes = false;
        brain.Session = "s-3";

        Assert.Equal("s-3", Staged("update", "chai", "The user drinks masala chai").Session);
    }

    /// <summary>Runs a command that stages a write and returns the origin its pending write keeps.</summary>
    private WriteOrigin Staged(params string[] arguments)
    {
        var output = brain.Ok(arguments);
        var id = PendingId().Match(output).Groups[1].Value;
        var store = new PendingStore(brain.Home);
        store.Ask(id);
        return store.Asked().Single(asked => asked.Write.Id == id).Write.Origin!;
    }

    [GeneratedRegex("pending ([0-9a-f]{12}): nothing is written yet")]
    private static partial Regex PendingId();
}
