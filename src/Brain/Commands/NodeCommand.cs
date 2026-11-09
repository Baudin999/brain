namespace Brain.Commands;

/// <summary>
/// Rung 1: a node's title, frontmatter fields, body, why and weighted edges, strongest first.
/// Opening a node travels each of its edges whose other end was opened earlier in the same session.
/// </summary>
internal static class NodeCommand
{
    public const string Name = "node";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string Gap = "  ";
    private const string FieldJoin = " | ";
    private const string EdgeIndent = "  ";
    private const string Arrow = " -> ";
    private const string WhyPrefix = NodeParts.Why + NodeSyntax.FieldSeparator;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var positional = Options.Parse(arguments).Expect(1, Usage);
        var graph = Graph.Load(env);
        var node = graph.Require(positional[0]);
        env.Out.WriteLine(node.Ref + Gap + node.Node.Title);
        env.Out.WriteLine(string.Join(FieldJoin, node.Node.Fields.Select(field => field.Name + NodeSyntax.FieldSeparator + field.Value)));
        if (node.Node.Body.Length > 0)
        {
            env.Out.WriteLine(node.Node.Body);
        }

        if (node.Node.Why.Length > 0)
        {
            env.Out.WriteLine(WhyPrefix + node.Node.Why.ReplaceLineEndings(Gap));
        }

        foreach (var edge in new EdgeWeigher(env.Today).Ranked(node))
        {
            env.Out.WriteLine(EdgeIndent + edge.WeightText + Gap + edge.Edge.Rel + Arrow + edge.Edge.Target);
        }

        Travels.Opened(env, graph, node);
        env.Refs?.Open(node);
        return ExitCodes.Ok;
    }
}
