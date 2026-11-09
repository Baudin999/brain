using System.Text;

namespace Brain.Installing;

/// <summary>
/// What one target becomes: a link to <see cref="LinkTo"/>, or the <see cref="Files"/> written there. For a link,
/// the files are what the link shows, so the diff reads the same either way. <see cref="Loss"/> is set where a target of
/// the user's own is theirs to replace or keep, and says what brain lacks when they keep it. <see cref="Unpacks"/> marks
/// the harness a bare binary unpacks for the links to read: what stands there is brain's, and is overwritten.
/// </summary>
internal sealed record Placement(string Target, string? LinkTo, IReadOnlyDictionary<string, byte[]> Files, string? Loss = null, bool Unpacks = false)
{
    public static Placement Link(string target, string source, IReadOnlyDictionary<string, byte[]> files, string? loss = null) => new(target, source, files, loss);

    /// <summary>A link to <paramref name="source"/>, showing the files that stand there now.</summary>
    public static Placement Link(string target, string source) => Link(target, source, Tree.Read(source));

    public static Placement Copy(string target, IReadOnlyDictionary<string, byte[]> files, string? loss = null) => new(target, null, files, loss);

    public static Placement Unpack(HarnessRoot harness) => new(harness.Root, null, harness.Files, Unpacks: true);

    /// <summary>True when the target already is what this placement makes it.</summary>
    public bool InPlace => LinkTo is null
        ? Tree.Exists(Target) && !Tree.IsLink(Target) && Tree.Hash(Tree.Read(Target)) == Tree.Hash(Files)
        : string.Equals(Tree.LinkTarget(Target), LinkTo, StringComparison.Ordinal);

    /// <summary>The detail of its made line in the install record: the link's source, or the hash of what was written.</summary>
    public string Detail => LinkTo ?? Manifest.HashPrefix + Tree.Hash(Files);

    /// <summary>
    /// What the install shows for it: what it becomes and what stood there, in plain words, then the diff of every file
    /// it changes. A file where nothing stood shows whole. A new unpacked harness shows no diff: the links that read it
    /// show its files. <paramref name="brains"/> says whether what stands there now is brain's.
    /// </summary>
    public IEnumerable<string> Shown(bool brains)
    {
        yield return Heading(brains);
        if (Unpacks && !Tree.Exists(Target))
        {
            yield break;
        }

        var before = Tree.Read(Target);
        foreach (var key in before.Keys.Union(Files.Keys).Order(StringComparer.Ordinal))
        {
            var label = key.Length == 0 ? Target : Path.Combine(Target, key);
            foreach (var line in LineDiff.Unified(label, Text(before, key), Text(Files, key)))
            {
                yield return "    " + line;
            }
        }
    }

    /// <summary>Puts it in place, where nothing stands any more.</summary>
    public void Make()
    {
        if (LinkTo is null)
        {
            Tree.Write(Target, Files);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        File.CreateSymbolicLink(Target, LinkTo);
    }

    private string Heading(bool brains)
    {
        var was = Tree.LinkTarget(Target);
        if (Unpacks)
        {
            return Tree.Exists(Target)
                ? $"replace {Target}: the harness this binary carries, unpacked again over the one there, edits included"
                : $"new     {Target}: the harness this binary carries, unpacked for the links below";
        }

        if (!Tree.Exists(Target))
        {
            return LinkTo is null ? $"new     {Target}" : $"new     {Target} -> {LinkTo}";
        }

        if (LinkTo is null)
        {
            return was is null ? $"replace {Target}" : $"replace {Target}: the link to {was} becomes a file";
        }

        if (was is not null)
        {
            return $"relink  {Target}: it links to {was} now, and will link to {LinkTo}";
        }

        var kind = Directory.Exists(Target) ? "directory" : "file";
        return brains
            ? $"replace {Target}: brain's copy there becomes a link to {LinkTo}"
            : $"replace {Target}: your {kind} there becomes a link to {LinkTo}";
    }

    private static string? Text(IReadOnlyDictionary<string, byte[]> files, string key) =>
        files.TryGetValue(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
