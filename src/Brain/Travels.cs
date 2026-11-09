namespace Brain;

/// <summary>
/// An edge is travelled when both its ends are opened with <c>brain node</c> in one Claude Code session: showing an
/// edge is not travelling it, because not every edge leads somewhere. Each edge counts once per session, and only in
/// a scope in effect, so opening a node never writes the log of a scope not in effect. Without a session nothing is recorded.
/// </summary>
internal static class Travels
{
    private const char Tab = '\t';

    public static void Opened(BrainEnv env, Graph graph, PlacedNode node)
    {
        if (env.Session is null)
        {
            return;
        }

        using var trail = SessionTrail.Open(SessionTrail.PathIn(env.Home), env.Session);
        var touching = Neighbours.Of(graph, new EdgeWeigher(env.Today), node)
            .Where(end => end.Other is not null && graph.Scopes.Contains(end.Edge.From.Scope) && trail.WasOpened(end.Other.Location))
            .ToList();
        trail.MarkOpened(node.Location);
        foreach (var neighbour in touching)
        {
            var (from, edge) = (neighbour.Edge.From, neighbour.Edge.Edge);
            if (trail.MarkTravelled(string.Join(Tab, from.Scope.LogPath, from.Ref, edge.Rel, edge.Target)))
            {
                EdgeLog.Travel(from, edge, env.Today);
            }
        }
    }
}
