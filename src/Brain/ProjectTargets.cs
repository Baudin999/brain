namespace Brain;

/// <summary>
/// The rules for node refs that cross into a project. <c>project:</c> means the repo of the working directory, so only
/// project nodes may use it; personal and global nodes name the project, and that name must be registered.
/// </summary>
internal static class ProjectTargets
{
    /// <summary>Why <paramref name="from"/> may not hold <paramref name="node"/>, or null when it may.</summary>
    public static string? Problem(Graph graph, PlacedNode from, NodeRef node)
    {
        if (node.Project is { } name)
        {
            return graph.Projects.Get(name).Registered ? null : $"edge target {node.Text}: {name} is neither a ref kind nor a registered project";
        }

        var crosses = from.Scope.Name != ScopeNames.Project && node.Scope == ScopeNames.Project;
        return crosses ? $"a {from.Scope.Name} node may not target {node.Text}; name the project from projects.tsv: <name>:{node.Slug}" : null;
    }

    /// <summary>A warning when <paramref name="edge"/> names a registered project that is not cloned here, or null.</summary>
    public static string? Warning(Graph graph, PlacedNode from, Edge edge)
    {
        if (Ref.Parse(edge.Target).Node is not { Project: { } name })
        {
            return null;
        }

        var project = graph.Projects.Get(name);
        return project.Registered && !project.Cloned ? $"{from.Ref}: {edge.Target}: project {name} is not cloned at {project.Root}" : null;
    }
}
