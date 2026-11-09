using System.Text.RegularExpressions;

namespace Brain;

/// <summary>
/// The rung-1 title: one line of at most 25 words. The limit is a ratchet: it only ever goes down. A body and a why may
/// be any length, but neither may hold a line that opens a section of the node file.
/// </summary>
internal static partial class Titles
{
    public const int MaxWords = 25;

    public static int Words(string title) => Word().Count(title);

    /// <summary>Why the title is unfit, or null when it fits.</summary>
    public static string? Problem(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"title is empty";
        }

        if (title.Contains(NodeSyntax.NewLine, StringComparison.Ordinal))
        {
            return $"title is more than one line";
        }

        var words = Words(title);
        return words > MaxWords ? $"title has {words} words, the limit is {MaxWords}" : null;
    }

    /// <summary>Why a body or a why cannot be written, or null when it can.</summary>
    public static string? TextProblem(string part, string text) =>
        text.Split(NodeSyntax.NewLine).FirstOrDefault(NodeSyntax.IsSection) is { } line
            ? $"the {part} holds the line \"{line.Trim()}\", which opens a section of the node file"
            : null;

    [GeneratedRegex(@"\S+")]
    private static partial Regex Word();
}
