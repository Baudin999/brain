using System.Net;
using Brain.Serving;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// The window's perf rows: a window's session sends them in a batch, brain serve writes them as CSV to the data repo's
/// perf/, which git ignores, never to the usage log, and reads the GPU's state for the overlay, empty without
/// nvidia-smi. A file older than 14 days goes when brain serve starts.
/// </summary>
public sealed partial class ReviewServerTests
{
    private static readonly object Row = new
    {
        at = 1_790_000_000_000,
        view = ":home",
        moving = "input+glide",
        frames = 61,
        frameP50 = 16.66,
        frameP95 = 17.2,
        frameMax = 33.4,
        dropped = 1,
        longs = 0,
        longWorst = (double?)null,
        paintP50 = 7.04,
        paintMax = 9.5,
        spark = 25,
        motion = "motion+sparks+glide",
    };

    [Fact]
    public async Task AWindowsPerfRowsGoToThePerfLogAsCsv()
    {
        var token = await Trade(Launch());

        var stranger = await Send(HttpMethod.Post, "/api/perf", Json(new { rows = new[] { Row } }), origin: server.Origin);
        var foreign = await Send(HttpMethod.Post, "/api/perf", Json(new { rows = new[] { Row } }), token: token, origin: "http://evil.example");
        var empty = await Send(HttpMethod.Post, "/api/perf", Json(new { }), token: token, origin: server.Origin);
        var sent = await Send(HttpMethod.Post, "/api/perf", Json(new { rows = new[] { Row, Row } }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var perf = Path.Combine(brain.Home, PerfLog.Dir);
        var lines = await File.ReadAllLinesAsync(Directory.EnumerateFiles(perf, "*.csv").Single());
        Assert.Equal(PerfLog.Header, lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",:home,input+glide,61,16.7,17.2,33.4,1,0,,7.0,9.5,25,motion+sparks+glide,,", lines[1], StringComparison.Ordinal);
        Assert.Equal("*\n", await File.ReadAllTextAsync(Path.Combine(perf, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(brain.Home, UsageLog.Dir)) && Directory.EnumerateFiles(Path.Combine(brain.Home, UsageLog.Dir)).Any(file => File.ReadAllText(file).Contains("input+glide", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheGpuReadsEmptyWithoutNvidiaSmi()
    {
        var token = await Trade(Launch());

        var stranger = await Send(HttpMethod.Get, "/api/perf/gpu");
        var gpu = await (await Send(HttpMethod.Get, "/api/perf/gpu", token: token)).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal("{\"vramMb\":null,\"loadPercent\":null}", gpu);
    }

    [Fact]
    public void APerfFileOlderThanFourteenDaysGoesWhenBrainServeStarts()
    {
        var perf = Path.Combine(brain.Home, PerfLog.Dir);
        Directory.CreateDirectory(perf);
        var old = Path.Combine(perf, "old.csv");
        var recent = Path.Combine(perf, "recent.csv");
        File.WriteAllText(old, PerfLog.Header + "\n");
        File.WriteAllText(recent, PerfLog.Header + "\n");
        File.SetLastWriteTimeUtc(old, time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(15));
        File.SetLastWriteTimeUtc(recent, time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(13));

        Stop();
        Start();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    /// <summary>
    /// A second with the frame loop running and no frame is a pause Chromium did not report as hidden, as a covered
    /// window: the first frame after it only restarts the measurement, so the pause never reads as one long frame.
    /// </summary>
    [WindowFact]
    public async Task TheFirstFrameAfterAPausedSecondRestartsTheMeasurement()
    {
        await InWindow(async window =>
        {
            var counted = await window.Eval("""
                (() => {
                  perf.running = true;
                  perf.until = 0;
                  perf.last = performance.now() - 44714;
                  perf.second = perfSecond();
                  perf.tick();
                  const now = performance.now();
                  perf.step(now);
                  const after = perf.second.frames.length;
                  perf.step(now + 16.7);
                  return `${after} ${perf.second.frames.map((frame) => frame.toFixed(1)).join()}`;
                })()
                """).ConfigureAwait(false);

            Assert.Equal("0 16.7", counted);
        }).ConfigureAwait(false);
    }
}
