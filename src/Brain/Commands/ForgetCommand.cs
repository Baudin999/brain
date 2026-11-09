using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// Removes nodes that were never a lasting subject, and every edge in the scopes in effect that points at one, all or
/// nothing. A missing node refuses the whole call. Git keeps the old files; the traversal log keeps its rows, which no
/// edge reads any more, and search drops the nodes from its index on its next refresh. From the command line the forget waits
/// as a pending write, showing each node's full text, for the user's yes.
/// </summary>
internal static class ForgetCommand
{
    public const string Name = "forget";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var positional = Options.Parse(arguments).Positional;
        if (positional.Count == 0)
        {
            throw BrainException.Usage(Usage);
        }

        return WriteGate.Run(env, (graph, output) => ForgetPlan.Make(graph, [.. positional.Select(graph.Require)]).Apply(graph, output));
    }
}
