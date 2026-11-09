using Brain.Commands;

namespace Brain;

/// <summary>Everything wrong with one node: its frontmatter, its title and why, the lines that would not parse, and its edges.</summary>
internal static class NodeChecker
{
    public static IEnumerable<string> Problems(Graph graph, PlacedNode node) =>
        IdentityProblems(node.Node)
            .Concat(FieldProblems(node.Node))
            .OfType<string>()
            .Concat(node.Node.Malformed.Select(line => $"cannot place line: {line}"))
            .Concat(node.Node.Edges.SelectMany(edge => EdgeChecker.Problems(graph, node, edge)))
            .Select(problem => $"{node.Ref}: {problem}");

    private static string?[] IdentityProblems(Node node) =>
    [
        Slugs.IsValid(node.Slug) ? null : $"file name {node.Slug} is not a lowercase-hyphenated slug",
        Titles.Problem(node.Title),
    ];

    private static IEnumerable<string?> FieldProblems(Node node) =>
    [
        KindProblem(node.Field(FieldNames.Kind)),
        FieldNames.Namers.Contains(node.Field(FieldNames.NamedBy), StringComparer.Ordinal) ? null : $"{FieldNames.NamedBy} is not one of {string.Join(Separator, FieldNames.Namers)}",
        node.Born is null ? $"{FieldNames.Born} is missing or not {FieldNames.DateFormat}" : null,
        Subjects.Problem(node.Field(FieldNames.Subjects)),
        .. Migration.GoneFields.Where(name => node.Field(name) is not null).Select(name => $"{name} is from the old model; brain migrate drops it"),
    ];

    /// <summary>One of the two kinds. An old kind is an error, so none can hide behind a passing check.</summary>
    private static string? KindProblem(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return $"{FieldNames.Kind} is missing";
        }

        if (Kinds.IsKnown(kind))
        {
            return null;
        }

        return Migration.Moves(kind)
            ? $"{FieldNames.Kind} {kind} is from the old model; brain migrate moves it"
            : $"{FieldNames.Kind} {kind} is not one of {string.Join(Separator, Kinds.All)}";
    }

    private const string Separator = ", ";
}
