using System.Globalization;
using System.Text;

namespace Brain.Serving;

/// <summary>
/// The perf log: how brain's window runs, one CSV row per second the window recorded, apart from the usage log, which is
/// for brain's events. It lives in the data repo's <c>perf/</c>, outside git, as <see cref="PerfFolder"/> keeps it. Each
/// file opens with the header line and never grows past 1 MB; a new one starts when a batch would take it past, named
/// for the time it starts. Files older than 14 days go when brain serve starts. The window sends its rows in batches,
/// and brain serve adds the GPU's state of each row's second.
/// </summary>
internal sealed class PerfLog(string home, TimeProvider time)
{
    public const string Dir = "perf";
    public const long MaxBytes = 1024 * 1024;
    public const string Header =
        "time,view,moving,frames,frame_p50_ms,frame_p95_ms,frame_max_ms,dropped,long_frames,long_worst_ms,"
        + "paint_p50_ms,paint_max_ms,repaints_spark,motion_settings,vram_mb,gpu_load_pct";

    private const string Extension = ".csv";
    private const string NameFormat = "yyyy-MM-ddTHH-mm-ss";
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:sszzz";
    private const int MaxText = 64;

    private readonly Lock gate = new();
    private string? current;

    public string Folder { get; } = Path.Combine(home, Dir);

    /// <summary>Deletes every file of the log last written more than 14 days ago.</summary>
    public void Prune() => PerfFolder.Prune(Folder, Extension, time.GetUtcNow());

    /// <summary>Appends <paramref name="rows"/>, each with the GPU's state <paramref name="gpu"/> reads for its second.</summary>
    public void Append(IReadOnlyList<PerfRow> rows, Func<long, GpuSample?> gpu)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.Append(Line(row, gpu(row.At / 1000))).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        lock (gate)
        {
            var file = Target(bytes.Length);
            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }

    /// <summary>The file a batch of <paramref name="bytes"/> goes to: the current one while it stays under 1 MB, else a new one with its header.</summary>
    private string Target(int bytes)
    {
        if (current is not null && File.Exists(current) && new FileInfo(current).Length + bytes <= MaxBytes)
        {
            return current;
        }

        PerfFolder.Make(Folder);

        var name = time.GetLocalNow().ToString(NameFormat, CultureInfo.InvariantCulture);
        var next = Path.Combine(Folder, name + Extension);
        for (var count = 2; next == current || File.Exists(next); count++)
        {
            next = Path.Combine(Folder, name + "-" + count.ToString(CultureInfo.InvariantCulture) + Extension);
        }

        File.WriteAllText(next, Header + "\n");
        current = next;
        return next;
    }

    private string Line(PerfRow row, GpuSample? gpu)
    {
        var at = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(row.At), time.LocalTimeZone);
        string[] fields =
        [
            at.ToString(TimeFormat, CultureInfo.InvariantCulture),
            Text(row.View),
            Text(row.Moving),
            Count(row.Frames),
            Ms(row.FrameP50),
            Ms(row.FrameP95),
            Ms(row.FrameMax),
            Count(row.Dropped),
            Count(row.Longs),
            Ms(row.LongWorst),
            Ms(row.PaintP50),
            Ms(row.PaintMax),
            Count(row.Spark),
            Text(row.Motion),
            gpu is null ? string.Empty : Count(gpu.VramMb),
            gpu is null ? string.Empty : Count(gpu.LoadPercent),
        ];
        return string.Join(',', fields);
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A time in ms to a tenth; an empty field when there is none, as a paint time on a view without one.</summary>
    private static string Ms(double? value) => value is { } ms ? ms.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>A text field as CSV reads it: cut short, and quoted with its quotes doubled when it holds a comma, a quote or a line break.</summary>
    private static string Text(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > MaxText)
        {
            text = text[..MaxText];
        }

        if (text.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

/// <summary>
/// One second of the window, as it sends it: its end in ms since the epoch, the view shown, what moved, joined by +,
/// the frames presented, the frame time p50, p95 and max, the dropped frames, the long animation frames and the worst
/// of them, the view's own paint time p50 and max when it has one, the sparks' repaints, and which
/// of Home's motion settings were on, joined by +.
/// </summary>
internal sealed record PerfRow(
    long At,
    string? View,
    string? Moving,
    int Frames,
    double? FrameP50,
    double? FrameP95,
    double? FrameMax,
    int Dropped,
    int Longs,
    double? LongWorst,
    double? PaintP50,
    double? PaintMax,
    int Spark,
    string? Motion);

/// <summary>A batch of the window's rows.</summary>
internal sealed record PerfBatch(IReadOnlyList<PerfRow>? Rows);

/// <summary>How many rows of a batch the perf log took.</summary>
internal sealed record PerfReply(int Rows);

/// <summary>The GPU's state now, for the window's overlay: nulls without <c>nvidia-smi</c>.</summary>
internal sealed record GpuView(int? VramMb, int? LoadPercent)
{
    public static GpuView Of(GpuSample? sample) => new(sample?.VramMb, sample?.LoadPercent);
}
