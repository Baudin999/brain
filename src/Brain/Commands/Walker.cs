namespace Brain.Commands;

/// <summary>
/// Breadth-first walk from a node over its neighbours, edges out and in, each hop strongest edge first, never visiting a
/// node twice and showing an edge once. A node of a named project is shown but not walked on, so the walk stays inside
/// the scopes in effect.
/// </summary>
internal sealed class Walker(Graph graph, EdgeWeigher weigher)
{
    public const int MaxHops = 2;

    public IReadOnlyList<WalkStep> Walk(PlacedNode start, int hops)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Ref };
        var shown = new HashSet<(string From, Edge Edge)>();
        var steps = new List<WalkStep>();
        IReadOnlyList<PlacedNode> frontier = [start];
        for (var hop = 1; hop <= hops; hop++)
        {
            var reached = frontier
                .SelectMany(node => Neighbours.Of(graph, weigher, node))
                .Where(neighbour => shown.Add((neighbour.Edge.From.Ref, neighbour.Edge.Edge)))
                .OrderByDescending(neighbour => neighbour.Edge.Weight)
                .Select(neighbour => new WalkStep(hop, neighbour))
                .ToList();
            steps.AddRange(reached);
            frontier = [.. reached.Select(step => step.Neighbour.Other).OfType<PlacedNode>().Where(node => graph.Scopes.Contains(node.Scope) && visited.Add(node.Ref))];
        }

        return steps;
    }
}
