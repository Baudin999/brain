using System.Text.RegularExpressions;

namespace Brain.Judging;

/// <summary>
/// Finds a title that repeats a commit subject, compared without case or punctuation: the same words, or one holding
/// the other whole. A held text must be at least three words, so a subject like "first" does not refuse every title.
/// </summary>
internal static partial class CommitEcho
{
    public const int MinWords = 3;
    private const char Space = ' ';

    /// <summary>The subject the title repeats, or null when it repeats none.</summary>
    public static string? Find(string title, IEnumerable<string> subjects)
    {
        var words = Normal(title);
        return subjects.FirstOrDefault(subject => Echoes(words, Normal(subject)));
    }

    /// <summary>The text in lower case, every run of punctuation and space one space, padded so a word match is a substring match.</summary>
    public static string Normal(string text) =>
        Space + NotAWord().Replace(text.ToLowerInvariant(), Space.ToString()).Trim() + Space;

    private static bool Echoes(string title, string subject)
    {
        if (subject.Trim().Length == 0)
        {
            return false;
        }

        var (shorter, longer) = title.Length <= subject.Length ? (title, subject) : (subject, title);
        return string.Equals(title, subject, StringComparison.Ordinal)
            || (Words(shorter) >= MinWords && longer.Contains(shorter, StringComparison.Ordinal));
    }

    private static int Words(string normal) => normal.Split(Space, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotAWord();
}
