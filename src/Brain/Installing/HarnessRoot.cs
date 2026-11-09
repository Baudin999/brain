using System.Reflection;

namespace Brain.Installing;

/// <summary>
/// A harness that Claude Code's config directory links into: its root on disk and its files, keyed by their path under
/// the root. <see cref="Unpacks"/> holds for the harness a bare binary carries, which brain install writes to its root
/// before it links to it, so its files come from the binary rather than from disk.
/// </summary>
internal sealed record HarnessRoot(string Root, IReadOnlyDictionary<string, byte[]> Files, bool Unpacks)
{
    private const string ResourcePrefix = "harness/";

    /// <summary>
    /// The installed harness: the one a package put beside the binary, or else the one the binary carries, unpacked into
    /// the data repo's gitignored cache/harness, so nothing generated lands in git.
    /// </summary>
    public static HarnessRoot Installed(Installation at, string brainHome) => at.PackagedHarness is { } packaged
        ? At(packaged)
        : new HarnessRoot(Path.Combine(brainHome, "cache", "harness"), Embedded(), Unpacks: true);

    /// <summary>The harness at <paramref name="root"/>, as it stands on disk.</summary>
    public static HarnessRoot At(string root) => new(root, Tree.Read(root), Unpacks: false);

    /// <summary>True when <paramref name="path"/> lies under this harness's root.</summary>
    public bool Holds(string path) => path.StartsWith(Root + "/", StringComparison.Ordinal);

    /// <summary>The harness this binary carries, keyed by its path under harness/.</summary>
    public static SortedDictionary<string, byte[]> Embedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            files[name[ResourcePrefix.Length..].Replace('\\', '/')] = bytes.ToArray();
        }

        return files;
    }
}
