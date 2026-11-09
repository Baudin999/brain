namespace Brain.Migrating;

/// <summary>
/// Splits an old one-line label at its first ", because " or " because ". The left part is the title, unchanged. The
/// right part is the why, with a capital first letter and a closing full stop. A label without "because" is all title.
/// </summary>
internal static class LabelSplit
{
    private const string Because = " because ";
    private const char Comma = ',';
    private const char FullStop = '.';
    private static readonly char[] Closers = ['.', '!', '?'];

    public static (string Title, string Why) Split(string label)
    {
        var at = label.IndexOf(Because, StringComparison.Ordinal);
        if (at <= 0)
        {
            return (label.Trim(), string.Empty);
        }

        var title = label[..at].TrimEnd();
        title = title.EndsWith(Comma) ? title[..^1].TrimEnd() : title;
        var why = label[(at + Because.Length)..].Trim();
        return why.Length == 0 ? (label.Trim(), string.Empty) : (title, Sentence(why));
    }

    private static string Sentence(string why)
    {
        var capital = char.ToUpperInvariant(why[0]) + why[1..];
        return Closers.Contains(capital[^1]) ? capital : capital + FullStop;
    }
}
