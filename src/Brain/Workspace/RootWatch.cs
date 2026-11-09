using Brain.Windows;

namespace Brain.Workspace;

/// <summary>
/// Watches a workspace's folder through the system's file events, inotify on Linux, and never by polling. The paths that
/// change within 50 ms of the first go out together, each as its path from the root. A change inside a git folder is not
/// the workspace's file: one to git's index, HEAD or a ref says only that git moved, as a stage, a commit or a push moves
/// it. A git folder outside the root, as a worktree's common one, is watched beside it. When the system loses events, or
/// more than 500 paths change at once, the batch says all. Each batch is a record.
/// </summary>
internal sealed class RootWatch : IDisposable
{
    public const int MaxPaths = 500;
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);

    /// <summary>The files of a git folder whose change can change what is uncommitted, besides each under refs/.</summary>
    private static readonly HashSet<string> GitState = new(["index", "HEAD", "packed-refs"], StringComparer.Ordinal);

    private readonly Lock gate = new();
    private readonly ProjectFiles files;
    private readonly IReadOnlyList<string> gitDirs;
    private readonly Records records;
    private readonly Action<Changes> changed;
    private readonly ITimer flush;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly HashSet<string> paths = new(StringComparer.Ordinal);
    private bool all;
    private bool git;
    private bool pending;

    public RootWatch(ProjectFiles files, IReadOnlyList<string> gitDirs, Records records, TimeProvider time, Action<Changes> changed)
    {
        this.files = files;
        this.gitDirs = gitDirs;
        this.records = records;
        this.changed = changed;
        flush = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Watch(files.Root);
        foreach (var dir in gitDirs.Where((dir) => !Inside(dir, files.Root)))
        {
            Watch(dir);
        }
    }

    /// <summary>Watches <paramref name="folder"/>; a watch the system refuses is a record, and the workspace still reads, showing no change there.</summary>
    private void Watch(string folder)
    {
        try
        {
            watchers.Add(records.Time("watch", "start " + folder, null, () => Start(folder)));
        }
        catch (BrainException)
        {
            // The record says the watch was refused.
        }
    }

    /// <summary>The watch over <paramref name="folder"/>, refused when the system will not give one.</summary>
    private FileSystemWatcher Start(string folder)
    {
        var watching = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        try
        {
            watching.Changed += (_, change) => Note(change.FullPath);
            watching.Created += (_, change) => Note(change.FullPath);
            watching.Deleted += (_, change) => Note(change.FullPath);
            watching.Renamed += (_, change) => Renamed(change);
            watching.Error += (_, _) => Lost();
            watching.EnableRaisingEvents = true;
            return watching;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // The machine is out of inotify watches, or the folder shut.
            watching.Dispose();
            throw new BrainException($"the workspace cannot watch {folder}: {error.Message}");
        }
    }

    private static bool Inside(string full, string folder) =>
        full == folder || full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private void Renamed(RenamedEventArgs change)
    {
        Note(change.OldFullPath);
        Note(change.FullPath);
    }

    private void Note(string full)
    {
        var dir = gitDirs.FirstOrDefault((one) => Inside(full, one));
        if (dir is not null)
        {
            NoteGit(full, dir);
            return;
        }

        var path = files.Relative(full);
        if (path == ".git" || path.StartsWith(".git/", StringComparison.Ordinal))
        {
            return;
        }

        lock (gate)
        {
            paths.Add(path);
            Schedule();
        }
    }

    /// <summary>Notes that git moved when <paramref name="full"/>, in the git folder <paramref name="dir"/>, is its index, HEAD or a ref; a lock is not.</summary>
    private void NoteGit(string full, string dir)
    {
        var inDir = Path.GetRelativePath(dir, full).Replace(Path.DirectorySeparatorChar, '/');
        var state = GitState.Contains(Path.GetFileName(full)) || inDir.StartsWith("refs/", StringComparison.Ordinal);
        if (!state || full.EndsWith(".lock", StringComparison.Ordinal))
        {
            return;
        }

        lock (gate)
        {
            git = true;
            Schedule();
        }
    }

    private void Lost()
    {
        lock (gate)
        {
            all = true;
            Schedule();
        }
    }

    /// <summary>Sends the batch 50 ms after its first change, so a burst of changes goes out as one.</summary>
    private void Schedule()
    {
        if (pending)
        {
            return;
        }

        pending = true;
        flush.Change(Settle, Timeout.InfiniteTimeSpan);
    }

    private void Flush()
    {
        Changes batch;
        lock (gate)
        {
            var whole = all || paths.Count > MaxPaths;
            batch = new Changes(whole ? [] : [.. paths.Order(StringComparer.Ordinal)], whole, git);
            paths.Clear();
            all = false;
            git = false;
            pending = false;
        }

        var args = batch.All ? "all" : $"{batch.Paths.Count} paths";
        if (batch.Git)
        {
            args += " and git";
        }

        records.Time("watch", args, null, () =>
        {
            changed(batch);
            return true;
        });
    }

    public void Dispose()
    {
        foreach (var watcher in watchers)
        {
            watcher.Dispose();
        }

        flush.Dispose();
    }
}
