namespace Brain.Merge;

/// <summary>
/// <c>+ scope:slug [a, b]</c>: sets the node's subjects, a node of the same report or one that exists. It applies after
/// the nodes, so a node the report adds can take its subjects.
/// </summary>
internal sealed record SubjectsOp(int LineNumber, string Node, IReadOnlyList<string> Subjects) : MergeOp(LineNumber)
{
    public override int Stage => 1;

    public override string Apply(Graph graph, DateOnly today)
    {
        var node = graph.Require(Node);
        graph.Save(node with { Node = Brain.Subjects.With(node.Node, Subjects) });
        return $"subjects {node.Ref} {Brain.Subjects.Format(Subjects)}";
    }
}
