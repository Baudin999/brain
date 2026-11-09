namespace Brain;

/// <summary>The edges of other nodes that point at a set of nodes: about to be forgotten, or shown as a node's inbound edges.</summary>
internal static class InboundEdges
{
    /// <summary>Every edge into the <paramref name="targets"/> from a node of the scopes in effect, or of another cloned project, that is not one of them.</summary>
    public static IReadOnlyList<InboundEdge> Every(Graph graph, Func<PlacedNode, bool> targets) =>
    [
        .. Into(graph.Nodes, graph.Target, targets).Concat(Into(graph.OtherProjectNodes, graph.TargetFromProject, targets))
            .OrderBy(edge => edge.Text, StringComparer.Ordinal),
    ];

    private static IEnumerable<InboundEdge> Into(IEnumerable<PlacedNode> sources, Func<PlacedNode, NodeRef, PlacedNode?> resolve, Func<PlacedNode, bool> targets) =>
        sources
            .Where(from => !targets(from))
            .SelectMany(from => from.Node.Edges
                .Select(edge => new { Edge = edge, Target = Ref.Parse(edge.Target).Node is { } reference ? resolve(from, reference) : null })
                .Where(hit => hit.Target is not null && targets(hit.Target))
                .Select(hit => new InboundEdge(from, hit.Edge, hit.Target!)));
}
