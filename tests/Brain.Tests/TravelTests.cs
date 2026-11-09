namespace Brain.Tests;

public sealed class TravelTests : IDisposable
{
    private readonly TempBrain brain = new();

    public TravelTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "wg-12", "The ground phase", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "global", "wg-1", "The first phase", "--kind", "idea");
        brain.Ok("link", "meadow", "part-of", "wg-12");
        brain.Ok("link", "meadow", "after", "wg-1");
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void OpeningBothEndsInOneSessionTravelsTheEdgeOnce()
    {
        brain.Session = "s1";
        brain.Ok("node", "meadow");
        brain.Ok("node", "wg-12");
        brain.Ok("node", "wg-12");
        brain.Ok("node", "meadow");

        Assert.Equal(["from\trel\tto\tweight\tasof", "global:meadow\tpart-of\twg-12\t1\t2026-10-03"], File.ReadAllLines(brain.LogPath));
        Assert.Contains("  2.00  part-of -> wg-12", brain.Ok("node", "meadow"), StringComparison.Ordinal);
        Assert.Contains("  1.00  after -> wg-1", brain.Ok("node", "meadow"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEdgeTravelsWhicheverEndIsOpenedFirstAndOnceMoreInAnotherSession()
    {
        brain.Session = "s1";
        brain.Ok("node", "wg-12");
        brain.Ok("node", "meadow");
        brain.Session = "s2";
        brain.Date = TempBrain.Today.AddDays(90);
        brain.Ok("node", "wg-12");
        brain.Ok("node", "meadow");

        Assert.Equal("global:meadow\tpart-of\twg-12\t1.5\t2027-01-01", File.ReadAllLines(brain.LogPath)[1]);
    }

    [Fact]
    public void NoSessionWalkAndLookupRecordNothing()
    {
        brain.Ok("node", "meadow");
        brain.Ok("node", "wg-12");
        brain.Session = "s1";
        brain.Ok("walk", "meadow", "--hops", "2");
        brain.Ok("lookup", "ground");
        brain.Ok("node", "wg-1");

        Assert.False(File.Exists(brain.LogPath));
    }

    [Fact]
    public void FoldKeepsEveryWeightAndLeavesOneLinePerEdge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(brain.LogPath)!);
        File.WriteAllLines(brain.LogPath, [
            "date\tfrom\trel\tto",
            "2026-07-05\tglobal:meadow\tpart-of\twg-12",
            "2026-09-01\tglobal:meadow\tpart-of\twg-12",
            "2026-10-01\tglobal:meadow\tpart-of\twg-12",
            "2026-10-01\tglobal:meadow\tafter\twg-1",
        ]);
        var before = brain.Ok("faded");

        Assert.Contains("5 lines -> 3 lines", brain.Ok("fold"), StringComparison.Ordinal);
        Assert.Equal(before, brain.Ok("faded"));
        Assert.Equal(EdgeLog.Header, File.ReadAllLines(brain.LogPath)[0]);
        Assert.Contains("3.27  global:meadow -part-of-> wg-12", before, StringComparison.Ordinal);
    }

    [Fact]
    public void AProjectEdgeTravelsInTheBrainsCacheAndLeavesTheRepoClean()
    {
        brain.MakeProject();
        brain.Ok("add", "--subjects", "test", "project", "timeline", "The timeline", "--kind", "idea");
        brain.Ok("add", "--subjects", "test", "project", "beats", "The beats", "--kind", "idea");
        brain.Ok("link", "project:timeline", "holds", "beats");
        TempBrain.CommitAll(brain.Cwd);
        brain.Session = "s1";

        brain.Ok("node", "project:timeline");
        brain.Ok("node", "project:beats");

        Assert.Equal(string.Empty, TempBrain.Git(brain.Cwd, "status", "--porcelain"));
        var log = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(brain.LogPath)!, "repo-*.tsv"));
        Assert.Equal("project:timeline\tholds\tbeats\t1\t2026-10-03", File.ReadAllLines(log)[1]);
        Assert.Contains("  2.00  holds -> beats", brain.Ok("node", "project:timeline"), StringComparison.Ordinal);
    }

    [Fact]
    public void AWorktreeSharesItsCheckoutsEdgeLog()
    {
        brain.MakeProject();
        var worktree = Path.Combine(brain.Root, "worktree");
        TempBrain.Git(brain.Cwd, "worktree", "add", "-q", worktree, "-b", "side");

        var checkout = ScopeResolver.ForRepo(brain.Home, ScopeNames.Project, brain.Cwd);
        var linked = ScopeResolver.ForRepo(brain.Home, ScopeNames.Project, worktree);

        Assert.Equal(checkout.LogPath, linked.LogPath);
    }

    [Fact]
    public void ATempFileAnotherWriterHoldsDoesNotBlockATravel()
    {
        Directory.CreateDirectory(brain.LogPath + ".tmp");
        brain.Session = "s1";
        brain.Ok("node", "meadow");
        brain.Ok("node", "wg-12");

        Assert.Equal("global:meadow\tpart-of\twg-12\t1\t2026-10-03", File.ReadAllLines(brain.LogPath)[1]);
        Assert.Equal([brain.LogPath], Directory.GetFiles(Path.GetDirectoryName(brain.LogPath)!));
    }
}
