namespace Brain.Code;

/// <summary>
/// The build brain code's server runs, and the newer one it reloads on. <c>./build.sh</c>, which <c>./install.sh</c>
/// runs, writes <see cref="StampFile"/> beside the binary once a publish is whole: the server keeps the stamp it started
/// with, and a newer build is ready once the folder holds a stamp of other source; an empty one is still being written. A build that carries no stamp, as a
/// package's, never says a newer one is ready.
/// </summary>
/// <param name="folder">The folder the server's binary runs from.</param>
internal sealed class CodeBuild(string folder)
{
    public const string StampFile = "source.stamp";

    private readonly string? ran = Stamp(folder);

    /// <summary>True once a build of other source than this server's was published whole beside it.</summary>
    public bool Newer()
    {
        var now = Stamp(folder);
        if (string.IsNullOrEmpty(now))
        {
            return false;
        }

        return now != ran;
    }

    /// <summary>
    /// Watches the folder for a stamp written: each write goes to <paramref name="published"/>, until the watch is
    /// disposed. Nothing is watched when the folder is not there.
    /// </summary>
    public IDisposable? Watch(Action published)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var watcher = new FileSystemWatcher(folder, StampFile) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
        watcher.Created += (_, _) => published();
        watcher.Changed += (_, _) => published();
        watcher.Renamed += (_, _) => published();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static string? Stamp(string folder)
    {
        try
        {
            return File.ReadAllText(Path.Combine(folder, StampFile)).Trim();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
