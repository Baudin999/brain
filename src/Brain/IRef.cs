namespace Brain;

/// <summary>An edge target or a command-line ref, exactly as written. <see cref="Ref.Parse"/> makes one.</summary>
internal interface IRef
{
    string Text { get; }

    /// <summary>This ref as a node ref, or null when it names something that is not a node.</summary>
    NodeRef? Node { get; }

    /// <summary>Why the ref is malformed, or null when it is well formed.</summary>
    string? Problem();
}
