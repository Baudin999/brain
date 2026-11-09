using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The launch code a window server hands its successor: it is taken once, and one past its life is no handover, so a
/// server that finds a code a successor that never started left opens its own window.
/// </summary>
public sealed class HandedLaunchTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly Clock time = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AHandedCodeIsTakenOnce()
    {
        var files = new ServeFiles(brain.Home);
        var code = HandedLaunch.Hand(files, time);

        var taken = HandedLaunch.Take(files, time);
        var again = HandedLaunch.Take(files, time);

        Assert.Matches("^[0-9a-f]{64}$", code);
        Assert.Equal(code, taken);
        Assert.Null(again);
    }

    [Fact]
    public void ACodePastItsLifeIsNoHandoverAndGoes()
    {
        var files = new ServeFiles(brain.Home);
        HandedLaunch.Hand(files, time);
        time.Advance(HandedLaunch.Life + TimeSpan.FromSeconds(1));

        var taken = HandedLaunch.Take(files, time);

        Assert.Null(taken);
        Assert.False(File.Exists(Path.Combine(files.Dir, HandedLaunch.FileName)));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan span) => now += span;
    }
}
