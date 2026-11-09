namespace Brain.Serving;

/// <summary>
/// The upkeep of a log folder in the data repo's <c>perf/</c>, the window's perf log and each workspace's records alike:
/// the folder carries a <c>.gitignore</c> of its own that ignores everything in it, so it stays outside git, and its
/// files go once they were last written more than 14 days ago.
/// </summary>
internal static class PerfFolder
{
    private const string Ignore = ".gitignore";
    private static readonly TimeSpan Kept = TimeSpan.FromDays(14);

    /// <summary>Makes <paramref name="folder"/>, with its .gitignore, when it is not there yet.</summary>
    public static void Make(string folder)
    {
        Directory.CreateDirectory(folder);
        var ignore = Path.Combine(folder, Ignore);
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "*\n");
        }
    }

    /// <summary>Deletes every file of <paramref name="folder"/> ending in <paramref name="extension"/> that is older than 14 days at <paramref name="now"/>.</summary>
    public static void Prune(string folder, string extension, DateTimeOffset now)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var cutoff = now.UtcDateTime - Kept;
        foreach (var file in Directory.EnumerateFiles(folder, "*" + extension))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
