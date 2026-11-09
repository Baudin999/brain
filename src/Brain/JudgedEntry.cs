namespace Brain;

/// <summary>
/// One node as the gate reads it: its title, body and why as separate parts. Subjects are the node's search keywords,
/// and Edges the edges it will carry. TitleIsNew is false when a write keeps the title a node already has. NodeWrite is
/// true for add, edit and merge, whose write carries the node's subjects and edges: it needs a subject, and it is
/// refused when it carries no edge while the judge names one. Nearest are the entries nearest to the node and InUse the
/// subjects its scope's nodes carry, which the gate gives it before the judge reads it.
/// </summary>
internal sealed record JudgedEntry(string Scope, string Slug, string Kind, string Title, string Body, string Why)
{
    public string Ref => NodeRef.Qualified(Scope, Slug);

    public IReadOnlyList<string> Subjects { get; init; } = [];

    public IReadOnlyList<Edge> Edges { get; init; } = [];

    public bool TitleIsNew { get; init; } = true;

    public bool NodeWrite { get; init; }

    public IReadOnlyList<PlacedNode> Nearest { get; init; } = [];

    public IReadOnlyList<string> InUse { get; init; } = [];

    /// <summary>A new node as add and merge write it.</summary>
    public static JudgedEntry Of(NewNode spec) =>
        new(spec.Scope, spec.Slug, spec.Kind, spec.Title.Trim(), spec.Body.Trim(), spec.Why.Trim()) { Subjects = spec.Subjects };

    /// <summary>A node as it would be written, for update and edit.</summary>
    public static JudgedEntry Of(PlacedNode node, Node revised) =>
        new(node.Scope.Name, node.Node.Slug, revised.Field(FieldNames.Kind) ?? string.Empty, revised.Title ?? string.Empty, revised.Body, revised.Why)
        {
            Subjects = Brain.Subjects.Of(revised),
            Edges = revised.Edges,
            TitleIsNew = !string.Equals(node.Node.Title, revised.Title, StringComparison.Ordinal),
        };

    /// <summary>The node as the nearest query reads it: its title, body, why and subjects.</summary>
    public Node Document() =>
        new(Slug, Title, [new NodeField(FieldNames.Subjects, Brain.Subjects.Format(Subjects))], Edges, Body, Why, []);
}
