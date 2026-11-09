using System.Diagnostics;
using System.Globalization;
using Brain.Children;

namespace Brain.Tests;

/// <summary>
/// What a keeper ends: what is below it now, what it once saw below it that still runs as that same process, and what
/// kept its process group. The second and third are how it gets as close as macOS allows, with no subreaper; Linux
/// proves them on its own processes, whose orphans this test process does not reap.
/// </summary>
public sealed class LineageTests
{
    private const int Keeper = 100;

    [Fact]
    public void AKeeperEndsWhatIsBelowItWhatItSawAndWhatKeptItsGroup()
    {
        var lineage = new Lineage(Keeper);
        lineage.Remember(Tree(
            (101, Keeper, Keeper, "a"),
            (105, 101, 105, "e"),
            (106, 101, 106, "f")));

        var left = lineage.Left(Tree(
            (Keeper, 1, Keeper, "keeper"),
            (101, Keeper, Keeper, "a"),
            (102, 101, 102, "b"),
            (103, 1, Keeper, "c"),
            (104, 1, 104, "d"),
            (105, 1, 105, "e"),
            (106, 1, 106, "a later process"),
            (107, Keeper, Keeper, "zombie")));

        Assert.Equal([101, 102, 105, 103], left);
    }

    /// <summary>An orphan whose parent ended moved out from below; the keeper still ends it once it saw it, and not once it is gone.</summary>
    [Fact]
    public void AnOrphanTheKeeperSawStaysItsToEnd()
    {
        var root = Path.Combine(Path.GetTempPath(), "brain-lineage-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        var pidFile = Path.Combine(root, "orphan");
        var go = Path.Combine(root, "go");
        using var parent = Process.Start(new ProcessStartInfo("/bin/sh", ["-c", $"sleep 300 </dev/null >/dev/null 2>&1 & echo $! > '{pidFile}'; while [ ! -e '{go}' ]; do sleep 0.05; done"]) { UseShellExecute = false })!;
        try
        {
            Assert.True(SpinWait.SpinUntil(() => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0, TimeSpan.FromSeconds(5)));
            var orphan = int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture);
            var lineage = new Lineage(Environment.ProcessId);
            var before = ProcessTree.Read();
            lineage.Remember(before);

            File.WriteAllText(go, string.Empty);
            parent.WaitForExit();
            Assert.True(SpinWait.SpinUntil(() => !ProcessTree.Read().Below(Environment.ProcessId).Contains(orphan), TimeSpan.FromSeconds(5)));
            var moved = ProcessTree.Read();
            Posix.Signal(orphan, Posix.SigKill);
            var ended = ReviewServerTests.Gone(orphan);

            Assert.Contains(orphan, before.Below(Environment.ProcessId));
            Assert.DoesNotContain(orphan, moved.Below(Environment.ProcessId));
            Assert.Contains(orphan, lineage.Left(moved));
            Assert.True(ended);
            Assert.DoesNotContain(orphan, lineage.Left(ProcessTree.Read()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// On macOS ps lists by pid, which wraps, so a keeper's own read of the table can have a lower pid than claude. The
    /// tree orders by the start ps prints, so claude is still the keeper's oldest child and its build is what it runs.
    /// </summary>
    [Fact]
    public void ClaudeIsTheKeepersOldestChildThoughItsPidIsHigher()
    {
        const string Said = """
              500     1   500 S    Mon Nov  2 18:43:40 2026
            99990   500   500 S    Mon Nov  2 18:43:41 2026
               12   500   500 R    Mon Nov  2 18:43:43 2026
            99995 99990 99995 S    Mon Nov  2 18:43:45 2026
               13   500   500 R    Mon Nov  2 18:43:46 2026
            """;

        var tree = ProcessTree.FromPs(Said, self: 13);

        Assert.Equal([99990, 12], tree.Children(500));
        Assert.Equal([99995], ClaudeRun.Jobs(tree, 500, orphansComeToKeeper: false));
        Assert.Equal("Mon Nov  2 18:43:45 2026", tree.Start(99995));
    }

    private static ProcessTree Tree(params (int Pid, int Parent, int Group, string Start)[] processes) =>
        new(processes.ToDictionary(process => process.Pid, process => new ProcessTree.Entry(process.Parent, process.Group, process.Start, process.Pid, process.Start == "zombie")));
}
