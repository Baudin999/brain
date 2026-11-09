using System.Globalization;

namespace Brain;

/// <summary>
/// One node file, parsed: its frontmatter fields, the title, the body in prose, the why, and its edges. Malformed holds
/// the lines the parser could not place, so check can report them and nothing is silently lost.
/// </summary>
internal sealed record Node(
    string Slug,
    string? Title,
    IReadOnlyList<NodeField> Fields,
    IReadOnlyList<Edge> Edges,
    string Body,
    string Why,
    IReadOnlyList<string> Malformed)
{
    public string? Field(string name) =>
        Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))?.Value;

    public bool NamedByUser => string.Equals(Field(FieldNames.NamedBy), FieldNames.User, StringComparison.Ordinal);

    public DateOnly? Born =>
        DateOnly.TryParseExact(Field(FieldNames.Born), FieldNames.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var born)
            ? born
            : null;

    public Node WithEdge(Edge edge) => this with { Edges = [.. Edges, edge] };

    public Node WithoutEdge(Edge edge) => this with { Edges = [.. Edges.Where(kept => kept != edge)] };

    /// <summary>Sets a frontmatter field: in place when the node has it, appended when it does not.</summary>
    public Node WithField(NodeField field) =>
        this with
        {
            Fields = Field(field.Name) is null
                ? [.. Fields, field]
                : [.. Fields.Select(old => string.Equals(old.Name, field.Name, StringComparison.Ordinal) ? field : old)],
        };

    public Node WithoutField(string name) =>
        this with { Fields = [.. Fields.Where(field => !string.Equals(field.Name, name, StringComparison.Ordinal))] };
}
