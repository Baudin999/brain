using System.Globalization;

namespace Brain.Commands;

/// <summary>Rung 2: the neighbours' titles within a 4000 character budget. It travels no edge: showing an edge is not travelling it.</summary>
internal static class WalkCommand
{
    public const string Name = "walk";
    public const int CharacterBudget = 4000;
    private static readonly string Usage = Help.UsageOf(Name);
    private const string HopsOption = "hops";
    private const string Gap = "  ";
    private const string Arrow = " -> ";
    private const string Space = " ";
    private const string Missing = "(missing)";
    private const string LeftOutSuffix = " more left out";
    private const string LeftOutPrefix = "... ";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, HopsOption);
        var positional = options.Expect(1, Usage);
        var hops = Hops(options.Get(HopsOption));
        var graph = Graph.Load(env);
        var start = graph.Require(positional[0]);
        env.Refs?.Open(start);
        var budget = new Budget(CharacterBudget);
        budget.Offer(start.Ref + Gap + start.Node.Title);
        foreach (var step in new Walker(graph, new EdgeWeigher(env.Today)).Walk(start, hops))
        {
            budget.Offer(Line(step));
        }

        Print(env.Out, budget);
        return ExitCodes.Ok;
    }

    private static void Print(TextWriter output, Budget budget)
    {
        foreach (var line in budget.Kept)
        {
            output.WriteLine(line);
        }

        if (budget.LeftOut > 0)
        {
            output.WriteLine(LeftOutPrefix + budget.LeftOut.ToString(CultureInfo.InvariantCulture) + LeftOutSuffix);
        }
    }

    private static int Hops(string? text)
    {
        if (text is null)
        {
            return 1;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var hops) && hops is >= 1 and <= Walker.MaxHops
            ? hops
            : throw new BrainException("--hops must be 1 or 2");
    }

    private static string Line(WalkStep step)
    {
        var indent = string.Concat(Enumerable.Repeat(Gap, step.Hop));
        var neighbour = step.Neighbour;
        var edge = neighbour.Edge;
        var target = neighbour.Target is null ? edge.Edge.Target : neighbour.Target.Ref;
        var title = neighbour.Other?.Node.Title ?? (Ref.Parse(edge.Edge.Target) is NodeRef ? Missing : string.Empty);
        return (indent + edge.WeightText + Gap + edge.From.Ref + Space + edge.Edge.Rel + Arrow + target + Gap + title).TrimEnd();
    }
}
