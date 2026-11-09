using System.Globalization;

namespace Brain.Commands;

/// <summary>
/// The subjects in use. <c>subjects [scope]</c> lists each subject the nodes of the scopes in effect carry, or of the one
/// named, a registered project included, with how many nodes carry it, so a writer reuses a word in use.
/// </summary>
internal static class SubjectsCommand
{
    public const string Name = "subjects";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string Gap = "  ";
    private const int CountWidth = 3;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var positional = Options.Parse(arguments).Positional;
        if (positional.Count > 1)
        {
            throw BrainException.Usage(Usage);
        }

        var graph = Graph.Load(env);
        foreach (var (scope, nodes) in Listed(graph, positional.Count == 1 ? positional[0] : null))
        {
            List(env.Out, scope, nodes);
        }

        return ExitCodes.Ok;
    }

    /// <summary>The scopes to list with their nodes: every scope in effect, or the one named.</summary>
    private static List<(Scope Scope, List<Node> Nodes)> Listed(Graph graph, string? name)
    {
        if (name is null)
        {
            return [.. graph.Scopes.Select(scope => (scope, graph.NodesOf(scope)))];
        }

        if (graph.Scopes.Named(name) is { } inEffect)
        {
            return [(inEffect, graph.NodesOf(inEffect))];
        }

        var project = graph.Projects.Get(name);
        if (!project.Cloned)
        {
            throw new BrainException($"scope {name} is not in effect here and names no project cloned here");
        }

        var nodes = project.Nodes.Values.Select(node => node.Node).ToList();
        return [(project.Scope!, nodes)];
    }

    private static void List(TextWriter output, Scope scope, List<Node> nodes)
    {
        var inUse = Subjects.InUse(nodes);
        var noun = inUse.Count == 1 ? "subject" : "subjects";
        output.WriteLine($"{scope.Name}: {inUse.Count} {noun} in use");
        var width = inUse.Select(use => use.Subject.Length).DefaultIfEmpty(0).Max();
        foreach (var (subject, count) in inUse)
        {
            var counted = count.ToString(CultureInfo.InvariantCulture).PadLeft(CountWidth);
            output.WriteLine(Gap + subject.PadRight(width) + Gap + counted);
        }
    }
}
