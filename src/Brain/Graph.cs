using Brain.Staging;

namespace Brain;

/// <summary>Every node of every scope in effect, addressable by ref.</summary>
internal sealed class Graph
{
    private readonly Dictionary<string, PlacedNode> nodes;

    /// <summary>Each node of the scopes in effect as this graph last read or wrote it, to see another command's write.</summary>
    private readonly Dictionary<string, string> read;

    private Graph(IReadOnlyList<Scope> scopes, NamedProjects projects, bool rehearsing, ChangeSet? staged = null)
    {
        Scopes = scopes;
        Projects = projects;
        Rehearsing = rehearsing;
        Staged = staged;
        nodes = scopes
            .SelectMany(scope => NodeStore.All(scope).Select(node => new PlacedNode(scope, node)))
            .ToDictionary(node => node.Ref, StringComparer.Ordinal);
        read = nodes.ToDictionary(pair => pair.Key, pair => NodeRenderer.Render(pair.Value.Node), StringComparer.Ordinal);
    }

    public IReadOnlyList<Scope> Scopes { get; }

    /// <summary>The registered projects that refs like <c>afterlife:wg-19</c> name, outside the scopes in effect.</summary>
    public NamedProjects Projects { get; }

    public IReadOnlyCollection<PlacedNode> Nodes => nodes.Values;

    /// <summary>The nodes of <paramref name="scope"/>, one of the scopes in effect.</summary>
    public List<Node> NodesOf(Scope scope) => [.. nodes.Values.Where(node => node.Scope == scope).Select(node => node.Node)];

    /// <summary>True for a rehearsal: a save changes the graph in memory and writes nothing to disk or to a log.</summary>
    public bool Rehearsing { get; }

    /// <summary>
    /// For a staged graph, the node files its saves and removes would change. A staged graph writes nothing to disk; its
    /// changes become a pending write that lands on the user's yes. Null for a graph that writes.
    /// </summary>
    public ChangeSet? Staged { get; }

    public static Graph Load(BrainEnv env) => new(ScopeResolver.InEffect(env), new NamedProjects(env.Home, env.UserHome), rehearsing: false);

    /// <summary>Personal and global with no project in effect, wherever the command runs: every project is one edges name.</summary>
    public static Graph Brain(BrainEnv env) =>
        new([ScopeResolver.BrainScope(env.Home, ScopeNames.Global), ScopeResolver.BrainScope(env.Home, ScopeNames.Personal)], new NamedProjects(env.Home, env.UserHome), rehearsing: false);

    /// <summary>The graph as Load reads it, for a command to try every write on before it makes any.</summary>
    public static Graph Rehearse(BrainEnv env) => new(ScopeResolver.InEffect(env), new NamedProjects(env.Home, env.UserHome), rehearsing: true);

    /// <summary>The graph as Load reads it, whose writes are recorded in <see cref="Staged"/> instead of made.</summary>
    public static Graph Stage(BrainEnv env) => new(ScopeResolver.InEffect(env), new NamedProjects(env.Home, env.UserHome), rehearsing: false, new ChangeSet());

    /// <summary>A node by a ref typed on the command line. A bare slug searches project, global, personal in turn.</summary>
    public PlacedNode? Find(NodeRef reference) =>
        reference.Scope is null
            ? Scopes.Select(scope => Target(scope, reference)).FirstOrDefault(node => node is not null)
            : Target(holder: null, reference);

    /// <summary>
    /// The nodes of every cloned registered project except the one in effect, whose edges the scopes in effect do not
    /// read. Check and forget read them so an edge into personal or global is judged from any repo.
    /// </summary>
    public IEnumerable<PlacedNode> OtherProjectNodes
    {
        get
        {
            var inEffect = Scopes.FirstOrDefault(scope => scope.Name == ScopeNames.Project)?.Repo;
            return Projects.Cloned()
                .Where(project => inEffect is null || !SameDirectory(project.Root!, inEffect))
                .SelectMany(project => project.Nodes.Values);
        }
    }

    /// <summary>
    /// The node an edge of <paramref name="from"/>, a node of another project, points at. A bare slug or project: stays in
    /// that project, since the project in effect is not its own.
    /// </summary>
    public PlacedNode? TargetFromProject(PlacedNode from, NodeRef reference) =>
        reference.Scope is null || reference.Scope == ScopeNames.Project
            ? Projects.Get(from.Scope.Name).Nodes.GetValueOrDefault(reference.Slug)
            : Target(holder: null, reference);

    /// <summary>The node an edge of <paramref name="from"/> points at. A bare slug stays in from's scope.</summary>
    public PlacedNode? Target(PlacedNode from, NodeRef reference) => Target(from.Scope, reference);

    public PlacedNode Require(string text)
    {
        if (Ref.Parse(text).Node is not { } reference || reference.Problem() is not null)
        {
            throw new BrainException($"{text} is not a node ref");
        }

        return Find(reference) ?? throw new BrainException($"no node {text}");
    }

    /// <summary>Writes the node, refusing one read from an old-format file: the parser placed nothing of it, so a write would empty it.</summary>
    public void Save(PlacedNode node)
    {
        ThrowIfOld(node);
        if (!Rehearsing)
        {
            if (Staged?.Touched(NodeStore.PathOf(node.Scope, node.Node.Slug)) != true)
            {
                ThrowIfChanged(node);
            }

            Write(node.Scope, node.Node);
            read[node.Ref] = NodeRenderer.Render(node.Node);
        }

        nodes[node.Ref] = node;
    }

    /// <summary>Refuses a node read from an old-format file, for a command that must check before its first write.</summary>
    public static void ThrowIfOld(PlacedNode node)
    {
        if (node.Node.Malformed.Contains(NodeParser.OldFormat, StringComparer.Ordinal))
        {
            throw new BrainException($"{node.Ref} is in the old format; run brain migrate before writing it");
        }
    }

    /// <summary>
    /// Refuses to write a node another command changed on disk since this graph read it: several sessions write the same
    /// graph, and a judge call holds a write open for seconds, so the write would silently undo the other change.
    /// </summary>
    private void ThrowIfChanged(PlacedNode node)
    {
        if (read.TryGetValue(node.Ref, out var known)
            && !string.Equals(NodeStore.Current(node.Scope, node.Node.Slug) is { } current ? NodeRenderer.Render(current) : null, known, StringComparison.Ordinal))
        {
            throw new BrainException($"{node.Ref} changed on disk since this command read it; run the command again");
        }
    }

    /// <summary>Deletes the node's file. Its edges go with it; edges into it are the caller's to remove first.</summary>
    public void Remove(PlacedNode node)
    {
        if (Staged is not null)
        {
            Staged.Record(Label(node.Scope, node.Node.Slug), NodeStore.PathOf(node.Scope, node.Node.Slug), after: null);
        }
        else if (!Rehearsing)
        {
            NodeStore.Delete(node.Scope, node.Node.Slug);
        }

        nodes.Remove(node.Ref);
        read.Remove(node.Ref);
    }

    /// <summary>
    /// Writes a node of another project that this graph does not hold, such as one whose edge into a forgotten node goes,
    /// or records the write on a staged graph.
    /// </summary>
    public void WriteOutside(Scope scope, Node node)
    {
        if (!Rehearsing)
        {
            Write(scope, node);
        }
    }

    /// <summary>How a staged change names a node: its ref, and for the project in effect the repo it lives in.</summary>
    private static string Label(Scope scope, string slug) =>
        scope.Name == ScopeNames.Project && scope.Repo is { } repo
            ? $"{NodeRef.Qualified(scope.Name, slug)} in {Path.GetFileName(Path.TrimEndingDirectorySeparator(repo))}"
            : NodeRef.Qualified(scope.Name, slug);

    private void Write(Scope scope, Node node)
    {
        if (Staged is null)
        {
            NodeStore.Write(scope, node);
            return;
        }

        Staged.Record(Label(scope, node.Slug), NodeStore.PathOf(scope, node.Slug), NodeRenderer.Render(node));
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.Ordinal);

    private PlacedNode? Target(Scope? holder, NodeRef reference)
    {
        if (reference.Project is { } project)
        {
            return Projects.Get(project).Nodes.GetValueOrDefault(reference.Slug);
        }

        var scope = reference.Scope ?? holder?.Name;
        return scope is null ? null : nodes.GetValueOrDefault(NodeRef.Qualified(scope, reference.Slug));
    }
}
