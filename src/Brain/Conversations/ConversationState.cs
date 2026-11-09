using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Brain.Conversations;

/// <summary>
/// One sitting: one stretch of talking on one model at one effort, or at none when a parked session recorded none, from
/// when it started to when the next one ended it, and the id of the dream that settled it; null while it is unsettled.
/// </summary>
internal sealed record Sitting(int Number, string Model, string? Effort, DateTimeOffset Started, DateTimeOffset? Ended, string? Dream = null)
{
    /// <summary>The model, and the effort when the sitting names one: <c>opus at low effort</c> or <c>opus</c>.</summary>
    [JsonIgnore]
    public string Runs => Effort is null ? Model : $"{Model} at {Effort} effort";
}

/// <summary>
/// A conversation's <c>state.md</c>: its title, its status, the day it was born and every sitting. The frontmatter holds
/// the first three; a table under <c>## Sittings</c> holds the sittings, one row each, oldest first. A table from before
/// dreaming has no dream column, and every sitting in it reads as unsettled.
/// </summary>
internal sealed record ConversationState(string Title, string Status, DateOnly Born, IReadOnlyList<Sitting> Sittings)
{
    public const string Active = "active";
    public const string FirstTitle = "New conversation";
    public const int MaxTitle = 120;

    private const string Fence = "---";
    private const string TitleKey = "title";
    private const string StatusKey = "status";
    private const string BornKey = "born";
    private const string KeySeparator = ": ";
    private const string SittingsHeading = "## Sittings";
    private const string TableHead = "| sitting | model | effort | started | ended | dream |";
    private const string TableRule = "|---|---|---|---|---|---|";
    private const string UndreamedHead = "| sitting | model | effort | started | ended |";
    private const string UndreamedRule = "|---|---|---|---|---|";
    private const string None = "-";
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";
    private const char Cell = '|';
    private const int UndreamedColumns = 5;
    private const int EffortColumn = 2;
    private const int StartedColumn = 3;
    private const int EndedColumn = 4;
    private const int DreamColumn = 5;

    /// <summary>The sitting talked in now: the last one.</summary>
    [JsonIgnore]
    public Sitting Current => Sittings[^1];

    /// <summary>Why a title cannot be one: empty, more than one line, or too long; null when it can.</summary>
    public static string? TitleProblem(string title) =>
        string.IsNullOrWhiteSpace(title) ? "a conversation's title is empty"
        : title.Contains('\n', StringComparison.Ordinal) || title.Contains('\r', StringComparison.Ordinal) ? "a conversation's title is one line"
        : title.Trim().Length > MaxTitle ? $"a conversation's title is at most {MaxTitle} characters"
        : null;

    public string Render()
    {
        var text = new StringBuilder();
        text.Append(Fence).Append('\n');
        text.Append(TitleKey).Append(KeySeparator).Append(Title).Append('\n');
        text.Append(StatusKey).Append(KeySeparator).Append(Status).Append('\n');
        text.Append(BornKey).Append(KeySeparator).Append(Born.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture)).Append('\n');
        text.Append(Fence).Append("\n\n").Append(SittingsHeading).Append("\n\n");
        text.Append(TableHead).Append('\n').Append(TableRule).Append('\n');
        foreach (var sitting in Sittings)
        {
            var ended = sitting.Ended is { } end ? Time(end) : None;
            text.Append(CultureInfo.InvariantCulture, $"| {sitting.Number} | {sitting.Model} | {sitting.Effort ?? None} | {Time(sitting.Started)} | {ended} | {sitting.Dream ?? None} |\n");
        }

        return text.ToString();
    }

    /// <summary>Reads a <c>state.md</c>; throws a refusal that names <paramref name="path"/> for anything it cannot read.</summary>
    public static ConversationState Parse(string text, string path)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 2 || lines[0] != Fence)
        {
            throw Unreadable(path, "it does not start with a --- frontmatter");
        }

        var end = Array.IndexOf(lines, Fence, 1);
        if (end < 0)
        {
            throw Unreadable(path, "its frontmatter never closes");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines[1..end])
        {
            var at = line.IndexOf(KeySeparator, StringComparison.Ordinal);
            if (at > 0)
            {
                fields[line[..at]] = line[(at + KeySeparator.Length)..].Trim();
            }
        }

        var title = fields.GetValueOrDefault(TitleKey) ?? throw Unreadable(path, "it has no title");
        var status = fields.GetValueOrDefault(StatusKey) ?? throw Unreadable(path, "it has no status");
        if (!DateOnly.TryParseExact(fields.GetValueOrDefault(BornKey), FieldNames.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var born))
        {
            throw Unreadable(path, "its born is not a yyyy-MM-dd date");
        }

        var sittings = lines[(end + 1)..]
            .SkipWhile(line => line.Trim() != SittingsHeading)
            .Skip(1)
            .Where(line => line.StartsWith(Cell) && line.Trim() is not (TableHead or TableRule or UndreamedHead or UndreamedRule))
            .Select(line => Row(line, path))
            .ToList();
        return sittings.Count == 0 ? throw Unreadable(path, "it lists no sitting") : new ConversationState(title, status, born, sittings);
    }

    private static Sitting Row(string line, string path)
    {
        var cells = line.Trim().Trim(Cell).Split(Cell).Select(cell => cell.Trim()).ToArray();
        if (cells.Length is not (UndreamedColumns or UndreamedColumns + 1)
            || !int.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || !Parsed(cells[StartedColumn], out var started))
        {
            throw Unreadable(path, $"a sitting row is not | number | model | effort | started | ended | dream |: {line}");
        }

        DateTimeOffset? ended = Parsed(cells[EndedColumn], out var end) ? end : null;
        var dream = cells.Length > DreamColumn && cells[DreamColumn] is { Length: > 0 } id && id != None ? id : null;
        return new Sitting(number, cells[1], cells[EffortColumn] == None ? null : cells[EffortColumn], started, ended, dream);
    }

    private static bool Parsed(string cell, out DateTimeOffset time) =>
        DateTimeOffset.TryParseExact(cell, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);

    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture);

    private static BrainException Unreadable(string path, string why) => new($"{path} is not a conversation's state: {why}");
}
