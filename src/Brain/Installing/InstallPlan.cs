namespace Brain.Installing;

/// <summary>
/// Everything an install would change, worked out before anything is touched: the placements that are not in place
/// yet, the targets that already are, whether the data repo is created, what happens to the embedding models' files,
/// and the notes on what it leaves out.
/// </summary>
internal sealed record InstallPlan(
    IReadOnlyList<Placement> Placements,
    IReadOnlyList<string> InPlace,
    bool CreatesData,
    ModelPlan Model,
    IReadOnlyList<string> Notes)
{
    private const string Harness = "harness";
    private const string ClaudeMd = "CLAUDE.md";
    private const string Settings = "settings.json";
    private static readonly string[] PrivateDirs = ["hooks", "skills", "agents", "rules"];

    /// <summary>The parts of the harness that link into Claude Code's config directory, each to one harness root.</summary>
    public static readonly string[] Parts = [ClaudeMd, "agents", "hooks", "skills"];

    /// <summary>What a choice of keeping one's own CLAUDE.md costs.</summary>
    public const string KeepClaudeMd = "Claude does not get brain's instructions: how the open agent and the closed agents split the work, and that the graph is your shared memory. The skills and hooks are there, and nothing tells Claude to use them.";

    /// <summary>What a choice of keeping one's own settings.json costs.</summary>
    public const string KeepSettings = "none of brain's hooks run: no graph context at the start of a session, no developer gate before building, no door that keeps hand edits out of the graph and no worktree guard. Claude Code does not start as the brain agent either.";

    /// <summary>
    /// The plan for an install into <paramref name="at"/>, with the data repo at <paramref name="brainHome"/> and the
    /// permission <paramref name="mode"/>, each part of the harness linked to the root <paramref name="harness"/> picks
    /// for it. A bare binary's harness is unpacked first when any part links into it.
    /// </summary>
    public static InstallPlan Build(Installation at, string brainHome, string mode, string userHome, HarnessChoice harness)
    {
        var settings = SettingsFile.Merge(
            Overlay(harness.Settings.Files[Settings], Path.Combine(brainHome, Harness, Settings)),
            mode,
            string.Equals(brainHome, at.DefaultBrainHome, StringComparison.Ordinal) ? null : brainHome,
            SettingsFile.Spelled(at.ClaudeHome, userHome));
        var placements = new List<Placement>();
        if (harness.Unpacks)
        {
            placements.Add(Placement.Unpack(harness.Installed));
        }

        var instructions = harness.For(ClaudeMd);
        placements.Add(Placement.Link(Path.Combine(at.ClaudeHome, ClaudeMd), Path.Combine(instructions.Root, ClaudeMd), Whole(instructions.Files[ClaudeMd]), KeepClaudeMd));
        placements.Add(Placement.Copy(Path.Combine(at.ClaudeHome, Settings), Whole(settings), KeepSettings));
        var items = new List<string>();
        foreach (var part in Parts.Where(part => part != ClaudeMd))
        {
            var root = harness.For(part);
            foreach (var (item, files) in Items(root.Files, part))
            {
                items.Add(item);
                placements.Add(Placement.Link(Path.Combine(at.ClaudeHome, item), Path.Combine(root.Root, item), files));
            }
        }

        if (at.Checkout is not null)
        {
            placements.Add(Placement.Link(Path.Combine(at.LocalBin, "brain"), Path.Combine(at.Checkout, "bin", "brain")));
        }

        var notes = new List<string>();
        placements.AddRange(Private(at, brainHome, items, notes));
        return new InstallPlan(
            [.. placements.Where(placement => !placement.InPlace)],
            [.. placements.Where(placement => placement.InPlace).Select(placement => placement.Target)],
            !Tree.Exists(brainHome),
            EmbeddingModel.Plan(at, brainHome),
            notes);
    }

    /// <summary>
    /// Each child of <paramref name="part"/>, a directory of the harness, with its files, so anything else in that
    /// directory of Claude Code's config, such as a skill of the user's own, stays put.
    /// </summary>
    private static SortedDictionary<string, SortedDictionary<string, byte[]>> Items(IReadOnlyDictionary<string, byte[]> harness, string part)
    {
        var items = new SortedDictionary<string, SortedDictionary<string, byte[]>>(StringComparer.Ordinal);
        foreach (var (key, bytes) in harness.Where(file => file.Key.StartsWith(part + "/", StringComparison.Ordinal)))
        {
            var parts = key.Split('/');
            var item = parts[0] + "/" + parts[1];
            if (!items.TryGetValue(item, out var files))
            {
                items[item] = files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            }

            files[parts.Length > 2 ? string.Join('/', parts[2..]) : Tree.Self] = bytes;
        }

        return items;
    }

    /// <summary>
    /// The data repo's own harness: each child of its hooks, skills, agents and rules links in beside brain's, unless
    /// brain ships one of that name, and its guard links to where scripts/dotnet-guard.sh looks for it.
    /// </summary>
    private static IEnumerable<Placement> Private(Installation at, string brainHome, IEnumerable<string> shipped, List<string> notes)
    {
        var own = Path.Combine(brainHome, Harness);
        foreach (var dir in PrivateDirs.Where(dir => Directory.Exists(Path.Combine(own, dir))))
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(Path.Combine(own, dir)).Order(StringComparer.Ordinal))
            {
                var item = dir + "/" + Path.GetFileName(child);
                if (shipped.Contains(item, StringComparer.Ordinal))
                {
                    notes.Add($"skipped    {child} (brain ships {item} itself)");
                    continue;
                }

                yield return Placement.Link(Path.Combine(at.ClaudeHome, item), child);
            }
        }

        var guard = Path.Combine(own, "guard");
        if (File.Exists(guard) && !OperatingSystem.IsWindows() && (File.GetUnixFileMode(guard) & UnixFileMode.UserExecute) != 0)
        {
            yield return Placement.Link(at.GuardPath, guard);
        }
    }

    private static IEnumerable<byte[]> Overlay(byte[] shipped, string own) => File.Exists(own) ? [shipped, File.ReadAllBytes(own)] : [shipped];

    private static SortedDictionary<string, byte[]> Whole(byte[] bytes) => new(StringComparer.Ordinal) { [Tree.Self] = bytes };
}
