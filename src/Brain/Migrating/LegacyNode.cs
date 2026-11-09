namespace Brain.Migrating;

/// <summary>
/// Reads a node file in the old format: <c># slug</c>, a one-line label, <c>- Name: value</c> fields, <c>## Edges</c>
/// and an optional <c>## Notes</c>. It gives the node in the new format: the label split into title and why, the fields
/// in lower case with an empty subjects list after the kind and phase, and the notes as the body.
/// </summary>
internal sealed class LegacyNode
{
    private const string NotesHeading = "Notes";

    private readonly List<NodeField> fields = [];
    private readonly List<Edge> edges = [];
    private readonly List<string> notes = [];
    private readonly List<string> malformed = [];
    private bool titled;
    private string? label;
    private string section = string.Empty;

    /// <summary>True for a file already in the new format, which migration leaves alone.</summary>
    public static bool IsCurrent(string text) =>
        text.TrimStart().StartsWith(NodeSyntax.Fence, StringComparison.Ordinal);

    /// <summary>The old file as a new node and its old label, or a refusal naming the lines it could not place.</summary>
    public static (Node Node, string Label) Convert(string slug, string text)
    {
        var reader = Reader(text);
        if (reader.malformed.Count > 0 || reader.label is null)
        {
            throw new BrainException($"{slug}: cannot migrate, the old file has no label or lines it cannot place: {string.Join(LineJoin, reader.malformed)}");
        }

        return (reader.Build(slug), reader.label);
    }

    /// <summary>A file of either format as a node, for reading an old version from git; nothing it holds is refused.</summary>
    public static Node Read(string slug, string text) =>
        IsCurrent(text) ? NodeParser.Parse(slug, text) : Reader(text).Build(slug);

    private const string LineJoin = " | ";

    private static LegacyNode Reader(string text)
    {
        var reader = new LegacyNode();
        foreach (var line in text.ReplaceLineEndings(NodeSyntax.NewLine.ToString()).Split(NodeSyntax.NewLine))
        {
            reader.Read(line);
        }

        return reader;
    }

    private Node Build(string slug)
    {
        var (title, why) = LabelSplit.Split(label ?? string.Empty);
        return new Node(slug, title, Fields(), edges, string.Join(NodeSyntax.NewLine, notes).Trim(), why, malformed);
    }

    private void Read(string line)
    {
        if (string.Equals(section, NotesHeading, StringComparison.Ordinal))
        {
            notes.Add(line.TrimEnd());
        }
        else if (line.StartsWith(NodeSyntax.HeadingPrefix, StringComparison.Ordinal))
        {
            section = line[NodeSyntax.HeadingPrefix.Length..].Trim();
        }
        else if (!titled && line.StartsWith(NodeSyntax.TitlePrefix, StringComparison.Ordinal))
        {
            titled = true;
        }
        else if (line.StartsWith(NodeSyntax.ItemPrefix, StringComparison.Ordinal))
        {
            ReadItem(line, line[NodeSyntax.ItemPrefix.Length..]);
        }
        else if (line.Trim().Length > 0)
        {
            ReadProse(line.Trim());
        }
    }

    private void ReadProse(string text)
    {
        if (titled && label is null && section.Length == 0)
        {
            label = text;
        }
        else
        {
            malformed.Add(text);
        }
    }

    private void ReadItem(string line, string item)
    {
        if (string.Equals(section, NodeSyntax.EdgesHeading, StringComparison.Ordinal) && Split(item, NodeSyntax.Arrow) is { } edge)
        {
            edges.Add(new Edge(edge.Name, edge.Value));
        }
        else if (section.Length == 0 && Split(item, NodeSyntax.FieldSeparator) is { } field)
        {
            fields.Add(new NodeField(field.Name.ToLowerInvariant(), field.Value));
        }
        else
        {
            malformed.Add(line);
        }
    }

    /// <summary>The fields in their old order, with an empty subjects list right after the kind and the phase.</summary>
    private List<NodeField> Fields()
    {
        var at = fields.FindLastIndex(field => field.Name is FieldNames.Kind or Commands.Migration.Phase) + 1;
        return [.. fields.Take(at), new NodeField(FieldNames.Subjects, Subjects.Empty), .. fields.Skip(at)];
    }

    private static NodeField? Split(string item, string separator)
    {
        var at = item.IndexOf(separator, StringComparison.Ordinal);
        var name = at > 0 ? item[..at].Trim() : string.Empty;
        var value = at > 0 ? item[(at + separator.Length)..].Trim() : string.Empty;
        return name.Length > 0 && value.Length > 0 ? new NodeField(name, value) : null;
    }
}
