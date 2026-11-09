using System.Globalization;
using System.Text.RegularExpressions;
using Brain.Windows;

namespace Brain.Workspace;

/// <summary>
/// Git for a workspace's root: what is uncommitted against HEAD, staged and unstaged together with what git does not
/// track yet; each changed file's hunks against HEAD, and its patch with every line in place, staged or not; the commits
/// a push would send and those the upstream is ahead by; staging and unstaging a whole file; a commit of what is staged
/// under the root and nothing staged outside it; and a push of the current branch to its upstream, never forced. Reading
/// never writes the index. Each git call is a record with its duration: the status and the commits within the tree's
/// 200 ms and a file's diff within a file's 100 ms. A git that says no, as a commit with nothing staged or a push the
/// remote rejects, is a refusal that carries what git said; a git that does not start or runs too long is a failure. A
/// root in no repo has no status and refuses the rest. A patch over the reader's 2 MB limit has no lines, as a binary
/// file has none, and says why.
/// </summary>
internal sealed partial class WorkspaceGit
{
    public const double StatusBudget = ProjectFiles.ListBudget;
    public const double DiffBudget = ProjectFiles.ReadBudget;
    public const string Added = "added";
    public const string Changed = "changed";
    public const string Deleted = "deleted";
    public const string Untracked = "untracked";

    /// <summary>A literal pathspec: a file's name is never read as a pattern.</summary>
    private const string Literal = ":(literal)";

    /// <summary>Context enough to hold the whole file, so a patch reads in place.</summary>
    private const string WholeFile = "-U999999";

    private const string NoRepo = "the workspace is in no git repo";

    private readonly ProjectFiles files;
    private readonly Records records;

    /// <summary>The root's place in its repo: empty at its top, else ending in a slash; null when the root is in no repo.</summary>
    private readonly string? prefix;

    /// <summary>The checkout's own git folder, which holds its index.</summary>
    private readonly string gitDir = string.Empty;

    public WorkspaceGit(ProjectFiles files, Records records)
    {
        this.files = files;
        this.records = records;
        var place = records.Time("git", "rev-parse", null, () => Git.Run(files.Root, "rev-parse", "--path-format=absolute", "--git-dir", "--git-common-dir", "--show-prefix"));
        if (!place.Ok)
        {
            Watched = [];
            return;
        }

        var lines = place.Output.Split('\n');
        var dir = Path.TrimEndingDirectorySeparator(lines[0]);
        var common = Path.TrimEndingDirectorySeparator(lines[1]);
        prefix = lines[2];
        gitDir = dir;
        Watched = dir.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.Ordinal) || dir == common ? [common] : [common, dir];
    }

    /// <summary>The git folders whose index and refs say when the status may have changed: the repo's common one, and the checkout's own when it lies elsewhere.</summary>
    public IReadOnlyList<string> Watched { get; }

    /// <summary>Every file under the root that is uncommitted against HEAD, or untracked, with the branch and how far it is from its upstream.</summary>
    public GitStatus Status()
    {
        if (prefix is null)
        {
            return new GitStatus(false, null, null, 0, 0, [], false);
        }

        var run = Call("status", StatusBudget, "could not read the status", "--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all", "--no-renames", "--", ".");
        return ReadStatus(run.Output);
    }

    /// <summary>
    /// The changes of the file at <paramref name="path"/> against HEAD, each hunk where it sits. It reads
    /// through diff-index, which never refreshes the index: a porcelain diff would rewrite it and take its lock, racing a
    /// stage or a commit someone else runs in the repo, and firing the workspace's own watch.
    /// </summary>
    public FileDiff Diff(string path)
    {
        Refuse(path);
        var run = Call("diff-index " + path, DiffBudget, "could not diff " + path, "diff-index", "-p", "-U0", "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "HEAD", "--", Literal + path);
        return new FileDiff(path, ReadHunks(run.Output));
    }

    /// <summary>
    /// The patch of the file at <paramref name="path"/> with all its lines: against the index when <paramref name="staged"/>
    /// is false, as staging it would change it, and the index against HEAD when it is true. An untracked file is all
    /// added. It reads through diff-files and diff-index, which never write the index.
    /// </summary>
    public GitPatch Patch(string path, bool staged)
    {
        Refuse(path);
        var onDisk = new FileInfo(files.Full(path));
        if (!staged && onDisk.Exists && onDisk.Length > ProjectFiles.MaxText)
        {
            return new GitPatch(path, ProjectFiles.TooLarge, []);
        }

        if (staged)
        {
            var cached = Call("diff-index --cached " + path, DiffBudget, "could not diff " + path, "diff-index", "--cached", "-p", WholeFile, "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "HEAD", "--", Literal + path);
            return ReadPatch(path, cached.Output);
        }

        var others = Call("ls-files --others " + path, DiffBudget, "could not look up " + path, "ls-files", "--others", "--exclude-standard", "--", Literal + path);
        if (others.Output.Trim().Length > 0)
        {
            return AllAdded(path);
        }

        var run = Call("diff-files " + path, DiffBudget, "could not diff " + path, "diff-files", "-p", WholeFile, "--no-color", "--no-ext-diff", "--no-textconv", "--no-renames", "--", Literal + path);
        return ReadPatch(path, run.Output);
    }

    /// <summary>An untracked file as a patch: every line added.</summary>
    private GitPatch AllAdded(string path)
    {
        var file = files.Read(path);
        if (file.Text is null)
        {
            return new GitPatch(path, file.Problem, []);
        }

        var lines = file.Text.Split('\n');
        var count = lines.Length;
        if (lines[^1].Length == 0)
        {
            count--;
        }

        return new GitPatch(path, null, [.. lines.Take(count).Select((line) => new GitPatchLine("added", line))]);
    }

    /// <summary>The commits ahead of the upstream and behind it, the newest first, each side at most 100.</summary>
    public GitRemote Remote()
    {
        Refuse(string.Empty);
        var ahead = Call("log @{upstream}..HEAD", StatusBudget, "could not read the commits to push", "log", "-n", "100", "--format=%h%x00%s", "@{upstream}..HEAD");
        var behind = Call("log HEAD..@{upstream}", StatusBudget, "could not read the commits behind", "log", "-n", "100", "--format=%h%x00%s", "HEAD..@{upstream}");
        return new GitRemote(ReadCommits(ahead.Output), ReadCommits(behind.Output));
    }

    /// <summary>Stages the whole file at <paramref name="path"/>: its changes, its being new or its being deleted.</summary>
    public void Stage(string path)
    {
        Refuse(path);
        Call("add " + path, null, "could not stage " + path, "add", "-A", "--", Literal + path);
    }

    /// <summary>Unstages the whole file at <paramref name="path"/>, leaving its changes on disk.</summary>
    public void Unstage(string path)
    {
        Refuse(path);
        Call("reset " + path, null, "could not unstage " + path, "reset", "-q", "--", Literal + path);
    }

    /// <summary>
    /// Commits what is staged under the root with <paramref name="message"/>, and says the new commit. What is staged
    /// outside the root, which the pane does not show, stays staged and out of the commit, so a root that is one folder of
    /// a large repo commits only its own.
    /// </summary>
    public GitDone Commit(string? message)
    {
        Refuse(string.Empty);
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new BrainException("a commit needs a message");
        }

        if (prefix!.Length == 0)
        {
            Call("commit", null, "did not commit", "commit", "-q", "-m", message);
        }
        else
        {
            CommitRoot(message);
        }

        var head = Call("log -1", null, "could not read HEAD", "log", "-1", "--format=%h %s");
        return new GitDone("Committed " + head.Output.Trim());
    }

    /// <summary>
    /// Commits from a copy of the repo's index whose entries outside the root are reset to HEAD. The repo's own index is
    /// never written: what is staged under the root matches the new HEAD, and what is staged outside it stays staged.
    /// </summary>
    private void CommitRoot(string message)
    {
        var index = Path.Combine(gitDir, "brain-workspace-index-" + Guid.NewGuid().ToString("N"));
        var own = Path.Combine(gitDir, "index");
        try
        {
            if (File.Exists(own))
            {
                File.Copy(own, index);
            }

            CallOn(index, "reset outside " + prefix, null, "could not leave out what is staged outside " + prefix, "reset", "-q", "--", ":(top)", ":(top,exclude,literal)" + prefix);
            CallOn(index, "commit " + prefix, null, "did not commit", "commit", "-q", "-m", message);
        }
        finally
        {
            File.Delete(index);
        }
    }

    /// <summary>Pushes the current branch to its upstream and nothing else, never forced, and says where it went.</summary>
    public GitDone Push()
    {
        Refuse(string.Empty);
        var head = records.Time("git", "symbolic-ref HEAD", null, () => Git.Run(files.Root, "symbolic-ref", "-q", "--short", "HEAD"));
        if (!head.Ok)
        {
            throw new BrainException("HEAD is detached: a push sends the current branch, and there is none");
        }

        var branch = head.Output.Trim();
        var upstream = Call("for-each-ref " + branch, null, "could not read the upstream of " + branch, "for-each-ref", "--format=%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads/" + branch);
        var parts = upstream.Output.Trim().Split('\0');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new BrainException($"{branch} has no upstream to push to; set one with git push -u once");
        }

        var remote = parts[0];
        var target = parts[1];
        Call("push " + remote + " " + branch, null, $"did not push {branch} to {remote}", "push", "--porcelain", remote, $"refs/heads/{branch}:{target}");
        return new GitDone($"Pushed {branch} to {remote} {target.Replace("refs/heads/", string.Empty, StringComparison.Ordinal)}");
    }

    /// <summary>
    /// Runs git in the root as a record named git with <paramref name="args"/>. A git that says no is a refusal saying it
    /// <paramref name="failed"/>, with what git said.
    /// </summary>
    private Git.GitRun Call(string args, double? budget, string failed, params string[] arguments) =>
        CallOn(null, args, budget, failed, arguments);

    /// <summary>As <see cref="Call"/>, on the index at <paramref name="index"/>, or the repo's own when it is null.</summary>
    private Git.GitRun CallOn(string? index, string args, double? budget, string failed, params string[] arguments) =>
        records.Time("git", args, budget, () =>
        {
            var run = Git.RunWith(files.Root, index, arguments);
            if (!run.Ok)
            {
                var said = (run.Errors.Trim() + "\n" + run.Output.Trim()).Trim();
                throw new BrainException($"git {failed}: {said}");
            }

            return run;
        });

    /// <summary>Refuses outside a repo, and a path that leads out of the root.</summary>
    private void Refuse(string path)
    {
        if (prefix is null)
        {
            throw new BrainException(NoRepo);
        }

        files.Full(path);
    }

    private GitStatus ReadStatus(string output)
    {
        string? branch = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var changed = new List<GitFile>();
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (entry.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = entry["# branch.head ".Length..];
            }
            else if (entry.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = entry["# branch.upstream ".Length..];
            }
            else if (AheadBehind().Match(entry) is { Success: true } counts)
            {
                ahead = int.Parse(counts.Groups[1].Value, CultureInfo.InvariantCulture);
                behind = int.Parse(counts.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else if (Entry(entry) is { } file)
            {
                changed.Add(file);
            }
        }

        var cut = changed.Count > ProjectFiles.MaxFiles;
        return new GitStatus(true, branch, upstream, ahead, behind, [.. changed.Take(ProjectFiles.MaxFiles)], cut);
    }

    /// <summary>One file of git's status, its path from the root; null for a line that names no file.</summary>
    private GitFile? Entry(string entry)
    {
        if (entry.StartsWith("? ", StringComparison.Ordinal))
        {
            return new GitFile(FromRoot(entry[2..]), Untracked, false, true);
        }

        if (entry.StartsWith("u ", StringComparison.Ordinal))
        {
            return new GitFile(FromRoot(entry.Split(' ', 11)[10]), Changed, false, true);
        }

        if (!entry.StartsWith("1 ", StringComparison.Ordinal))
        {
            return null;
        }

        var fields = entry.Split(' ', 9);
        var staged = fields[1][0];
        var unstaged = fields[1][1];
        return new GitFile(FromRoot(fields[8]), State(staged, unstaged), staged != '.', unstaged != '.');
    }

    /// <summary>What a file is against HEAD, from what its index and its working tree say.</summary>
    private static string State(char staged, char unstaged)
    {
        if (staged == 'A')
        {
            return Added;
        }

        if (staged == 'D' || unstaged == 'D')
        {
            return Deleted;
        }

        return Changed;
    }

    /// <summary>A path from the repo's top as a path from the root.</summary>
    private string FromRoot(string path) => path[prefix!.Length..];

    /// <summary>The hunks of a diff with no context: where each sits on each side.</summary>
    private static List<GitHunk> ReadHunks(string output)
    {
        var hunks = new List<GitHunk>();
        foreach (var line in output.Split('\n'))
        {
            if (HunkHead().Match(line) is { Success: true } head)
            {
                hunks.Add(new GitHunk(Number(head.Groups[1]), Count(head.Groups[2]), Number(head.Groups[3]), Count(head.Groups[4])));
            }
        }

        return hunks;
    }

    /// <summary>The lines of a patch made with the whole file as context: those after its hunk head, each same, added or removed.</summary>
    private static GitPatch ReadPatch(string path, string output)
    {
        if (output.Length > ProjectFiles.MaxText)
        {
            return new GitPatch(path, ProjectFiles.TooLarge, []);
        }

        var lines = new List<GitPatchLine>();
        var inHunk = false;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("Binary files ", StringComparison.Ordinal))
            {
                return new GitPatch(path, ProjectFiles.Binary, []);
            }

            if (HunkHead().IsMatch(line))
            {
                inHunk = true;
            }
            else if (inHunk && line.StartsWith('+'))
            {
                lines.Add(new GitPatchLine("added", line[1..]));
            }
            else if (inHunk && line.StartsWith('-'))
            {
                lines.Add(new GitPatchLine("removed", line[1..]));
            }
            else if (inHunk && line.StartsWith(' '))
            {
                lines.Add(new GitPatchLine("same", line[1..]));
            }
        }

        return new GitPatch(path, null, lines);
    }

    private static List<GitCommit> ReadCommits(string output)
    {
        var commits = new List<GitCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\0', 2);
            var subject = string.Empty;
            if (parts.Length > 1)
            {
                subject = parts[1];
            }

            commits.Add(new GitCommit(parts[0], subject));
        }

        return commits;
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    /// <summary>A hunk's count of lines, which git leaves out when it is one.</summary>
    private static int Count(Group group)
    {
        if (!group.Success)
        {
            return 1;
        }

        return Number(group);
    }

    [GeneratedRegex(@"^# branch\.ab \+(\d+) -(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AheadBehind();

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHead();
}
