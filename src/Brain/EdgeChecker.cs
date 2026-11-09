namespace Brain;

/// <summary>Everything wrong with one edge: its relation, its ref syntax, and whether its target exists.</summary>
internal static class EdgeChecker
{
    public static IEnumerable<string> Problems(Graph graph, PlacedNode from, Edge edge)
    {
        if (!Slugs.IsValid(edge.Rel))
        {
            yield return $"relation {edge.Rel} is not lowercase-hyphenated";
        }

        var reference = Ref.Parse(edge.Target);
        var problem = reference.Problem() ?? Unresolved(graph, from, reference);
        if (problem is not null)
        {
            yield return problem;
        }
    }

    private static string? Unresolved(Graph graph, PlacedNode from, IRef reference) =>
        reference.Node is { } node ? ProjectTargets.Problem(graph, from, node) ?? MissingNode(graph, from, node) : null;

    /// <summary>A target in a scope not in effect here, or in a project not cloned here, cannot be judged here, so it passes.</summary>
    private static string? MissingNode(Graph graph, PlacedNode from, NodeRef node)
    {
        var judged = node.Project is { } project ? graph.Projects.Get(project).Cloned : node.Scope is null || graph.Scopes.Named(node.Scope) is not null;
        return judged && graph.Target(from, node) is null ? $"edge target {node.Text} does not exist" : null;
    }

    /// <summary>
    /// The edges of a node of another project whose target is missing. Only existence is judged: the project's own check
    /// judges the rest. A target in a project not cloned here cannot be judged, so it passes.
    /// </summary>
    public static IEnumerable<string> Dangling(Graph graph, PlacedNode from) =>
        from.Node.Edges
            .Where(edge => Ref.Parse(edge.Target).Node is { } node
                && (node.Project is null || graph.Projects.Get(node.Project).Cloned)
                && graph.TargetFromProject(from, node) is null)
            .Select(edge => $"{from.Ref}: edge target {edge.Target} does not exist");
}
