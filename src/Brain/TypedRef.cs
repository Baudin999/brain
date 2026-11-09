namespace Brain;

/// <summary>A ref to something that is not a node: a session, file or url. A commit is read so it can be refused.</summary>
internal sealed record TypedRef(string Text, string Kind, string Value) : IRef
{
    public NodeRef? Node => null;

    /// <summary>Why the ref cannot be an edge target, or null when it can. Git holds the commits, so no edge points at one.</summary>
    public string? Problem() =>
        string.Equals(Kind, TypedRefKinds.Commit, StringComparison.Ordinal)
            ? $"{Text}: a commit is not an edge target; git holds what happened"
            : TypedRefKinds.Problem(Kind, Value);
}
