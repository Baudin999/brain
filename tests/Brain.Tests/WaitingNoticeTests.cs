using System.Runtime.CompilerServices;
using Brain.Staging;

namespace Brain.Tests;

public sealed class WaitingNoticeTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly string sender;
    private readonly string closer;
    private readonly string log;

    public WaitingNoticeTests()
    {
        log = Path.Combine(brain.Root, "calls.log");
        sender = Fake("send", $"echo \"send $*\" >> '{log}'\necho 42\nsleep 30");
        closer = Fake("close", $"echo \"close $*\" >> '{log}'");
    }

    private PendingStore Store => new(brain.Home);

    public void Dispose() => brain.Dispose();

    [Fact]
    public void OneNoticeForSeveralLandsAndItClosesWhenNoneWaits()
    {
        var first = Ask();
        using var one = Notice();
        using var two = Notice();
        one.Tick();
        two.Tick();
        WaitFor(() => Calls().Any(call => call.StartsWith("send", StringComparison.Ordinal)));
        Assert.Single(Calls(), call => call.StartsWith("send", StringComparison.Ordinal));
        Assert.Contains("1 write waits", Calls()[0], StringComparison.Ordinal);

        WaitFor(() => File.Exists(Path.Combine(Store.Root, "notice.id")));
        var second = Ask();
        one.Tick();
        WaitFor(() => Calls().Count(call => call.StartsWith("send", StringComparison.Ordinal)) == 2);
        Assert.Contains("--replace-id=42", Calls()[1], StringComparison.Ordinal);
        Assert.Contains("2 writes wait", Calls()[1], StringComparison.Ordinal);

        Answer(first);
        Answer(second);
        one.Tick();
        WaitFor(() => Calls().Any(call => call == "close call --session --dest org.freedesktop.Notifications --object-path /org/freedesktop/Notifications --method org.freedesktop.Notifications.CloseNotification 42"));
    }

    [Fact]
    public void AnotherLandTakesOverWhenTheOwnerLeaves()
    {
        Ask();
        var one = Notice();
        using var two = Notice();
        one.Tick();
        two.Tick();
        WaitFor(() => File.Exists(Path.Combine(Store.Root, "notice.id")));
        one.Dispose();
        two.Tick();

        WaitFor(() => Calls().Count(call => call.StartsWith("send", StringComparison.Ordinal)) == 2);
        Assert.Contains("--replace-id=42", Calls()[1], StringComparison.Ordinal);
        Assert.DoesNotContain(Calls(), call => call.StartsWith("close", StringComparison.Ordinal));
    }

    [Fact]
    public void NoNotifierMeansNoNoticeAndNoError()
    {
        Ask();
        using var missing = new WaitingNotice(Store, Path.Combine(brain.Root, "absent"), null, Path.Combine(brain.Root, "absent"));
        missing.Tick();
        using var off = new WaitingNotice(Store, WaitingNotice.Off, null);
        off.Tick();

        Assert.Empty(Calls());
    }

    [Fact]
    public void AClickRunsTheCommandWithNoStreamOfItsOwn()
    {
        Ask();
        var sender = Fake("clicking", "echo 42\necho default\nsleep 30");
        var touched = Path.Combine(brain.Root, "clicked");
        using var notice = new WaitingNotice(Store, sender, $"echo noise; touch '{touched}'", closer, TimeSpan.Zero);
        notice.Tick();

        WaitFor(() => File.Exists(touched));
    }

    [Fact]
    public void ASurvivingNoticeClosesWhatAKilledOwnerLeft()
    {
        Directory.CreateDirectory(Store.Root);
        File.WriteAllText(Path.Combine(Store.Root, "notice.id"), "42");
        using var notice = Notice();
        notice.Settle();

        WaitFor(() => Calls().Any(call => call.StartsWith("close", StringComparison.Ordinal) && call.EndsWith(" 42", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADaemonThatAnswersNothingIsAskedOncePerCount()
    {
        Ask();
        var mute = Fake("mute", $"echo \"send $*\" >> '{log}'");
        using var notice = new WaitingNotice(Store, mute, null, closer, TimeSpan.Zero);
        for (var tick = 0; tick < 5; tick++)
        {
            notice.Tick();
        }

        Assert.Single(Calls());
    }

    private WaitingNotice Notice() => new(Store, sender, null, closer, TimeSpan.Zero);

    private string Ask()
    {
        var write = Store.Stage(["added"], [], new WriteOrigin(Session: null));
        Store.Ask(write.Id);
        return write.Id;
    }

    private void Answer(string id) => Store.Answer(id, Store.Asked().Single(write => write.Write.Id == id).Digest, yes: false, reason: null);

    private string[] Calls() => File.Exists(log) ? File.ReadAllLines(log) : [];

    private string Fake(string name, string body)
    {
        var path = Path.Combine(brain.Root, name + ".sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (var tries = 0; tries < 100 && !condition(); tries++)
        {
            Thread.Sleep(50);
        }

        Assert.True(condition());
    }
}

internal static class NoNotifications
{
    /// <summary>No test run shows a desktop notification: the other tests drive `brain land` for real.</summary>
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable(WaitingNotice.SendVariable, WaitingNotice.Off);
}
