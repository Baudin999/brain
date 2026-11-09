namespace Brain;

/// <summary>A checked forget: the nodes to remove and every edge in the scopes in effect and in other cloned projects that points at one of them.</summary>
internal sealed class ForgetPlan
{
    private const string Indent = "  ";

    private readonly IReadOnlyList<PlacedNode> forgotten;
    private readonly IReadOnlyList<InboundEdge> inbound;

    private ForgetPlan(IReadOnlyList<PlacedNode> forgotten, IReadOnlyList<InboundEdge> inbound)
    {
        this.forgotten = forgotten;
        this.inbound = inbound;
    }

    /// <summary>Every edge the forget removes, written the way faded writes an edge, for a confirm step to show first.</summary>
    public IReadOnlyList<string> Edges => [.. inbound.Select(edge => edge.Text)];

    /// <summary>Refuses a node outside the scopes in effect, whose inbound edges cannot all be seen from here.</summary>
    public static ForgetPlan Make(Graph graph, IReadOnlyList<PlacedNode> nodes)
    {
        var forgotten = nodes.DistinctBy(node => node.Location, StringComparer.Ordinal).OrderBy(node => node.Ref, StringComparer.Ordinal).ToList();
        var locations = forgotten.Select(node => node.Location).ToHashSet(StringComparer.Ordinal);
        var outside = forgotten.FirstOrDefault(node => !graph.Scopes.Contains(node.Scope));
        if (outside is not null)
        {
            throw new BrainException($"{outside.Ref} is outside the scopes in effect; forget it from inside its repo");
        }

        return new ForgetPlan(forgotten, InboundEdges.Every(graph, node => locations.Contains(node.Location)));
    }

    /// <summary>
    /// Removes every inbound edge from its node, then each forgotten node's file, and says what went. Each source node is
    /// read from the graph again, so a node saved after the plan was made keeps that save.
    /// </summary>
    public void Apply(Graph graph, TextWriter output)
    {
        foreach (var from in inbound.GroupBy(edge => edge.From.Ref, StringComparer.Ordinal))
        {
            var source = graph.Nodes.FirstOrDefault(node => string.Equals(node.Ref, from.Key, StringComparison.Ordinal));
            if (source is null)
            {
                Unlink(graph, from.First().From, from);
                continue;
            }

            graph.Save(source with { Node = from.Aggregate(source.Node, (node, edge) => node.WithoutEdge(edge.Edge)) });
        }

        foreach (var node in forgotten)
        {
            graph.Remove(node);
            output.WriteLine($"forgot {node.Ref}");
            foreach (var edge in inbound.Where(edge => string.Equals(edge.Target.Location, node.Location, StringComparison.Ordinal)))
            {
                output.WriteLine($"{Indent}unlinked {edge.Text}");
            }
        }

        foreach (var repo in inbound.Select(edge => edge.From.Scope).Where(scope => scope.Repo is not null && !ScopeNames.IsScope(scope.Name)).Select(scope => scope.Repo!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            output.WriteLine($"changed {ProjectScaffold.Root(repo)}; commit it");
        }
    }

    /// <summary>Removes the edges from a node of another project, read from its file again, and names the repo whose docs/brain changed.</summary>
    private static void Unlink(Graph graph, PlacedNode from, IEnumerable<InboundEdge> edges)
    {
        if (graph.Rehearsing || NodeStore.Current(from.Scope, from.Node.Slug) is not { } current)
        {
            return;
        }

        graph.WriteOutside(from.Scope, edges.Aggregate(current, (node, edge) => node.WithoutEdge(edge.Edge)));
    }
}
