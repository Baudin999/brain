using System.Text;

namespace Brain;

/// <summary>Writes a <see cref="Node"/> back as its file, in the one canonical layout.</summary>
internal static class NodeRenderer
{
    public static string Render(Node node)
    {
        var text = new StringBuilder();
        Line(text, NodeSyntax.Fence);
        foreach (var field in node.Fields)
        {
            Line(text, field.Name + NodeSyntax.FieldSeparator + field.Value);
        }

        Line(text, NodeSyntax.Fence);
        Line(text, NodeSyntax.TitlePrefix + node.Title);
        if (node.Body.Length > 0)
        {
            Line(text, string.Empty);
            Line(text, node.Body);
        }

        if (node.Why.Length > 0)
        {
            Line(text, string.Empty);
            Line(text, NodeSyntax.HeadingPrefix + NodeSyntax.WhyHeading);
            Line(text, node.Why);
        }

        Line(text, string.Empty);
        Line(text, NodeSyntax.HeadingPrefix + NodeSyntax.EdgesHeading);
        foreach (var edge in node.Edges)
        {
            Line(text, NodeSyntax.ItemPrefix + edge.Rel + NodeSyntax.Arrow + edge.Target);
        }

        return text.ToString();
    }

    private static void Line(StringBuilder text, string line) => text.Append(line).Append(NodeSyntax.NewLine);
}
