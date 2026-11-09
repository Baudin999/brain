using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Brain.Usage;

/// <summary>
/// The usage log: what brain costs and what it gives back, written by code as it happens and never by an agent. It
/// lives in the data repo's <c>usage/</c>, one JSON line per event in a file per month such as <c>2026-11.jsonl</c>,
/// outside <c>graph/</c>, so nothing reads it as knowledge, and it is committed with the data repo. Each line goes to
/// the end of its file in one append, so sessions that write at once never mix their lines.
/// </summary>
internal static class UsageLog
{
    public const string Dir = "usage";
    private const string Extension = ".jsonl";
    private const string Pattern = "*" + Extension;
    private const string MonthFormat = "yyyy-MM";
    private const string AtFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    /// <summary>A line as a person reads it: a +, an apostrophe or a non-ASCII letter stays itself instead of a \u escape.</summary>
    private static readonly JsonTypeInfo<UsageEvent> Line =
        new UsageJson(new JsonSerializerOptions(UsageJson.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).UsageEvent;

    /// <summary>Now, as a usage line writes it: local time to the millisecond, with its offset.</summary>
    public static string Now() => DateTimeOffset.Now.ToString(AtFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Appends <paramref name="usage"/> to this month's file and returns the file's path. A brain home that does not
    /// exist logs nothing, and a log that cannot be written says so on <paramref name="errors"/> and never fails what was
    /// logged; either returns null.
    /// </summary>
    public static string? Append(string home, UsageEvent usage, TextWriter errors)
    {
        if (!Directory.Exists(home))
        {
            return null;
        }

        try
        {
            var dir = Path.Combine(home, Dir);
            Directory.CreateDirectory(dir);
            var month = usage.At[..MonthFormat.Length];
            var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(usage, Line) + "\n");
            var path = Path.Combine(dir, month + Extension);
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            file.Write(line);
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            errors.WriteLine($"brain: the usage log could not be written: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// Every event written after the newest event of kind <paramref name="kind"/>, oldest first, the whole log when it
    /// has none. With <paramref name="mine"/>, the events before the event of that kind and id instead, back to the
    /// event of that kind before it; none when the log has no such event. It reads the files newest first and stops
    /// there, so older months stay unread.
    /// </summary>
    public static IReadOnlyList<UsageEvent> After(string home, string kind, string? mine = null)
    {
        var dir = Path.Combine(home, Dir);
        var after = new List<UsageEvent>();
        if (!Directory.Exists(dir))
        {
            return after;
        }

        var started = mine is null;
        foreach (var path in Directory.EnumerateFiles(dir, Pattern).OrderDescending(StringComparer.Ordinal))
        {
            foreach (var usage in Enumerable.Reverse(File.ReadAllLines(path)).Where(line => line.Length > 0).Select(Parse).OfType<UsageEvent>())
            {
                var ofKind = string.Equals(usage.Event, kind, StringComparison.Ordinal);
                if (!started)
                {
                    started = ofKind && string.Equals(usage.Id, mine, StringComparison.Ordinal);
                }
                else if (ofKind)
                {
                    after.Reverse();
                    return after;
                }
                else
                {
                    after.Add(usage);
                }
            }
        }

        after.Reverse();
        return after;
    }

    /// <summary>Every event in the log, oldest file first and in the order written, and how many lines could not be read.</summary>
    public static (IReadOnlyList<UsageEvent> Events, int Unreadable) Read(string home)
    {
        var dir = Path.Combine(home, Dir);
        if (!Directory.Exists(dir))
        {
            return ([], 0);
        }

        var events = new List<UsageEvent>();
        var unreadable = 0;
        foreach (var path in Directory.EnumerateFiles(dir, Pattern).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(path).Where(line => line.Length > 0))
            {
                if (Parse(line) is { } usage)
                {
                    events.Add(usage);
                }
                else
                {
                    unreadable++;
                }
            }
        }

        return (events, unreadable);
    }

    private static UsageEvent? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, Line) is { At.Length: > 0, Event.Length: > 0 } usage ? usage : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
