namespace Brain.Installing;

/// <summary>One line of the install record: its kind, the target and the detail.</summary>
internal sealed record ManifestLine(string Kind, string Target, string Detail);

/// <summary>
/// The install record, backups/brain-install.tsv in Claude Code's config directory: one tab-separated line per target.
/// A made line names a target brain made, and its detail says how to undo it: the link's source, <c>sha256:</c> and the
/// hash of the file or directory brain wrote, <c>downloaded</c> for the model, <c>created</c> for a directory that did not
/// exist before brain put something in it, or <c>generated</c> for a settings.json an install.sh before brain install
/// wrote. A saved line names a target that was yours, and its detail is where it was backed up; a target
/// backed up by several installs has a saved line for each, the first one what stood there before brain.
/// <c>brain uninstall</c> reads it to put everything back.
/// </summary>
internal sealed class Manifest(string path)
{
    public const string Made = "made";
    public const string Saved = "saved";
    public const string Downloaded = "downloaded";
    public const string Generated = "generated";
    public const string Created = "created";
    public const string HashPrefix = "sha256:";
    private const char Tab = '\t';

    public string Path { get; } = path;

    public IReadOnlyList<ManifestLine> Lines() => !File.Exists(Path)
        ? []
        : [.. File.ReadAllLines(Path)
            .Select(line => line.Split(Tab))
            .Where(fields => fields.Length == 3)
            .Select(fields => new ManifestLine(fields[0], fields[1], fields[2]))];

    /// <summary>
    /// True when the made line for <paramref name="target"/> still says what stands there: brain made it, and nothing
    /// changed it since. Replacing it then takes nothing of the user's.
    /// </summary>
    public bool StillMade(string target) =>
        Lines().LastOrDefault(line => line.Kind == Made && string.Equals(line.Target, target, StringComparison.Ordinal)) is { } made && StillMade(made);

    /// <summary>
    /// True when what stands at the made line's target is what brain made: the model it downloaded, a directory it
    /// created, the settings.json an install.sh before brain install wrote, a link that points where brain pointed it,
    /// or a file or directory that holds what brain wrote.
    /// </summary>
    public static bool StillMade(ManifestLine made) => made.Detail switch
    {
        Downloaded or Created => Directory.Exists(made.Target),
        Generated => File.Exists(made.Target) && !Tree.IsLink(made.Target),
        _ when made.Detail.StartsWith(HashPrefix, StringComparison.Ordinal) =>
            Tree.Exists(made.Target) && !Tree.IsLink(made.Target) && HashPrefix + Tree.Hash(Tree.Read(made.Target)) == made.Detail,
        _ => string.Equals(Tree.LinkTarget(made.Target), made.Detail, StringComparison.Ordinal),
    };

    /// <summary>
    /// Records a target. A made line replaces the made line an earlier install wrote for the same target, so the record
    /// says what stands there now. Every saved line is kept, so no backup of something of yours goes unrecorded.
    /// </summary>
    public void Record(string kind, string target, string detail)
    {
        var lines = Lines();
        var line = new ManifestLine(kind, target, detail);
        if (lines.Contains(line))
        {
            return;
        }

        var kept = kind == Made ? lines.Where(other => other.Kind != Made || !string.Equals(other.Target, target, StringComparison.Ordinal)) : lines;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllLines(Path, [.. kept.Append(line).Select(each => string.Join(Tab, each.Kind, each.Target, each.Detail))]);
    }
}
