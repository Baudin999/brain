namespace Brain.Children;

/// <summary>
/// What a keeper must end before it goes: every process below it now, every process it ever saw below it that still
/// runs as that same process, and every process left in its process group. On Linux the keeper is the subreaper, so the
/// first holds them all. Without a subreaper, as on macOS, an orphan moves to launchd and is no longer below the keeper:
/// the second still holds it once the keeper saw it, its start telling it from a later process given the same pid, and
/// the third holds it when it kept the keeper's group.
/// </summary>
internal sealed class Lineage(int keeper)
{
    private readonly Dictionary<int, string> seen = [];

    /// <summary>Remembers every process below the keeper in <paramref name="tree"/>, with when it started.</summary>
    public void Remember(ProcessTree tree)
    {
        foreach (var pid in tree.Below(keeper))
        {
            if (tree.Start(pid) is { } start)
            {
                seen[pid] = start;
            }
        }
    }

    /// <summary>Every process in <paramref name="tree"/> the keeper must still end, the keeper itself never.</summary>
    public IReadOnlyList<int> Left(ProcessTree tree) =>
        [.. tree.Below(keeper)
            .Concat(seen.Where(process => tree.Start(process.Key) == process.Value).Select(process => process.Key))
            .Concat(tree.InGroup(keeper))
            .Where(pid => pid != keeper)
            .Distinct()];
}
