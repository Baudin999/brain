namespace Brain;

/// <summary>
/// One edge at a node, as written. <paramref name="Target"/> is where the edge points, null when it points at no node of
/// the graph; <paramref name="Other"/> is the node at the far end from the one asked, null when there is none.
/// </summary>
internal sealed record Neighbour(WeightedEdge Edge, PlacedNode? Target, PlacedNode? Other);

/// <summary>The one definition of a node's neighbours: its own edges and every edge another node writes into it.</summary>
internal static class Neighbours
{
    /// <summary>The edges out of <paramref name="node"/> and into it, strongest first.</summary>
    public static IReadOnlyList<Neighbour> Of(Graph graph, EdgeWeigher weigher, PlacedNode node)
    {
        var outgoing = node.Node.Edges.Select(edge =>
        {
            var reference = Ref.Parse(edge.Target).Node;
            var target = reference is not null && reference.Problem() is null ? graph.Target(node, reference) : null;
            return new Neighbour(weigher.Weigh(node, edge), target, target);
        });
        var inbound = InboundEdges.Every(graph, other => other.Ref == node.Ref)
            .Select(edge => new Neighbour(weigher.Weigh(edge.From, edge.Edge), node, edge.From));
        return [.. outgoing.Concat(inbound).OrderByDescending(neighbour => neighbour.Edge.Weight)];
    }
}
