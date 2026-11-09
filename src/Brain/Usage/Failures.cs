using System.Globalization;

namespace Brain.Usage;

/// <summary>
/// Brain's errors, which it never swallows. Each one goes to the usage log as an <c>error</c> event, a fact: what
/// failed, the one line the user is shown and the whole exception. The session start tells the user of every error
/// written since they were last told, once: <c>brain context</c> says how many and which was the latest, and writes a
/// <c>told</c> event, so the next session start says nothing of them.
/// </summary>
internal static class Failures
{
    private const int MinuteLength = 16;

    /// <summary>
    /// Writes <paramref name="error"/> of <paramref name="failed"/> to the usage log and returns the line that tells the
    /// user: what failed, why, and where the whole error is kept.
    /// </summary>
    public static string Record(string? home, string failed, Exception error, string? session, TextWriter errors)
    {
        var message = FirstLine(error.Message);
        var path = home is null ? null : UsageLog.Append(home, new UsageEvent(UsageLog.Now(), UsageEvent.Error, session)
        {
            Failed = failed,
            Problem = message,
            Detail = error.ToString(),
        }, errors);
        var line = $"{failed} failed: {message}";
        return path is null ? line : $"{line}; the usage log keeps the whole error in {path}";
    }

    /// <summary>
    /// The line that tells the user of every error written since they were last told: how many and the latest. Null
    /// when there is none. Each error is told once, see <see cref="Tell"/>.
    /// </summary>
    public static string? Untold(string home, string? session, TextWriter errors) =>
        HasUntold(home) ? Tell(home, session, errors) : null;

    /// <summary>True when an error was written after the last <c>told</c>: a read that writes nothing.</summary>
    public static bool HasUntold(string home) =>
        UsageLog.After(home, UsageEvent.Told).Any(usage => usage.Event == UsageEvent.Error);

    /// <summary>
    /// Writes a <c>told</c> of its own id first, then tells the errors between the <c>told</c> before it and this one.
    /// An error written meanwhile by another session lands before this <c>told</c> or after it, so this start or the next
    /// tells it; two starts at once each tell the errors before their own <c>told</c> only, and none is told twice.
    /// Null when another start told every one first.
    /// </summary>
    public static string? Tell(string home, string? session, TextWriter errors)
    {
        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        if (UsageLog.Append(home, new UsageEvent(UsageLog.Now(), UsageEvent.Told, session) { Id = id }, errors) is null)
        {
            return null;
        }

        var untold = UsageLog.After(home, UsageEvent.Told, id)
            .Where(usage => usage.Event == UsageEvent.Error)
            .ToList();
        if (untold.Count == 0)
        {
            return null;
        }

        var latest = untold[^1];
        var at = latest.At[..Math.Min(latest.At.Length, MinuteLength)].Replace('T', ' ');
        var count = untold.Count == 1 ? "1 error" : $"{untold.Count} errors";
        return $"errors    {count} since you were last told; the latest, at {at}: {latest.Failed} failed: {latest.Problem}; "
            + $"each is an error event in {Path.Combine(home, UsageLog.Dir)}";
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "no message";
}
