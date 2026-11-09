namespace Brain.Merge;

/// <summary><c>+ from -rel-> to</c>, applied with link semantics.</summary>
internal sealed record EdgeOp(int LineNumber, string From, string Rel, string To) : MergeOp(LineNumber)
{
    public override int Stage => 1;

    public override string Apply(Graph graph, DateOnly today)
    {
        var added = EdgeLinker.Link(graph, graph.Require(From), Rel, To);
        return added ? $"linked {From} -{Rel}-> {To}" : $"edge exists: {From} -{Rel}-> {To}";
    }
}
