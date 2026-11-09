namespace Brain;

/// <summary>
/// A node write as add, edit and nearest read it, in the markdown a node file uses: an optional frontmatter between
/// <c>---</c> lines that holds the node's subjects, <c>subjects: [a, b]</c>, and no other field, since the command
/// gives the rest; then the title line, optionally written <c># title</c>, the body, <c>## Why</c> and the why, and
/// <c>## Edges</c> with one <c>- rel -> target</c> line per edge. A write that leaves a part out carries it empty.
/// </summary>
internal sealed record NodeDraft(NodeContent Content, IReadOnlyList<string> Subjects, IReadOnlyList<Edge> Edges)
{
    private const string Slug = "draft";

    /// <summary>
    /// Reads a write, refusing a frontmatter that does not close, a line the node file has no place for and a field
    /// other than subjects. The title line comes first, right after the frontmatter when there is one; a blank one is
    /// an empty title, which the title rule refuses.
    /// </summary>
    public static NodeDraft Parse(string text)
    {
        var lines = text.ReplaceLineEndings(NodeSyntax.NewLine.ToString()).Split(NodeSyntax.NewLine);
        var front = Front(lines);
        var rest = lines[front..];
        if (rest.Length == 0 || rest[0].Trim().Length == 0)
        {
            throw new BrainException(Titles.Problem(string.Empty)!);
        }

        if (!rest[0].StartsWith(NodeSyntax.TitlePrefix, StringComparison.Ordinal))
        {
            rest[0] = NodeSyntax.TitlePrefix + rest[0];
        }

        var head = front == 0 ? [NodeSyntax.Fence, NodeSyntax.Fence] : lines[..front];
        var node = NodeParser.Parse(Slug, string.Join(NodeSyntax.NewLine, [.. head, .. rest]));
        if (node.Malformed.Count > 0)
        {
            throw new BrainException($"cannot read the line {node.Malformed[0]}: a write is the markdown of a node file");
        }

        var foreign = node.Fields.FirstOrDefault(field => !string.Equals(field.Name, FieldNames.Subjects, StringComparison.Ordinal));
        if (foreign is not null)
        {
            throw new BrainException($"the frontmatter of a write holds only {FieldNames.Subjects}, not {foreign.Name}: the command gives the rest");
        }

        var subjects = node.Field(FieldNames.Subjects);
        if (subjects is not null)
        {
            BrainException.ThrowIf(Brain.Subjects.Problem(subjects));
        }

        var content = new NodeContent(node.Title ?? string.Empty, node.Body, node.Why);
        return new NodeDraft(content, Brain.Subjects.Parse(subjects), node.Edges);
    }

    /// <summary>How many lines the frontmatter takes, its two fences included; 0 when the write opens without one.</summary>
    private static int Front(string[] lines)
    {
        if (!IsFence(lines[0]))
        {
            return 0;
        }

        for (var at = 1; at < lines.Length; at++)
        {
            if (IsFence(lines[at]))
            {
                return at + 1;
            }
        }

        throw new BrainException($"the frontmatter of the write has no closing {NodeSyntax.Fence} line");
    }

    private static bool IsFence(string line) => string.Equals(line.TrimEnd(), NodeSyntax.Fence, StringComparison.Ordinal);
}
