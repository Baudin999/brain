namespace Brain.Installing;

/// <summary>
/// <c>brain uninstall</c>: puts Claude Code's config directory, the CLI's link on PATH and the machine's guard back the
/// way they were before brain, from the install record. A link brain made is removed while it still points where brain
/// pointed it; one changed since is left and reported. A file or directory brain wrote is removed while it holds what
/// brain wrote; one changed since moves to an uninstall backup, so no edit is lost. The model is removed. Then every
/// target of the user's own moves back as it was before brain, and each later backup of it, such as an edit of a copy
/// brain made, is named where it stays. Directories brain's targets leave empty go. The data repo is never touched.
/// </summary>
internal sealed class Uninstaller(BrainEnv env, Installation at, Choices choices)
{
    private readonly string undo = at.NewBackupDir("uninstall");
    private readonly List<string> parents = [];
    private readonly List<string> created = [];
    private int removed;
    private int moved;
    private int restored;
    private int left;

    public int Run()
    {
        var manifest = new Manifest(at.ManifestPath);
        if (!File.Exists(manifest.Path))
        {
            env.Out.WriteLine($"uninstall: there is no install record at {manifest.Path}, so there is nothing to undo.");
            env.Out.WriteLine($"Your data repo at {env.Home} is left alone.");
            return ExitCodes.Ok;
        }

        var lines = manifest.Lines();
        var made = lines.Where(line => line.Kind == Manifest.Made).ToList();
        var saved = lines.Where(line => line.Kind == Manifest.Saved).ToList();
        var data = made.Any(line => line.Target.StartsWith(env.Home + "/", StringComparison.Ordinal))
            ? $"Your data repo at {env.Home} is left alone, but for the harness brain unpacked into its cache."
            : $"Your data repo at {env.Home} is left alone.";
        env.Out.WriteLine($"""
            This uninstalls brain. It changes:

              {made.Count(line => line.Detail != Manifest.Created)} links, files and directories brain made: removed. Among them {Path.Combine(at.ClaudeHome, "CLAUDE.md")},
              settings.json, the hooks, skills and agents brain put there, the brain command on your PATH and the
              embedding model. One you changed since brain wrote it moves to {undo}/ instead.
              {saved.Count} files and links of yours that an install backed up: put back where they were.

            {data} It is yours: delete it yourself if you want it gone.

            """);
        if (!choices.Confirm("Do you want to continue?"))
        {
            env.Out.WriteLine("Nothing was changed.");
            return ExitCodes.Failure;
        }

        made.ForEach(Remove);
        foreach (var backups in saved.GroupBy(line => line.Target, StringComparer.Ordinal))
        {
            Restore(backups.First());
            foreach (var later in backups.Skip(1).Where(line => Tree.Exists(line.Detail)))
            {
                env.Out.WriteLine($"kept       {later.Detail} (your {later.Target} as a later install found it; the one from before brain is back)");
                left++;
            }
        }

        foreach (var dir in parents.Distinct(StringComparer.Ordinal).OrderByDescending(dir => dir.Length))
        {
            RemoveIfEmpty(dir);
        }

        File.Delete(manifest.Path);
        RemoveEmpty(at.Backups);
        foreach (var dir in created.Distinct(StringComparer.Ordinal).OrderByDescending(dir => dir.Length).Where(Directory.Exists))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }

        env.Out.WriteLine($"uninstall: {removed} removed, {moved} moved, {restored} restored, {left} left");
        env.Out.WriteLine(data);
        return ExitCodes.Ok;
    }

    private void Remove(ManifestLine line)
    {
        var target = line.Target;
        if (!Tree.Exists(target))
        {
            return;
        }

        if (line.Detail == Manifest.Created)
        {
            created.Add(target);
        }
        else if (line.Detail == Manifest.Downloaded)
        {
            Gone(target);
        }
        else if (line.Detail == Manifest.Generated)
        {
            if (Manifest.StillMade(line))
            {
                MoveToUndo(target, "brain wrote it before brain install");
            }
        }
        else if (Manifest.StillMade(line))
        {
            Gone(target);
        }
        else if (line.Detail.StartsWith(Manifest.HashPrefix, StringComparison.Ordinal))
        {
            MoveToUndo(target, "it changed since brain wrote it");
        }
        else
        {
            env.Out.WriteLine($"left       {target} (it changed since brain linked it)");
            left++;
        }
    }

    private void Gone(string target)
    {
        Tree.Delete(target);
        env.Out.WriteLine("removed    " + target);
        removed++;
        parents.Add(Path.GetDirectoryName(target)!);
    }

    private void MoveToUndo(string target, string why)
    {
        var to = at.BackupOf(undo, target);
        Tree.Move(target, to);
        env.Out.WriteLine($"moved      {target} -> {to} ({why})");
        moved++;
        parents.Add(Path.GetDirectoryName(target)!);
    }

    private void Restore(ManifestLine line)
    {
        if (!Tree.Exists(line.Detail))
        {
            env.Out.WriteLine($"left       {line.Target} (its backup {line.Detail} is gone)");
            left++;
        }
        else if (Tree.Exists(line.Target))
        {
            env.Out.WriteLine($"left       {line.Detail} (something new stands at {line.Target})");
            left++;
        }
        else
        {
            Tree.Move(line.Detail, line.Target);
            env.Out.WriteLine("restored   " + line.Target);
            restored++;
        }
    }

    /// <summary>Removes every empty directory under <paramref name="dir"/>, and <paramref name="dir"/> itself when that leaves it empty.</summary>
    private static void RemoveEmpty(string dir)
    {
        if (!Directory.Exists(dir) || Tree.IsLink(dir))
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(dir))
        {
            RemoveEmpty(child);
        }

        if (!Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
        }
    }

    /// <summary>
    /// Removes a directory that only held brain's targets, and each parent up to Claude Code's config directory that
    /// is empty after it, or the guard's directory. One with anything else in it stays.
    /// </summary>
    private void RemoveIfEmpty(string dir)
    {
        var guardDir = Path.GetDirectoryName(at.GuardPath)!;
        while ((dir.StartsWith(at.ClaudeHome + "/", StringComparison.Ordinal) || dir == guardDir) &&
            Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir)!;
        }
    }
}
