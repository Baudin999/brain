namespace Brain.Installing;

/// <summary>
/// A new, empty data repo: graph/personal, graph/global and voices, each kept by a .gitkeep, a .gitignore that keeps
/// the derived caches out, and git init. An install creates one only where nothing stands, so a data repo cloned there
/// first is kept and used.
/// </summary>
internal static class DataRepo
{
    private static readonly string[] Dirs = ["graph/personal", "graph/global", "voices"];
    private const string Ignore = "# Derived caches only. The text is the brain; everything here is rebuilt from it.\ncache/\n";

    public static void Create(string home)
    {
        foreach (var dir in Dirs)
        {
            Directory.CreateDirectory(Path.Combine(home, dir));
            File.WriteAllText(Path.Combine(home, dir, ".gitkeep"), string.Empty);
        }

        File.WriteAllText(Path.Combine(home, ".gitignore"), Ignore);
        Git.Init(home);
    }
}
