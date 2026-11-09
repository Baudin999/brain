namespace Brain.Commands;

/// <summary>
/// Validates every node of every scope in effect. Every cloned project besides the one in effect is read for edges whose
/// target does not exist. Each problem goes to stderr and makes the exit 1. Information without a
/// why is a migration line on stderr, never an error.
/// </summary>
internal static class CheckCommand
{
    public const string Name = "check";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Help.UsageOf(Name));
        var graph = Graph.Load(env);
        var problems = graph.Nodes.SelectMany(node => NodeChecker.Problems(graph, node)).Concat(graph.OtherProjectNodes.SelectMany(node => EdgeChecker.Dangling(graph, node))).ToList();
        foreach (var problem in problems)
        {
            env.Err.WriteLine(problem);
        }

        foreach (var warning in graph.Nodes.SelectMany(node => node.Node.Edges.Select(edge => ProjectTargets.Warning(graph, node, edge))).OfType<string>())
        {
            env.Err.WriteLine(Warning + warning);
        }

        foreach (var migration in Migration.Lines(graph))
        {
            env.Err.WriteLine(migration);
        }

        var edges = graph.Nodes.Sum(node => node.Node.Edges.Count);
        var scopes = string.Join(Separator, graph.Scopes.Select(scope => scope.Name));
        env.Out.WriteLine($"check: {graph.Nodes.Count} nodes, {edges} edges, {problems.Count} errors in {scopes}");
        return problems.Count == 0 ? ExitCodes.Ok : ExitCodes.Failure;
    }

    private const string Separator = ", ";
    private const string Warning = "warning: ";
}
