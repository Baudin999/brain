using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Brain.Conversations;

/// <summary>One phase of a sitting: its number, who spoke, when, the agent's model and its effort when the sitting
/// names one, and what was said. A user's phase can hold comments, in the order they were added; none is null.</summary>
internal sealed record Phase(int Number, string Who, DateTimeOffset At, string? Model, string? Effort, string Text, IReadOnlyList<PhaseComment>? Comments = null);

/// <summary>
/// A comment of a user's phase, numbered from 1 in that phase. It anchors to a phase of the sitting, or to settled when
/// <paramref name="Target"/> is <see cref="Trail.Settled"/>, and to the text it quotes there, never to a character offset.
/// </summary>
internal sealed record PhaseComment(int Number, int Target, string Quote, string Text);

/// <summary>
/// A sitting's trail: markdown, one file per sitting in the conversation's <c>trails/</c>, written only by brain serve.
/// Each phase is a header with its number, who, its time in UTC to the minute and, for the agent, the model and, when
/// the sitting names one, the effort, then what was said. The agent's thinking and its tool calls are not kept.
/// </summary>
internal static partial class Trail
{
    public const string User = "user";
    public const string Agent = "agent";
    public const string Dir = "trails";

    /// <summary>The target of a comment on settled.md, which no phase number can be.</summary>
    public const int Settled = 0;

    private const string TimeFormat = "yyyy-MM-ddTHH:mm";
    private const string Extension = ".md";

    /// <summary>The trail of sitting <paramref name="sitting"/> in the conversation folder <paramref name="conversation"/>.</summary>
    public static string PathOf(string conversation, int sitting) =>
        Path.Combine(conversation, Dir, sitting.ToString(CultureInfo.InvariantCulture) + Extension);

    /// <summary>A phase as the trail holds it: its header, a blank line, its text and a blank line.</summary>
    public static string Render(Phase phase)
    {
        var header = new StringBuilder();
        header.Append(CultureInfo.InvariantCulture, $"## phase {phase.Number} · {phase.Who} · {phase.At.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture)}");
        if (phase.Who == Agent)
        {
            header.Append(CultureInfo.InvariantCulture, $" · {phase.Model}{(phase.Effort is null ? string.Empty : " " + phase.Effort)}");
        }

        return header.Append("\n\n").Append(Body(phase)).Append("\n\n").ToString();
    }

    /// <summary>
    /// What the user said in a phase: each comment as a quote line, <c>&gt; 1 on phase 6: "text"</c>, then the comment,
    /// then a blank line, then the text typed at the bottom. The agent receives this, so it can answer each comment.
    /// A line of the user's own that starts with <c>&gt;</c> gets a backslash before it, so only the quote lines start
    /// with one. A blank line inside a comment is a line holding only <c>&gt;</c>, so a comment ends at its first blank line.
    /// </summary>
    public static string Body(Phase phase)
    {
        if (phase.Who != User)
        {
            return phase.Text.Trim();
        }

        var body = new StringBuilder();
        foreach (var comment in phase.Comments ?? [])
        {
            var on = comment.Target == Settled ? "settled" : string.Create(CultureInfo.InvariantCulture, $"phase {comment.Target}");
            body.Append(CultureInfo.InvariantCulture, $"> {comment.Number} on {on}: \"{comment.Quote}\"\n");
            foreach (var line in comment.Text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                body.Append(string.IsNullOrWhiteSpace(line) ? ">" : Escape(line)).Append('\n');
            }

            body.Append('\n');
        }

        return body.Append(string.Join('\n', phase.Text.Trim().Split('\n').Select(Escape))).ToString().Trim();
    }

    private static string Escape(string line) => Quoting().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line) => Escaped().IsMatch(line) ? line[1..] : line;

    /// <summary>Adds a phase at the end of its trail, making <c>trails/</c> when the sitting has none yet.</summary>
    public static void Append(string path, Phase phase)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, Render(phase));
    }

    /// <summary>
    /// The phases of a trail, in order; none when it does not exist yet. A header counts only when it carries the next
    /// number, so a phase whose text quotes a header stays one phase.
    /// </summary>
    public static IReadOnlyList<Phase> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var phases = new List<Phase>();
        Phase? open = null;
        var text = new StringBuilder();
        foreach (var line in File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var header = Header().Match(line);
            if (header.Success && int.Parse(header.Groups["number"].Value, CultureInfo.InvariantCulture) == phases.Count + (open is null ? 1 : 2))
            {
                Close(phases, open, text);
                open = new Phase(
                    phases.Count + 1,
                    header.Groups["who"].Value,
                    DateTimeOffset.ParseExact(header.Groups["at"].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    header.Groups["model"].Success ? header.Groups["model"].Value : null,
                    header.Groups["effort"].Success ? header.Groups["effort"].Value : null,
                    string.Empty);
                text.Clear();
                continue;
            }

            text.Append(line).Append('\n');
        }

        Close(phases, open, text);
        return phases;
    }

    private static void Close(List<Phase> phases, Phase? open, StringBuilder text)
    {
        if (open is not null)
        {
            var (comments, rest) = open.Who == User ? Comments(text.ToString().Trim()) : ([], text.ToString().Trim());
            phases.Add(open with { Text = rest, Comments = comments.Count > 0 ? comments : null });
        }
    }

    /// <summary>The comments a user's phase opens with, numbered one after the other, and the text after them.</summary>
    private static (List<PhaseComment> Comments, string Remainder) Comments(string text)
    {
        var comments = new List<PhaseComment>();
        var lines = text.Split('\n');
        var at = 0;
        while (at + 1 < lines.Length && QuoteLine().Match(lines[at]) is { Success: true } quote
            && int.Parse(quote.Groups["number"].Value, CultureInfo.InvariantCulture) == comments.Count + 1 && lines[at + 1].Length > 0)
        {
            var target = quote.Groups["phase"].Success ? int.Parse(quote.Groups["phase"].Value, CultureInfo.InvariantCulture) : Settled;
            var said = new List<string>();
            for (at++; at < lines.Length && lines[at].Length > 0; at++)
            {
                said.Add(lines[at] == ">" ? string.Empty : Unescape(lines[at]));
            }

            comments.Add(new PhaseComment(comments.Count + 1, target, quote.Groups["quote"].Value, string.Join('\n', said)));
            while (at < lines.Length && lines[at].Length == 0)
            {
                at++;
            }
        }

        return (comments, string.Join('\n', lines[at..].Select(Unescape)).Trim());
    }

    [GeneratedRegex(@"^\\*>")]
    private static partial Regex Quoting();

    [GeneratedRegex(@"^\\+>")]
    private static partial Regex Escaped();

    [GeneratedRegex("^> (?<number>[0-9]+) on (?:phase (?<phase>[0-9]+)|settled): \"(?<quote>.*)\"$")]
    private static partial Regex QuoteLine();

    [GeneratedRegex(@"^## phase (?<number>[0-9]+) · (?<who>user|agent) · (?<at>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2})(?: · (?<model>\S+)(?: (?<effort>\S+))?)?$")]
    private static partial Regex Header();
}
