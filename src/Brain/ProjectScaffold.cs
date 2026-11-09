namespace Brain;

/// <summary>Lays out a project's docs/brain: the graph. Its edge log lives in the brain's cache, never in the repo.</summary>
internal static class ProjectScaffold
{
    private const string KeepFile = ".gitkeep";

    public static string Root(string repo) => Path.Combine(repo, Layout.ProjectDocsDir, Layout.ProjectBrainDir);

    /// <summary>The folder a repo's project nodes are in: docs/brain/graph.</summary>
    public static string GraphDir(string repo) => Path.Combine(Root(repo), Layout.GraphDir);

    /// <summary>Creates the graph directory when it is missing; true when it did.</summary>
    public static bool Create(string repo)
    {
        var graph = GraphDir(repo);
        if (Directory.Exists(graph))
        {
            return false;
        }

        Directory.CreateDirectory(graph);
        File.WriteAllText(Path.Combine(graph, KeepFile), string.Empty);
        return true;
    }
}
