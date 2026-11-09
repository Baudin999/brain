namespace Brain;

/// <summary>
/// Reads a node file line by line into a <see cref="Node"/>: the frontmatter between two <c>---</c> lines, the
/// <c># title</c>, the body up to <c>## Why</c> or <c>## Edges</c>, the why, and the edges. A file without frontmatter is
/// in the old format, and the parser places nothing of it.
/// </summary>
internal sealed class NodeParser
{
    public const string OldFormat = "the file has no frontmatter: it is in the old format, run brain migrate";

    private readonly List<NodeField> fields = [];
    private readonly List<Edge> edges = [];
    private readonly List<string> body = [];
    private readonly List<string> why = [];
    private readonly List<string> malformed = [];
    private string? title;
    private Part part = Part.Start;

    private enum Part
    {
        Start,
        Front,
        Head,
        Body,
        Why,
        Edges,
        Old,
    }

    public static Node Parse(string slug, string text)
    {
        var parser = new NodeParser();
        foreach (var line in text.ReplaceLineEndings(NodeSyntax.NewLine.ToString()).Split(NodeSyntax.NewLine))
        {
            parser.Read(line);
        }

        return parser.Build(slug);
    }

    private void Read(string line)
    {
        switch (part)
        {
            case Part.Start:
                ReadStart(line);
                break;
            case Part.Front:
                ReadField(line);
                break;
            case Part.Head:
                ReadHead(line.Trim());
                break;
            case Part.Body:
                ReadText(line, body);
                break;
            case Part.Why:
                ReadText(line, why);
                break;
            case Part.Edges:
                ReadEdge(line);
                break;
            case Part.Old:
                break;
        }
    }

    private void ReadStart(string line)
    {
        if (string.Equals(line.TrimEnd(), NodeSyntax.Fence, StringComparison.Ordinal))
        {
            part = Part.Front;
        }
        else if (line.Trim().Length > 0)
        {
            malformed.Add(OldFormat);
            part = Part.Old;
        }
    }

    private void ReadField(string line)
    {
        if (string.Equals(line.TrimEnd(), NodeSyntax.Fence, StringComparison.Ordinal))
        {
            part = Part.Head;
        }
        else if (TrySplit(line, NodeSyntax.FieldSeparator, out var name, out var value))
        {
            fields.Add(new NodeField(name, value));
        }
        else if (line.Trim().Length > 0)
        {
            malformed.Add(line);
        }
    }

    private void ReadHead(string line)
    {
        if (line.StartsWith(NodeSyntax.TitlePrefix, StringComparison.Ordinal))
        {
            title = line[NodeSyntax.TitlePrefix.Length..].Trim();
            part = Part.Body;
        }
        else if (!Opens(line) && line.Length > 0)
        {
            malformed.Add(line);
        }
    }

    /// <summary>Moves to the section the line opens and says so. The why comes once, before the edges.</summary>
    private bool Opens(string line)
    {
        if (part != Part.Why && NodeSyntax.IsHeading(line, NodeSyntax.WhyHeading))
        {
            part = Part.Why;
            return true;
        }

        if (NodeSyntax.IsHeading(line, NodeSyntax.EdgesHeading))
        {
            part = Part.Edges;
            return true;
        }

        return false;
    }

    private void ReadText(string line, List<string> text)
    {
        if (!Opens(line))
        {
            text.Add(line.TrimEnd());
        }
    }

    private void ReadEdge(string line)
    {
        if (line.StartsWith(NodeSyntax.ItemPrefix, StringComparison.Ordinal)
            && TrySplit(line[NodeSyntax.ItemPrefix.Length..], NodeSyntax.Arrow, out var rel, out var target))
        {
            edges.Add(new Edge(rel, target));
        }
        else if (line.Trim().Length > 0)
        {
            malformed.Add(line.Trim());
        }
    }

    private static bool TrySplit(string item, string separator, out string name, out string value)
    {
        var at = item.IndexOf(separator, StringComparison.Ordinal);
        name = at > 0 ? item[..at].Trim() : string.Empty;
        value = at > 0 ? item[(at + separator.Length)..].Trim() : string.Empty;
        return name.Length > 0 && value.Length > 0;
    }

    private Node Build(string slug) => new(
        slug,
        title,
        fields,
        edges,
        string.Join(NodeSyntax.NewLine, body).Trim(),
        string.Join(NodeSyntax.NewLine, why).Trim(),
        malformed);
}
