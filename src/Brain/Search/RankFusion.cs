namespace Brain.Search;

/// <summary>
/// Reciprocal rank fusion: a node scores 1 / (60 + rank) in every ranking that holds it, and the sums order the result.
/// A node several rankings found rises above one only one found; one ranking alone keeps its own order.
/// </summary>
internal static class RankFusion
{
    private const double Damping = 60;

    /// <summary>Every node of the rankings with its summed score, by ref.</summary>
    public static Dictionary<string, (PlacedNode Node, double Score)> Fuse(IEnumerable<IReadOnlyList<PlacedNode>> rankings) =>
        rankings
            .SelectMany(ranking => ranking.Select((node, rank) => (Node: node, Score: 1 / (Damping + rank + 1))))
            .GroupBy(hit => hit.Node.Ref, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (group.First().Node, group.Sum(hit => hit.Score)), StringComparer.Ordinal);
}
