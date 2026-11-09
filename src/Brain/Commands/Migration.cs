namespace Brain.Commands;

/// <summary>
/// Moves a node to the two kinds. A decision in phase idea, and a concept, lesson or principle from before the four
/// kinds, becomes an idea; every other decision, goal, tool, person, phase or project becomes information. The phase and
/// superseded-by fields and every commit: edge go: git holds what happened, and a node that changed is edited in place.
/// Title, body and why stay as they are. Check fails any kind migrate cannot map, and lists information without a why
/// for the user and the brain to fix.
/// </summary>
internal static class Migration
{
    public const string Phase = "phase";
    public const string SupersededBy = "superseded-by";

    /// <summary>The fields of the old model, which migrate drops and check fails.</summary>
    public static readonly IReadOnlyList<string> GoneFields = [Phase, SupersededBy];

    private const string IdeaPhase = "idea";
    private const string OldDecision = "decision";

    private static readonly Dictionary<string, string> NewKinds = new(StringComparer.Ordinal)
    {
        [OldDecision] = Kinds.Information,
        ["goal"] = Kinds.Information,
        ["tool"] = Kinds.Information,
        ["person"] = Kinds.Information,
        ["phase"] = Kinds.Information,
        ["project"] = Kinds.Information,
        ["concept"] = Kinds.Idea,
        ["lesson"] = Kinds.Idea,
        ["principle"] = Kinds.Idea,
    };

    /// <summary>True for an old kind migrate moves to one of the two.</summary>
    public static bool Moves(string kind) => NewKinds.ContainsKey(kind);

    /// <summary>The node in the new model, or null when it already is or its kind is one migrate cannot map.</summary>
    public static Node? ToModel(Node node)
    {
        var kind = node.Field(FieldNames.Kind) ?? string.Empty;
        var moved = KindOf(kind, node.Field(Phase));
        var commits = node.Edges.Where(IsCommit).ToList();
        var gone = GoneFields.Where(name => node.Field(name) is not null).ToList();
        if (moved is null && commits.Count == 0 && gone.Count == 0)
        {
            return null;
        }

        var revised = gone.Aggregate(node, (current, name) => current.WithoutField(name)) with { Edges = [.. node.Edges.Except(commits)] };
        return moved is null ? revised : revised.WithField(new NodeField(FieldNames.Kind, moved));
    }

    /// <summary>The new kind of an old one, or null for a current kind or one migrate cannot map.</summary>
    private static string? KindOf(string kind, string? phase)
    {
        if (string.Equals(kind, OldDecision, StringComparison.Ordinal) && string.Equals(phase, IdeaPhase, StringComparison.Ordinal))
        {
            return Kinds.Idea;
        }

        return NewKinds.GetValueOrDefault(kind);
    }

    private static bool IsCommit(Edge edge) =>
        Ref.Parse(edge.Target) is TypedRef typed && string.Equals(typed.Kind, TypedRefKinds.Commit, StringComparison.Ordinal);

    /// <summary>The migration lines for check: none when every piece of information has its why.</summary>
    public static IEnumerable<string> Lines(Graph graph)
    {
        var whyless = graph.Nodes
            .Where(node => Kinds.NeedsWhy(node.Node.Field(FieldNames.Kind)) && node.Node.Why.Length == 0)
            .Select(node => node.Ref)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (whyless.Count > 0)
        {
            yield return $"migration: {whyless.Count} information nodes have no why: {string.Join(Separator, whyless)}";
        }
    }

    private const string Separator = ", ";
}
