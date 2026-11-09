using Brain.Staging;

namespace Brain.Search;

/// <summary>
/// The entries nearest to a node, one query behind every node write the command line stages, <c>brain nearest</c> and
/// brain's window, so a duplicate or a conflict is seen before the write lands. The query is the whole node, ranked by
/// <see cref="Ranking.Nearest"/>. Which graphs it reads follows the user's tiers, who I am above how we work above a
/// project, by the scope the node lands in: a project node meets that project, global and personal; a personal or
/// global node meets personal, global and every cloned project in projects.tsv. The node's own file never comes back.
/// It reads only, and a project node comes back under its projects.tsv name, a ref any directory can open.
/// </summary>
internal static class Nearest
{
    /// <summary>How many entries the command line shows per node.</summary>
    public const int Shown = 5;

    /// <summary>The entries nearest to <paramref name="node"/>, as it lands in <paramref name="scope"/>, best first.</summary>
    public static Ranked<Found> To(BrainEnv env, Scope scope, Node node, int limit)
    {
        var scopes = Tier(env, scope);
        List<PlacedNode> nodes = [.. scopes.SelectMany(tier => NodeStore.All(tier).Select(found => new PlacedNode(tier, found)))];
        var own = Full(NodeStore.PathOf(scope, node.Slug));
        return Ranking.Nearest(env, scopes, nodes, found => !string.Equals(Full(NodeStore.PathOf(found.Scope, found.Node.Slug)), own, StringComparison.Ordinal), node, limit);
    }

    /// <summary>
    /// Writes the nearest entries as rung 0 lines under a heading that names <paramref name="about"/>: ref, title and
    /// scope, one entry a line. Without the embedding model the heading says the entries are keyword hits only.
    /// </summary>
    public static void Print(BrainEnv env, string about, Ranked<Found> ranked)
    {
        var keywords = ranked.KeywordsOnly ? $", by keywords only: {env.Embedder.Silence}" : string.Empty;
        env.Out.WriteLine($"nearest to {about}{keywords}:");
        foreach (var hit in ranked.Hits)
        {
            env.Out.WriteLine($"  {hit.Node.Ref}  {hit.Node.Node.Title}  ({hit.Node.Scope.Name})");
        }

        if (ranked.Hits.Count == 0)
        {
            env.Out.WriteLine("  no node is near");
        }
    }

    /// <summary>
    /// The entries nearest to the node a staged change leaves, or null when the change asks none: a forget, a change of
    /// its edges, fields or subjects alone, or a file in no graph of this brain.
    /// </summary>
    public static Ranked<Found>? ToChange(BrainEnv env, StagedChange change, int limit)
    {
        if (change.After is null || ScopeOf(env, change.Path) is not { } scope)
        {
            return null;
        }

        var slug = Path.GetFileNameWithoutExtension(change.Path);
        var after = NodeParser.Parse(slug, change.After);
        return change.Before is not null && NodeContent.Of(NodeParser.Parse(slug, change.Before)) == NodeContent.Of(after) ? null : To(env, scope, after, limit);
    }

    /// <summary>The scope whose graph holds <paramref name="path"/>: personal, global, or a project's docs/brain/graph.</summary>
    public static Scope? ScopeOf(BrainEnv env, string path)
    {
        var directory = Full(Path.GetDirectoryName(path) ?? string.Empty);
        if (new[] { ScopeNames.Personal, ScopeNames.Global }.Select(name => ScopeResolver.BrainScope(env.Home, name)).FirstOrDefault(scope => Full(scope.GraphDir) == directory) is { } brain)
        {
            return brain;
        }

        var repo = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(directory)));
        return repo is not null && Full(ScopeResolver.ForRepo(env.Home, ScopeNames.Project, repo).GraphDir) == directory ? Named(env, repo) : null;
    }

    /// <summary>The graphs a node landing in <paramref name="scope"/> is read against, its own first.</summary>
    private static List<Scope> Tier(BrainEnv env, Scope scope)
    {
        var personal = ScopeResolver.BrainScope(env.Home, ScopeNames.Personal);
        var global = ScopeResolver.BrainScope(env.Home, ScopeNames.Global);
        if (scope.Repo is { } repo)
        {
            return [Named(env, repo), global, personal];
        }

        var projects = new NamedProjects(env.Home, env.UserHome).Cloned().Select(project => project.Scope!);
        return [personal, global, .. projects];
    }

    /// <summary>The project scope of <paramref name="repo"/> under its projects.tsv name, or under project when nobody registered it.</summary>
    private static Scope Named(BrainEnv env, string repo) =>
        ScopeResolver.ForRepo(env.Home, ProjectRegistry.NameOf(env.Home, env.UserHome, repo) ?? ScopeNames.Project, repo);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
