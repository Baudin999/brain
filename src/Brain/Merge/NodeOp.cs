namespace Brain.Merge;

/// <summary>
/// <c>+ scope:slug | title | kind | named-by | why</c>, the why optional. An existing node is skipped, not refused.
/// </summary>
internal sealed record NodeOp(int LineNumber, NewNode Spec) : MergeOp(LineNumber)
{
    public override int Stage => 0;

    public override string Apply(Graph graph, DateOnly today)
    {
        var name = NodeRef.Qualified(Spec.Scope, Spec.Slug);
        if (NodeCreator.Exists(graph, Spec))
        {
            return $"skipped {name}: it exists";
        }

        NodeCreator.Create(graph, Spec, today);
        return $"added {name}";
    }
}
