namespace Brain.Staging;

/// <summary>
/// The node files one command would change, recorded instead of written. Each file keeps its text on disk from when the
/// command first touched it and the last text the command gave it, so a node added and then retagged in one merge is one
/// change.
/// </summary>
internal sealed class ChangeSet
{
    private readonly Dictionary<string, StagedChange> changes = new(StringComparer.Ordinal);
    private readonly List<string> order = [];

    /// <summary>Every file whose text the command changed, in the order it first touched them.</summary>
    public IReadOnlyList<StagedChange> Changes =>
        [.. order.Select(path => changes[path]).Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal))];

    public bool Touched(string path) => changes.ContainsKey(path);

    /// <summary>Records that the command leaves <paramref name="after"/> at <paramref name="path"/>, null to delete it.</summary>
    public void Record(string reference, string path, string? after)
    {
        if (changes.TryGetValue(path, out var known))
        {
            changes[path] = known with { After = after };
            return;
        }

        changes[path] = new StagedChange(reference, path, File.Exists(path) ? File.ReadAllText(path) : null, after);
        order.Add(path);
    }
}
