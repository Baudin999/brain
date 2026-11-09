namespace Brain.Commands;

/// <summary>Adds one edge to its source node, once.</summary>
internal static class LinkCommand
{
    public const string Name = "link";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string Linked = "linked ";
    private const string Walked = "edge exists: ";
    private const string Space = " ";
    private const int RelArgument = 1;
    private const int ToArgument = 2;
    private const int ArgumentCount = 3;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var positional = Options.Parse(arguments).Expect(ArgumentCount, Usage);
        var graph = Graph.Load(env);
        var from = graph.Require(positional[0]);
        var rel = positional[RelArgument];
        var to = positional[ToArgument];
        var added = EdgeLinker.Link(graph, from, rel, to);
        env.Out.WriteLine((added ? Linked : Walked) + string.Join(Space, from.Ref, rel, to));
        return ExitCodes.Ok;
    }
}
