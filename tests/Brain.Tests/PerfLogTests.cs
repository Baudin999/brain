using System.Diagnostics;
using System.Globalization;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The perf log's files: each opens with the header and never grows past 1 MB, a text field that holds a comma is
/// quoted, and each row carries the GPU's state of its second. The GPU's state comes from one long-lived child that
/// prints a line a second, and stops when brain serve disposes it.
/// </summary>
public sealed class PerfLogTests : IDisposable
{
    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AFileNeverGrowsPastOneMegabyte()
    {
        var log = new PerfLog(brain.Home, TimeProvider.System);
        var rows = Enumerable.Repeat(Row(1_790_000_000_000, "node review"), 100).ToList();

        for (var batch = 0; batch < 300; batch++)
        {
            log.Append(rows, _ => null);
        }

        var files = Directory.EnumerateFiles(log.Folder, "*.csv").Order(StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= 2, $"{files.Count} files");
        Assert.All(files, file => Assert.True(new FileInfo(file).Length <= PerfLog.MaxBytes, file));
        Assert.All(files, file => Assert.Equal(PerfLog.Header, File.ReadLines(file).First()));
        Assert.Equal(30000, files.Sum(file => File.ReadLines(file).Count() - 1));
    }

    [Fact]
    public void ARowCarriesTheGpuOfItsSecondAndQuotesAComma()
    {
        var log = new PerfLog(brain.Home, TimeProvider.System);

        log.Append([Row(1_790_000_000_999, "a, \"b\"")], second => second == 1_790_000_000 ? new GpuSample(23494, 35) : null);

        var line = File.ReadLines(Directory.EnumerateFiles(log.Folder, "*.csv").Single()).Last();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_999).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        Assert.Equal(at + ",\"a, \"\"b\"\"\",,0,,,,0,0,,,,0,,23494,35", line);
    }

    [Fact]
    public void AnNvidiaSmiLineReadsAsVramAndLoad()
    {
        Assert.Equal(new GpuSample(23494, 2), GpuWatch.Parse("23494, 2"));
        Assert.Null(GpuWatch.Parse("[N/A], 2"));
        Assert.Null(GpuWatch.Parse("23494"));
    }

    [Fact]
    public void TheGpuWatchReadsItsChildAndStopsIt()
    {
        var script = Path.Combine(brain.Root, "gpu");
        var pid = Path.Combine(brain.Root, "gpu.pid");
        File.WriteAllText(script, $"#!/bin/sh\necho $$ > '{pid}'\nwhile true; do echo '1234, 56'; sleep 0.1; done\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        int child;
        using (var watch = GpuWatch.Start(TimeProvider.System, script, []))
        {
            Assert.True(SpinWait.SpinUntil(() => watch.Now() is not null, TimeSpan.FromSeconds(10)));
            Assert.Equal(new GpuSample(1234, 56), watch.Now());
            child = int.Parse(File.ReadAllText(pid).Trim(), CultureInfo.InvariantCulture);
        }

        Assert.True(SpinWait.SpinUntil(() => !Alive(child), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AGpuWatchWithoutItsProgramReadsNothing()
    {
        using var watch = GpuWatch.Start(TimeProvider.System, Path.Combine(brain.Root, "no-such-program"), []);

        Assert.Null(watch.Now());
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static PerfRow Row(long at, string view) =>
        new(at, view, null, 0, null, null, null, 0, 0, null, null, null, 0, null);
}
