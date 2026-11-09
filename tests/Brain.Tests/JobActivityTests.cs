using Brain.Children;

namespace Brain.Tests;

/// <summary>
/// Whether a session's job runs or is idle: a job seen for the first time runs, one whose tree used no CPU time for
/// thirty seconds is idle since the read that last saw its time change, one that used some runs again, one gone is
/// forgotten, a later process given the same pid is a job of its own, and one whose time cannot be read always runs.
/// </summary>
public sealed class JobActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AJobIsIdleOnceItsTreeUsedNoCpuTimeForThirtySeconds()
    {
        var activity = new JobActivity();

        var first = activity.Read([new JobUse(10, "a", 5)], Start);
        var soon = activity.Read([new JobUse(10, "a", 5)], Start.AddSeconds(29));
        var quiet = activity.Read([new JobUse(10, "a", 5)], Start.AddSeconds(30));
        var used = activity.Read([new JobUse(10, "a", 6)], Start.AddSeconds(40));

        Assert.Equal([null], first);
        Assert.Equal([null], soon);
        Assert.Equal([Start], quiet);
        Assert.Equal([null], used);
    }

    [Fact]
    public void AJobGoneIsForgottenAndALaterProcessWithItsPidIsNew()
    {
        var activity = new JobActivity();
        activity.Read([new JobUse(10, "a", 5), new JobUse(11, "b", 1)], Start);

        activity.Read([new JobUse(11, "b", 1)], Start.AddSeconds(10));
        var back = activity.Read([new JobUse(10, "a", 5), new JobUse(11, "b", 1), new JobUse(12, "c", null)], Start.AddSeconds(60));
        var reused = activity.Read([new JobUse(11, "z", 1), new JobUse(12, "c", null)], Start.AddSeconds(120));

        Assert.Equal([null, Start, null], back);
        Assert.Equal([null, null], reused);
    }

    [Fact]
    public void ATreesCpuTimeIsItsProcessesSumAndUnknownWhenOneHasNone()
    {
        var tree = new ProcessTree(new Dictionary<int, ProcessTree.Entry>
        {
            [10] = new(1, 10, "a", 1, Zombie: false, Cpu: 4),
            [11] = new(10, 10, "b", 2, Zombie: false, Cpu: 3),
            [12] = new(11, 10, "c", 3, Zombie: false, Cpu: 2),
            [20] = new(1, 20, "d", 4, Zombie: false, Cpu: 1),
            [21] = new(20, 20, "e", 5, Zombie: false),
        });

        Assert.Equal(9, tree.CpuOf(10));
        Assert.Equal(2, tree.CpuOf(12));
        Assert.Null(tree.CpuOf(20));
        Assert.Null(tree.CpuOf(99));
        Assert.Equal(OperatingSystem.IsLinux(), ProcessTree.Read().CpuOf(Environment.ProcessId) is not null);
    }
}
