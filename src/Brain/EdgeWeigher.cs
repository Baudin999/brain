namespace Brain;

/// <summary>
/// Weighs edges from the edge logs: an edge weighs its folded travels decayed to today, plus the node's Born date as
/// one implicit travel, and a node the user named counts double. Weights are computed on read; the log keeps only
/// each edge's travels folded as of its last one.
/// </summary>
internal sealed class EdgeWeigher(DateOnly today)
{
    private readonly Dictionary<string, Dictionary<string, FoldedEdge>> logs = new(StringComparer.Ordinal);

    /// <summary>The node's edges, strongest first.</summary>
    public IReadOnlyList<WeightedEdge> Ranked(PlacedNode node) =>
        [.. node.Node.Edges.Select(edge => Weigh(node, edge)).OrderByDescending(edge => edge.Weight)];

    public WeightedEdge Weigh(PlacedNode from, Edge edge)
    {
        var travelled = Folded(from, edge)?.On(today) ?? 0;
        var born = from.Node.Born is { } date ? Decay.Of(date, today) : 0;
        var sum = born + travelled;
        return new WeightedEdge(from, edge, from.Node.NamedByUser ? sum * Decay.UserFactor : sum);
    }

    /// <summary>Whether the edge's last travel in its log is today.</summary>
    public bool TravelledToday(PlacedNode from, Edge edge) =>
        Folded(from, edge)?.AsOf == today;

    private FoldedEdge? Folded(PlacedNode from, Edge edge) =>
        Log(from.Scope.LogPath).GetValueOrDefault(EdgeLog.Key(Logged(from), edge.Rel, edge.Target));

    /// <summary>
    /// The node's ref as its edge log names it. A project's travels are logged from inside its repo, where its scope is
    /// named project, so a node read under the project's name, as in studio:cups, is weighed under project:cups.
    /// </summary>
    private static string Logged(PlacedNode node) =>
        node.Scope.Repo is null ? node.Ref : NodeRef.Qualified(ScopeNames.Project, node.Node.Slug);

    private Dictionary<string, FoldedEdge> Log(string logPath)
    {
        if (!logs.TryGetValue(logPath, out var edges))
        {
            edges = EdgeLog.Read(logPath);
            logs[logPath] = edges;
        }

        return edges;
    }
}
