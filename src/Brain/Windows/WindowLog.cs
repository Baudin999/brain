using System.Globalization;
using System.Text;
using System.Text.Json;
using Brain.Serving;

namespace Brain.Windows;

/// <summary>
/// A small window's log: every record of its window and its server, one JSON line each, in the data repo's
/// <c>perf/&lt;dir&gt;</c>, as <c>perf/workspace</c> for a workspace and <c>perf/code</c> for brain code. It never leaves
/// the machine and stays outside git, as <see cref="PerfFolder"/> keeps it. Each window writes a file a day, named for
/// the day and the window, and files older than 14 days go when its server starts.
/// </summary>
internal sealed class WindowLog(string home, string dir, string window, TimeProvider time)
{
    private const string Extension = ".jsonl";
    private const string DayFormat = "yyyy-MM-dd";

    private readonly Lock gate = new();

    public string Folder { get; } = Path.Combine(home, "perf", dir);

    /// <summary>Deletes every file of the log last written more than 14 days ago.</summary>
    public void Prune() => PerfFolder.Prune(Folder, Extension, time.GetUtcNow());

    /// <summary>Appends <paramref name="records"/> to today's file of this window.</summary>
    public void Append(IReadOnlyList<WindowRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }

        var text = new StringBuilder();
        foreach (var record in records)
        {
            text.Append(JsonSerializer.Serialize(record, WindowJson.Default.WindowRecord)).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        lock (gate)
        {
            using var stream = new FileStream(Today(), FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
        }
    }

    /// <summary>Today's file, with the folder and its .gitignore made first.</summary>
    private string Today()
    {
        PerfFolder.Make(Folder);

        var day = time.GetLocalNow().ToString(DayFormat, CultureInfo.InvariantCulture);
        return Path.Combine(Folder, day + "-" + window + Extension);
    }
}
