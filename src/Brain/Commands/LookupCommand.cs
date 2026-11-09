using Brain.Search;

namespace Brain.Commands;

/// <summary>
/// Finds nodes by their words and by their meaning, ranked by <see cref="Ranking"/>, the engine brain's window searches
/// with too. Each hit prints with its tier, sure or likely; a weak hit is left out, and a query with neither says so in
/// one line. It reads, so it travels no edge, and when the embedding model does not load it says so in one line and
/// answers from keywords alone. <c>--subject</c> keeps only the nodes that carry it; with no words it lists every such
/// node, by ref.
/// </summary>
internal static class LookupCommand
{
    public const string Name = "lookup";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string SubjectOption = "subject";
    private const string NoMatch = "no node matches";
    private const string NoneSure = "no node is sure or likely";
    private const string Gap = "  ";
    private const string TagOpen = " [";
    private const string TagClose = "]";
    private const char Space = ' ';

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, SubjectOption);
        var subject = options.Get(SubjectOption);
        var visible = Visibility(subject);
        var graph = Graph.Load(env);
        if (subject is not null && options.Positional.Count == 0)
        {
            List<PlacedNode> carriers = [.. graph.Nodes.Where(visible).OrderBy(node => node.Ref, StringComparer.Ordinal)];
            return Write(env, carriers, [.. carriers.Select(node => node.Ref + Tag(node.Node) + Gap + node.Node.Title)], NoMatch);
        }

        var ranked = Ranking.Rank(env, graph, visible, string.Join(Space, Terms(options.Positional)));
        if (ranked.KeywordsOnly)
        {
            env.Err.WriteLine($"lookup: keyword hits only: {env.Embedder.Silence}");
        }

        var lines = ranked.Hits.Select(hit => hit.Node.Ref + Gap + hit.Tier.Name() + Tag(hit.Node.Node) + Gap + hit.Node.Node.Title);
        return Write(env, [.. ranked.Hits.Select(hit => hit.Node)], [.. lines], NoneSure);
    }

    /// <summary>
    /// Writes one line per node, or <paramref name="none"/> when there is no node, and returns the refs.
    /// </summary>
    private static int Write(BrainEnv env, List<PlacedNode> nodes, List<string> lines, string none)
    {
        env.Refs?.Return(nodes);
        foreach (var line in lines.DefaultIfEmpty(none))
        {
            env.Out.WriteLine(line);
        }

        return ExitCodes.Ok;
    }

    /// <summary>Every node, or only those that carry <paramref name="subject"/> when one is asked.</summary>
    private static Func<PlacedNode, bool> Visibility(string? subject) =>
        node => subject is null || Subjects.Of(node.Node).Contains(subject, StringComparer.Ordinal);

    /// <summary>An idea's kind in brackets, so the reader sees it is thinking and not what is true now; nothing for information.</summary>
    private static string Tag(Node node) =>
        string.Equals(node.Field(FieldNames.Kind), Kinds.Idea, StringComparison.Ordinal) ? TagOpen + Kinds.Idea + TagClose : string.Empty;

    private static List<string> Terms(IReadOnlyList<string> words)
    {
        var terms = words.SelectMany(word => word.Split(Space, StringSplitOptions.RemoveEmptyEntries)).ToList();
        return terms.Count > 0 ? terms : throw BrainException.Usage(Usage);
    }
}
