namespace Brain;

/// <summary>The brain's list of projects with a docs/brain, a tsv of name, path, remote and date, one line each.</summary>
internal static class ProjectRegistry
{
    public const string FileName = "projects.tsv";
    public const string Header = "name\tpath\tremote\tdate";
    private const char Tab = '\t';
    private const int Columns = 4;
    private const int PathColumn = 1;
    private const int RemoteColumn = 2;
    private const int DateColumn = 3;

    /// <summary>True when <paramref name="project"/> is registered already; refuses a name or a path taken by another.</summary>
    public static bool Holds(string home, RegisteredProject project)
    {
        var taken = All(home).FirstOrDefault(entry => Same(entry.Name, project.Name) || Same(entry.RepoPath, project.RepoPath));
        if (taken is null)
        {
            return false;
        }

        if (taken == (project with { Remote = taken.Remote, Date = taken.Date }))
        {
            return true;
        }

        throw new BrainException(Same(taken.Name, project.Name)
            ? $"the name {project.Name} is registered for {taken.RepoPath}; pass another name"
            : $"{project.RepoPath} is registered as {taken.Name}; run brain init {taken.Name}");
    }

    public static RegisteredProject? Named(string home, string name) =>
        All(home).FirstOrDefault(entry => Same(entry.Name, name));

    /// <summary>
    /// The name of the project whose repo root is one of <paramref name="paths"/>, compared as full paths with ~ expanded
    /// to <paramref name="userHome"/>; null when none is.
    /// </summary>
    public static string? At(IEnumerable<RegisteredProject> projects, string userHome, params IEnumerable<string> paths)
    {
        var wanted = paths.Select(Full).ToHashSet(StringComparer.Ordinal);
        return projects.FirstOrDefault(project => wanted.Contains(Full(project.Root(userHome))))?.Name;
    }

    /// <summary>The name of the registered project whose checkout, or the main checkout of whose worktree, is <paramref name="repo"/>; null when none is.</summary>
    public static string? NameOf(string home, string userHome, string repo) =>
        At(All(home), userHome, repo, Git.MainCheckout(repo) ?? repo);

    public static void Append(string home, RegisteredProject project)
    {
        var path = Path.Combine(home, FileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Header + NodeSyntax.NewLine);
        }

        var line = string.Join(Tab, project.Name, project.RepoPath, project.Remote, project.Date);
        File.AppendAllText(path, line + NodeSyntax.NewLine);
    }

    public static IEnumerable<RegisteredProject> All(string home)
    {
        var path = Path.Combine(home, FileName);
        return File.Exists(path) ? File.ReadLines(path).Skip(1).Select(Parse).OfType<RegisteredProject>() : [];
    }

    private static RegisteredProject? Parse(string line)
    {
        var cells = line.Split(Tab);
        return cells.Length == Columns
            ? new RegisteredProject(cells[0], cells[PathColumn], cells[RemoteColumn], cells[DateColumn])
            : null;
    }

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
