namespace Brain.Commands;

/// <summary>Removes one edge from its source node. Every other line stays; git keeps the old edge.</summary>
internal static class UnlinkCommand
{
    public const string Name = "unlink";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string Unlinked = "unlinked ";
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
        EdgeLinker.Unlink(graph, from, rel, to);
        env.Out.WriteLine(Unlinked + string.Join(Space, from.Ref, rel, to));
        return ExitCodes.Ok;
    }
}
