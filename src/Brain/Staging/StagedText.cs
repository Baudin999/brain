using System.Text;

namespace Brain.Staging;

/// <summary>
/// The text a command prints when it stages a write: what the command says it does, then the full text of every node it
/// writes or forgets, exactly as the file will read or as it read. A node whose edges alone change, such as one that
/// loses its edge into a forgotten node, shows as the command's line about it.
/// </summary>
internal static class StagedText
{
    private const string Indent = "  ";

    public static string Of(IReadOnlyList<string> lines, IReadOnlyList<StagedChange> changes)
    {
        var text = new StringBuilder();
        text.Append("brain asks before it writes:").Append(NodeSyntax.NewLine);
        foreach (var line in lines)
        {
            text.Append(Indent).Append(line).Append(NodeSyntax.NewLine);
        }

        foreach (var change in changes.Where(Shows))
        {
            var (state, shown) = change switch
            {
                { Before: null } => ("new", change.After!),
                { After: null } => ("forgotten", change.Before),
                _ => ("rewritten", change.After),
            };
            text.Append(NodeSyntax.NewLine).Append($"{change.Ref}, {state}:").Append(NodeSyntax.NewLine).Append(shown);
            if (!shown.EndsWith(NodeSyntax.NewLine))
            {
                text.Append(NodeSyntax.NewLine);
            }
        }

        return text.ToString();
    }

    /// <summary>True unless the change keeps everything of the node but its edges.</summary>
    public static bool Shows(StagedChange change) =>
        change.Before is null || change.After is null || !string.Equals(WithoutEdges(change, change.Before), WithoutEdges(change, change.After), StringComparison.Ordinal);

    private static string WithoutEdges(StagedChange change, string text) =>
        NodeRenderer.Render(NodeParser.Parse(Path.GetFileNameWithoutExtension(change.Path), text) with { Edges = [] });
}
