namespace Brain.Code;

/// <summary>The folder a brain code session runs in, as the user types it: <c>.</c>, <c>..</c>, <c>~/x</c> or a full path.</summary>
internal static class Folders
{
    /// <summary>
    /// The full path of <paramref name="typed"/>: <c>~</c> is <paramref name="home"/>, and a relative path is read from
    /// <paramref name="from"/>. A trailing separator goes, so two spellings of one folder compare equal.
    /// </summary>
    public static string Resolve(string typed, string home, string from)
    {
        var path = typed.Trim();
        if (path == "~")
        {
            path = home;
        }
        else if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = Path.Combine(home, path[2..]);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, from));
    }

    /// <summary>Whether <paramref name="typed"/> is spelled as a path, never as an agent's name: it holds a slash or starts with a dot or a tilde.</summary>
    public static bool LooksLikePath(string typed) =>
        typed.Contains('/', StringComparison.Ordinal) || typed.StartsWith('.') || typed.StartsWith('~');
}
