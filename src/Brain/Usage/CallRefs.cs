namespace Brain.Usage;

/// <summary>
/// The refs one call hands back, gathered while it runs for its usage line: the nodes a lookup or a nearest returned
/// and the nodes a node or a walk opened. A ref names a node the same from every directory: a project node by its
/// projects.tsv name, so two projects' nodes never share a ref and a lookup in one directory meets the node it opened.
/// </summary>
internal sealed class CallRefs(string home, string userHome)
{
    private readonly List<string> returned = [];
    private readonly List<OpenedNode> opened = [];
    private readonly Dictionary<string, string> projectNames = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Returned => returned;

    public IReadOnlyList<OpenedNode> Opened => opened;

    public void Return(IEnumerable<PlacedNode> nodes) => returned.AddRange(nodes.Select(Ref));

    public void Open(PlacedNode node) =>
        opened.Add(new OpenedNode(Ref(node), node.Node.Field(FieldNames.Born), node.Node.Field(FieldNames.Session)));

    private string Ref(PlacedNode node)
    {
        if (node.Scope.Repo is not { } repo)
        {
            return node.Ref;
        }

        if (!projectNames.TryGetValue(repo, out var name))
        {
            name = ProjectRegistry.NameOf(home, userHome, repo) ?? ScopeNames.Project;
            projectNames[repo] = name;
        }

        return NodeRef.Qualified(name, node.Node.Slug);
    }
}
