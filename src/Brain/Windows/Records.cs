namespace Brain.Windows;

/// <summary>
/// One record of a small window: a command its window ran, or work its server did. A command names its source, as a
/// workspace's key, bar, click, link, file for a change on disk, or start; the server's work, as a file read, a git call,
/// a session started or a minute of its CPU, says server. Its outcome is ok, refused or failed. A record with a budget, in
/// milliseconds, is over when it took longer.
/// </summary>
internal sealed record WindowRecord(long At, string Name, string Args, string Source, double Ms, string Outcome, double? Budget, bool Over)
{
    public const string Ok = "ok";
    public const string Refused = "refused";
    public const string Failed = "failed";
    public const string Server = "server";
}

/// <summary>
/// The records of a window's server: each goes to the <see cref="WindowLog"/>, and the last of the server's own stay
/// in memory for the probe, numbered, so the window asks only for those it has not seen.
/// </summary>
internal sealed class Records(WindowLog log, TimeProvider time)
{
    /// <summary>The most of the server's own records kept for the probe: far more than its last few seconds.</summary>
    private const int Kept = 500;

    private readonly Lock gate = new();
    private readonly Queue<(long Number, WindowRecord Record)> kept = new();
    private long last;

    /// <summary>
    /// Runs <paramref name="work"/>, records it as the server's with how long it took, and returns what it returned. A
    /// refusal is recorded as refused and a failure as failed, and each goes on to the caller.
    /// </summary>
    public T Time<T>(string name, string args, double? budget, Func<T> work)
    {
        var started = time.GetTimestamp();
        var outcome = WindowRecord.Failed;
        try
        {
            var result = work();
            outcome = WindowRecord.Ok;
            return result;
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            outcome = WindowRecord.Refused;
            throw;
        }
        finally
        {
            var ms = time.GetElapsedTime(started).TotalMilliseconds;
            Add(name, args, ms, outcome, budget);
        }
    }

    /// <summary>Records work of the server's that was timed elsewhere.</summary>
    public void Add(string name, string args, double ms, string outcome, double? budget)
    {
        var record = new WindowRecord(time.GetUtcNow().ToUnixTimeMilliseconds(), name, args, WindowRecord.Server, ms, outcome, budget, ms > budget);
        lock (gate)
        {
            last++;
            kept.Enqueue((last, record));
            if (kept.Count > Kept)
            {
                kept.Dequeue();
            }
        }

        log.Append([record]);
    }

    /// <summary>The window's own records, which go to the log only: the window keeps them for its probe itself.</summary>
    public void AddWindows(IReadOnlyList<WindowRecord> records) => log.Append(records);

    /// <summary>The server's records numbered past <paramref name="seen"/>, oldest first.</summary>
    public ServerRecords Since(long seen)
    {
        lock (gate)
        {
            var records = kept.Where((one) => one.Number > seen).Select((one) => one.Record).ToList();
            return new ServerRecords(last, records);
        }
    }
}
