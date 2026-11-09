using System.Globalization;

namespace Brain.Commands;

/// <summary>The weakest edges across every scope in effect, weakest first: the candidates for doubt.</summary>
internal static class FadedCommand
{
    public const string Name = "faded";
    private static readonly string Usage = Help.UsageOf(Name);
    public const int DefaultCount = 10;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var count = Count(Options.Parse(arguments).Positional);
        var graph = Graph.Load(env);
        var weigher = new EdgeWeigher(env.Today);
        foreach (var edge in Weakest(graph.Nodes, weigher, count))
        {
            env.Out.WriteLine($"{edge.WeightText}  {edge.From.Ref} -{edge.Edge.Rel}-> {edge.Edge.Target}");
        }

        return ExitCodes.Ok;
    }

    /// <summary>The <paramref name="count"/> weakest edges of the nodes, weakest first: what counts as faded.</summary>
    public static IEnumerable<WeightedEdge> Weakest(IEnumerable<PlacedNode> nodes, EdgeWeigher weigher, int count) =>
        nodes
            .SelectMany(weigher.Ranked)
            .OrderBy(edge => edge.Weight)
            .ThenBy(edge => edge.From.Ref, StringComparer.Ordinal)
            .Take(count);

    private static int Count(IReadOnlyList<string> positional)
    {
        if (positional.Count == 0)
        {
            return DefaultCount;
        }

        var parsed = int.TryParse(positional[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count);
        return parsed && count > 0 && positional.Count == 1 ? count : throw BrainException.Usage(Usage);
    }
}
