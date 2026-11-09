using System.Globalization;
using System.Text.Json;

namespace Brain.Stats;

/// <summary>
/// One event of Navarone's log, read once into a typed record: its type, its UTC time and day, the worktree it happened
/// in, its place in the log and the fields brain shows. An id is granted per worktree, so a problem is a worktree and an
/// id.
/// </summary>
internal sealed record NavaroneEvent(int Position, string Type, DateTimeOffset At, string Worktree)
{
    public DateOnly Day => DateOnly.FromDateTime(At.UtcDateTime);

    /// <summary>The main checkout every worktree of the repo shares; the worktree when the log names none.</summary>
    public required string Repo { get; init; }

    public string? Branch { get; init; }

    public string? Id { get; init; }

    public string? Check { get; init; }

    public string? Symbol { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    public string? Finding { get; init; }

    public string? Reason { get; init; }

    /// <summary>An escalation's text: what is wrong, where it shows and what the fix is.</summary>
    public string? Text { get; init; }

    /// <summary>The user's position in a settled escalation, keep or replace, and the words he gave with it.</summary>
    public string? User { get; init; }

    public string? Words { get; init; }

    /// <summary>The file and line of an escalation's finding, as a lens names them; the line is 0 when the event names none.</summary>
    public string? Path { get; init; }

    public long Line { get; init; }

    public bool SecondRound { get; init; }

    public long Tokens { get; init; }

    /// <summary>Reads one event; null when it is no object or lacks a type, a day or a worktree.</summary>
    // navarone: 0000152
    public static NavaroneEvent? Parse(JsonElement one, int position)
    {
        if (one.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = Field(one, "type");
        var worktree = Field(one, "worktree");
        if (type is null || worktree is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(Field(one, "at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return null;
        }

        return new NavaroneEvent(position, type, at, worktree)
        {
            Repo = Field(one, "repo") ?? worktree,
            Branch = Field(one, "branch"),
            Id = Field(one, "id"),
            Check = Field(one, "check"),
            Symbol = Field(one, "symbol"),
            Before = Field(one, "before"),
            After = Field(one, "after"),
            Finding = Field(one, "finding"),
            Reason = Field(one, "reason"),
            Text = Field(one, "text"),
            User = Field(one, "user"),
            Words = Field(one, "words"),
            Path = Field(one, "path"),
            Line = Number(one, "line"),
            SecondRound = Flag(one, "secondRound"),
            Tokens = Number(one, "tokens"),
        };
    }

    public bool Within(DateOnly? since, DateOnly? until)
    {
        if (since is { } first && Day < first)
        {
            return false;
        }

        if (until is { } last && Day > last)
        {
            return false;
        }

        return true;
    }

    private static string? Field(JsonElement one, string name)
    {
        if (!one.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool Flag(JsonElement one, string name)
    {
        if (!one.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True;
    }

    private static long Number(JsonElement one, string name)
    {
        if (!one.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        value.TryGetInt64(out var number);
        return number;
    }
}
