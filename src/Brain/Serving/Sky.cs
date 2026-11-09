using Brain.Commands;
using Brain.Search;

namespace Brain.Serving;

/// <summary>
/// The constellation on the window's Home: every node and every edge of personal, global and every cloned project in
/// projects.tsv, as one sky. Each graph is a cluster, its nodes placed by <see cref="SkyLayout"/> in the order their
/// files entered git. An edge joins the two nodes the graph resolves it to, in one cluster or across two, and weighs what
/// <see cref="EdgeWeigher"/> says today. It reads only: nothing here records a travel, so looking at the sky or searching
/// it never strengthens an edge.
/// </summary>
internal sealed class Sky
{
    private readonly BrainEnv env;
    private readonly Graph graph;
    private readonly List<SkyCluster> clusters;
    private readonly List<PlacedNode> nodes;
    private readonly List<SkyEdge> edges;

    private Sky(BrainEnv env, Graph graph, List<SkyCluster> clusters, List<PlacedNode> nodes, List<SkyEdge> edges)
    {
        this.env = env;
        this.graph = graph;
        this.clusters = clusters;
        this.nodes = nodes;
        this.edges = edges;
    }

    /// <summary>The scope of every graph in the sky.</summary>
    public IReadOnlyList<Scope> Scopes => [.. clusters.Select(cluster => cluster.Scope)];

    /// <summary>Every node of every graph in the sky.</summary>
    public IReadOnlyList<PlacedNode> Nodes => nodes;

    /// <summary>Reads every graph: personal, global, then each cloned project in projects.tsv order.</summary>
    public static Sky Load(BrainEnv env)
    {
        var graph = Graph.Brain(env);
        var projects = graph.Projects.Cloned().ToList();
        List<SkyCluster> clusters =
        [
            .. graph.Scopes.OrderByDescending(scope => scope.Name == ScopeNames.Personal).Select(scope => new SkyCluster(scope, scope.Name, null)),
            .. projects.Select(project => new SkyCluster(project.Scope!, ScopeNames.Project, ReviewViews.Hue(project.Name))),
        ];
        List<PlacedNode> nodes = [.. graph.Nodes.Concat(projects.SelectMany(project => project.Nodes.Values)).OrderBy(node => node.Ref, StringComparer.Ordinal)];
        var at = nodes.Select((node, index) => (node.Ref, index)).ToDictionary(pair => pair.Ref, pair => pair.index, StringComparer.Ordinal);
        var weigher = new EdgeWeigher(env.Today);
        var faded = FadedCommand.Weakest(graph.Nodes, weigher, FadedCommand.DefaultCount).Select(edge => (edge.From.Ref, edge.Edge)).ToHashSet();
        List<SkyEdge> edges = [];
        for (var from = 0; from < nodes.Count; from++)
        {
            foreach (var edge in nodes[from].Node.Edges)
            {
                if (Target(graph, nodes[from], edge.Target) is { } target && at.TryGetValue(target.Ref, out var to) && to != from)
                {
                    edges.Add(new SkyEdge(from, to, edge.Rel, weigher.Weigh(nodes[from], edge).Weight, weigher.TravelledToday(nodes[from], edge), faded.Contains((nodes[from].Ref, edge))));
                }
            }
        }

        return new Sky(env, graph, clusters, nodes, edges);
    }

    /// <summary>The sky as the window draws it: its clusters, its nodes where the layout placed them, and its edges.</summary>
    public SkyView View()
    {
        var cluster = clusters.Select((one, index) => (one.Scope.Name, index)).ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        var members = nodes.Select(node => cluster[node.Scope.Name]).ToList();
        // Every edge a node holds counts, also one into a project not cloned here or a ref that names no node, though only
        // an edge with both ends in the sky is drawn.
        var degree = nodes.Select(node => node.Node.Edges.Count).ToArray();
        foreach (var edge in edges)
        {
            degree[edge.To]++;
        }

        var (entered, beside) = Entries();
        var placed = SkyLayout.Place(
            [.. clusters.Select(one => new SkyLayout.Group(one.Tier, members.Count(member => member == cluster[one.Scope.Name])))],
            [.. nodes.Select((node, index) => new SkyLayout.Star(node.Ref, members[index], entered[index]))],
            beside,
            [.. edges.Select(edge => (edge.From, edge.To))]);
        return new SkyView(
            [.. clusters.Select((one, index) => new SkyClusterView(one.Scope.Name, one.Tier, one.Hue, Round(placed.Groups[index].X), Round(placed.Groups[index].Y), Round(placed.Groups[index].Radius), one.Tier == ScopeNames.Global ? Round(placed.Inner) : 0))],
            [.. nodes.Select((node, index) => new SkyNodeView(
                node.Ref,
                node.Node.Title,
                node.Scope.Name,
                node.Node.Field(FieldNames.Kind),
                string.IsNullOrWhiteSpace(node.Node.Body) && string.IsNullOrWhiteSpace(node.Node.Why),
                members[index],
                degree[index],
                node.Node.Born == env.Today,
                Round(placed.Stars[index].X),
                Round(placed.Stars[index].Y)))],
            [.. edges.Select(edge => new SkyEdgeView(edge.From, edge.To, edge.Rel, Math.Round(edge.Weight, 2), edge.Travelled, edge.Faded))]);
    }

    /// <summary>
    /// The search behind <c>brain lookup</c> and the window's search box, over every graph of the sky, a hit's
    /// neighbours rising by the sky's own edges. It gives every sure or likely node and not lookup's top hits, so Home
    /// lights how far a search spreads.
    /// </summary>
    public SearchResult Search(string words)
    {
        if (string.IsNullOrWhiteSpace(words))
        {
            return new SearchResult([], KeywordsOnly: null);
        }

        var at = nodes.Select((node, index) => (node.Ref, index)).ToDictionary(pair => pair.Ref, pair => pair.index, StringComparer.Ordinal);
        var ranked = Ranking.Spread(env, Scopes, nodes, words, hit => Neighbours(at[hit.Ref]));
        return new SearchResult(
            [.. ranked.Hits.Select(SearchHit.Of)],
            ranked.KeywordsOnly ? env.Embedder.Silence : null);
    }

    private IEnumerable<(PlacedNode Node, double Weight)> Neighbours(int index) =>
        edges.Where(edge => edge.From == index || edge.To == index).Select(edge => (nodes[edge.From == index ? edge.To : edge.From], edge.Weight));

    /// <summary>
    /// When each node entered its graph, and the edges it entered with, as <see cref="EntryOrder"/> reads them from the
    /// repo its cluster lives in. Personal and global share the brain's repo, so they share one count. A node not committed
    /// yet comes after every committed one, by born date then ref. A node with no commit that reads as a node, uncommitted
    /// or never moved out of the old format, is placed by the edges it holds now.
    /// </summary>
    private (int[] Entered, List<(int From, int To)> Beside) Entries()
    {
        var at = nodes.Select((node, index) => (node.Ref, index)).ToDictionary(pair => pair.Ref, pair => pair.index, StringComparer.Ordinal);
        var entered = new int[nodes.Count];
        var beside = new List<(int From, int To)>();
        foreach (var root in nodes.Select((node, index) => (Root: Root(node.Scope), Index: index)).GroupBy(pair => pair.Root, StringComparer.Ordinal))
        {
            var order = EntryOrder.Of(env.Home, root.Key);
            var next = order.Count == 0 ? 0 : order.Values.Max(entry => entry.Place) + 1;
            var waiting = new List<int>();
            foreach (var (_, index) in root)
            {
                var node = nodes[index];
                var entry = order.GetValueOrDefault(Path.GetRelativePath(root.Key, NodeStore.PathOf(node.Scope, node.Node.Slug)));
                if (entry is not null)
                {
                    entered[index] = entry.Place;
                }
                else
                {
                    waiting.Add(index);
                }

                foreach (var target in entry?.Edges ?? [.. node.Node.Edges.Select(edge => edge.Target)])
                {
                    if (Target(graph, node, target) is { } other && at.TryGetValue(other.Ref, out var to) && to != index)
                    {
                        beside.Add((index, to));
                    }
                }
            }

            foreach (var index in waiting.OrderBy(index => nodes[index].Node.Born ?? DateOnly.MaxValue).ThenBy(index => nodes[index].Ref, StringComparer.Ordinal))
            {
                entered[index] = next++;
            }
        }

        return (entered, beside);
    }

    /// <summary>The directory whose history orders a scope's nodes: the brain's graph for personal and global, a project's graph for its own.</summary>
    private static string Root(Scope scope) => scope.Repo is null ? Path.GetDirectoryName(scope.GraphDir)! : scope.GraphDir;

    /// <summary>The node an edge of <paramref name="from"/> to <paramref name="target"/> points at, as the graph resolves it: a project's bare slug stays in that project.</summary>
    private static PlacedNode? Target(Graph graph, PlacedNode from, string target)
    {
        if (Ref.Parse(target).Node is not { } reference || reference.Problem() is not null)
        {
            return null;
        }

        return from.Scope.Repo is null ? graph.Target(from, reference) : graph.TargetFromProject(from, reference);
    }

    private static double Round(double value) => Math.Round(value, 1);

    /// <summary>One graph of the sky: its scope, its tier, and a project's hue.</summary>
    private sealed record SkyCluster(Scope Scope, string Tier, int? Hue);

    /// <summary>An edge between two nodes of the sky, by their place in its node list, its weight today, whether it was last travelled today and whether <c>brain faded</c> lists it.</summary>
    private sealed record SkyEdge(int From, int To, string Rel, double Weight, bool Travelled, bool Faded);
}

/// <summary>The constellation: the clusters, the nodes and the edges, an edge naming its ends by their place in the node list.</summary>
internal sealed record SkyView(IReadOnlyList<SkyClusterView> Clusters, IReadOnlyList<SkyNodeView> Nodes, IReadOnlyList<SkyEdgeView> Edges);

/// <summary>A graph of the sky: its name, its tier (personal, global or project), a project's hue, where it lies, and for the ring how far out its inner edge lies, 0 for a disc.</summary>
internal sealed record SkyClusterView(string Name, string Tier, int? Hue, double X, double Y, double Radius, double Inner);

/// <summary>A node of the sky: its ref, title, scope and kind, whether it holds only a title, its cluster, how many edges touch it, whether it was born today, and where it lies.</summary>
internal sealed record SkyNodeView(string Ref, string? Title, string Scope, string? Kind, bool Bare, int Cluster, int Degree, bool Born, double X, double Y);

/// <summary>An edge of the sky: its ends by their place in the node list, its relation, its weight today, whether it was last travelled today and whether <c>brain faded</c> lists it.</summary>
internal sealed record SkyEdgeView(int From, int To, string Rel, double Weight, bool Travelled, bool Faded);
