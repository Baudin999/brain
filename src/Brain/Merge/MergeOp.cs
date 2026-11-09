namespace Brain.Merge;

/// <summary>One <c>+</c> line of a worker report, ready to apply. Nodes apply before edges.</summary>
internal abstract record MergeOp(int LineNumber)
{
    public abstract int Stage { get; }

    /// <summary>Applies the line and says what happened, or throws a <see cref="BrainException"/> saying why not.</summary>
    public abstract string Apply(Graph graph, DateOnly today);
}
