namespace Brain.Installing;

/// <summary>
/// The harness roots one install links into: every part of the harness into <see cref="Installed"/>, but the
/// <see cref="Parts"/> named, which link into <see cref="Linked"/>, such as a checkout of brain's own development.
/// </summary>
internal sealed record HarnessChoice(HarnessRoot Installed, HarnessRoot? Linked, IReadOnlyCollection<string> Parts)
{
    /// <summary>The root <paramref name="part"/> links into.</summary>
    public HarnessRoot For(string part) => Linked is not null && Parts.Contains(part, StringComparer.Ordinal) ? Linked : Installed;

    /// <summary>The harness whose settings.json the install writes from: the linked one when a part links there.</summary>
    public HarnessRoot Settings => Linked is not null && Parts.Count > 0 ? Linked : Installed;

    /// <summary>True when some part of the harness links into the installed harness.</summary>
    public bool LinksInstalled => InstallPlan.Parts.Any(part => ReferenceEquals(For(part), Installed));

    /// <summary>True when the installed harness must be unpacked: it is a bare binary's, and some part links into it.</summary>
    public bool Unpacks => Installed.Unpacks && LinksInstalled;

    /// <summary>True when <paramref name="path"/> lies in either root.</summary>
    public bool Holds(string path) => Installed.Holds(path) || Linked?.Holds(path) == true;

    /// <summary>
    /// The choice that keeps what each part links to now: a part whose links in <paramref name="claudeHome"/> go into a
    /// checkout's harness, as ./link.sh made them, stays linked there, and every other part links into
    /// <paramref name="installed"/>. A checkout's harness is a harness/ directory beside a bin/brain launcher, so a
    /// package's harness of an older version, or an unpacked one, is followed to the installed harness instead.
    /// </summary>
    public static HarnessChoice Keeping(string claudeHome, HarnessRoot installed)
    {
        var linked = InstallPlan.Parts.Select(part => (Part: part, Root: CheckoutOf(claudeHome, part)))
            .Where(each => each.Root is not null && each.Root != installed.Root).ToList();
        var root = linked.Select(each => each.Root).FirstOrDefault();
        return root is null
            ? new HarnessChoice(installed, null, [])
            : new HarnessChoice(installed, HarnessRoot.At(root), [.. linked.Where(each => each.Root == root).Select(each => each.Part)]);
    }

    /// <summary>The checkout harness the links of <paramref name="part"/> go into, or null when none does.</summary>
    private static string? CheckoutOf(string claudeHome, string part)
    {
        var target = Path.Combine(claudeHome, part);
        IEnumerable<string> links = part == InstallPlan.Parts[0] ? [target] : Directory.Exists(target) ? Directory.EnumerateFileSystemEntries(target) : [];
        foreach (var source in links.Select(Tree.LinkTarget).OfType<string>())
        {
            var root = Path.GetDirectoryName(source);
            if (part != InstallPlan.Parts[0])
            {
                root = Path.GetFileName(root) == part ? Path.GetDirectoryName(root) : null;
            }

            if (root is not null && Path.GetFileName(root) == "harness" && File.Exists(Path.Combine(root, "settings.json")) &&
                File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "bin", "brain")))
            {
                return root;
            }
        }

        return null;
    }
}
