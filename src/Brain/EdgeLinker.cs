namespace Brain;

/// <summary>
/// Adds an edge to a node, once; removes one that exists, also when its target node is gone or its target is a ref no
/// longer accepted, such as a commit. Linking logs no travel: the node's Born date already counts as one, and an edge is
/// travelled only when both its ends are opened.
/// </summary>
internal static class EdgeLinker
{
    /// <summary>Links and says whether the edge is new. A same-scope target is stored as a bare slug.</summary>
    public static bool Link(Graph graph, PlacedNode source, string rel, string to)
    {
        var edge = Checked(graph, source, rel, to);
        var added = !source.Node.Edges.Contains(edge);
        if (added)
        {
            graph.Save(source with { Node = source.Node.WithEdge(edge) });
        }

        return added;
    }

    /// <summary>
    /// Links every edge of a node write to the node <paramref name="reference"/> names, in order, each through
    /// <see cref="Link"/>, so a write's edges land as <c>brain link</c> would land them.
    /// </summary>
    public static void LinkAll(Graph graph, string reference, IEnumerable<Edge> edges)
    {
        foreach (var edge in edges)
        {
            Link(graph, graph.Require(reference), edge.Rel, edge.Target);
        }
    }

    /// <summary>
    /// The edge as <see cref="Link"/> would store it from <paramref name="source"/>, refused as link refuses it, and
    /// nothing written, so a write checks its edges before it is judged or staged.
    /// </summary>
    public static Edge Checked(Graph graph, PlacedNode source, string rel, string to)
    {
        if (Ref.Parse(to).Node is { } node && node.Problem() is null && ProjectTargets.Problem(graph, source, node) is { } refusal)
        {
            throw new BrainException(refusal);
        }

        return Resolve(graph, source, rel, to, dangling: false);
    }

    /// <summary>
    /// Removes the edge line from the source node and refuses an edge the node does not have. An edge into a node that no
    /// longer exists is matched as the source writes it, so a dangling edge can be removed.
    /// </summary>
    public static void Unlink(Graph graph, PlacedNode source, string rel, string to)
    {
        var edge = Resolve(graph, source, rel, to, dangling: true);
        if (!source.Node.Edges.Contains(edge))
        {
            throw new BrainException($"no edge {source.Ref} {rel} {to}");
        }

        graph.Save(source with { Node = source.Node.WithoutEdge(edge) });
    }

    private static Edge Resolve(Graph graph, PlacedNode source, string rel, string to, bool dangling) =>
        Slugs.IsValid(rel)
            ? new Edge(rel, Target(graph, source, to, dangling))
            : throw new BrainException($"relation {rel} is not lowercase-hyphenated");

    /// <summary>
    /// The target as the source writes it. A missing node and a refused typed ref are refused, unless
    /// <paramref name="dangling"/> allows them, so unlink can remove them.
    /// </summary>
    private static string Target(Graph graph, PlacedNode source, string to, bool dangling)
    {
        var reference = Ref.Parse(to);
        if (reference.Node is not { } node)
        {
            return dangling ? reference.Text : Accepted(reference);
        }

        BrainException.ThrowIf(node.Problem());

        if (graph.Target(source, node) is { } target)
        {
            return target.RefFrom(source);
        }

        if (!dangling)
        {
            throw new BrainException($"no node {to}");
        }

        return node.Project is null && string.Equals(node.Scope, source.Scope.Name, StringComparison.Ordinal) ? node.Slug : reference.Text;
    }

    private static string Accepted(IRef reference) =>
        reference.Problem() is { } problem ? throw new BrainException(problem) : reference.Text;
}
