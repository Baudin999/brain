namespace Brain.Installing;

/// <summary>What <c>brain install</c> tells the user before it asks anything: what brain is, and what each choice means.</summary>
internal static class InstallText
{
    /// <summary>The banner, then what brain is, what it changes in every project, that it is a beta and what it costs.</summary>
    public const string Banner = """

          ____  ____      _    ___ _   _
         | __ )|  _ \    / \  |_ _| \ | |
         |  _ \| |_) |  / _ \  | ||  \| |
         | |_) |  _ <  / ___ \ | || |\  |
         |____/|_| \_\/_/   \_\___|_| \_|

        brain is a knowledge graph you share with Claude Code, kept as plain text in git. It gives Claude a memory of
        who you are, how you work and what you decided, and a harness of instructions, skills, hooks and agents that
        use it.

        What changes: Claude Code, in every project, then starts as brain's open agent. The open agent talks with you,
        holds the overview and sends closed agents to build: a builder in its own worktree and a validator that reviews
        and lands its work. The open agent never writes code itself. Hooks gate building until the developer skill is
        loaded, and keep hand edits out of the graph: every write to the graph waits for your yes in brain's window.

        brain is a beta under active development. Expect rough edges and changes.

        What it costs: brain uses your Claude usage. The settings it installs default to Opus, and every write to the
        graph calls a Sonnet judge.

        Next it asks where your personal files go and which permission mode Claude Code starts in. Then it shows every
        change on disk with its diff, and asks whether to continue. Nothing is changed before that yes, and
        brain uninstall puts everything back the way it was.

        """;

    /// <summary>The choice to replace or keep a CLAUDE.md or settings.json of the user's own, and what keeping it costs.</summary>
    public static string ReplaceOrKeep(string target, string backup, string loss) => $"""
        You already have {target}, and it is yours, not brain's.

          r  Replace it with brain's, as shown above. Yours moves to {backup}/, and brain uninstall puts it back.
          k  Keep yours. Brain installs the rest, but {loss}

        """;

    /// <summary>
    /// The last word before anything changes: how many changes, where backups go, what overwrites edits to the installed
    /// harness when some part links into it, and how to undo it.
    /// </summary>
    public static string Continue(int changes, string backup, HarnessChoice harness) => $"""
        That is every change: {changes} in all. Nothing has been changed yet.

          Backups  whatever of yours stands in the way moves to {backup}/; nothing of yours is deleted
        {Edits(harness)}  Undo     brain uninstall removes what brain made and puts back what was there before; run it before you remove brain's package
          Data     your data repo is never touched by an uninstall

        If you say yes, brain installs all of it now. If you say anything else, nothing changes.

        """;

    /// <summary>
    /// The Edits line, ending in a line break, or nothing when no part links into the installed harness: an unpacked
    /// harness is overwritten by running brain install again, a packaged one by an update of the package.
    /// </summary>
    private static string Edits(HarnessChoice harness)
    {
        if (!harness.LinksInstalled)
        {
            return string.Empty;
        }

        var overwrites = harness.Installed.Unpacks ? "running brain install again overwrites them" : "an update of brain's package overwrites them";
        return $"  Edits    the harness files are links into {harness.Installed.Root}; {overwrites}, so editing them is at your own risk\n";
    }
}
