using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Brain;

/// <summary>
/// The questions the brain asks git: where a repo's top is, whether a commit exists in it, its commit subjects, where
/// it came from, which commits changed which files, what a file held in a commit, which paths it ignores and which files
/// a checkout holds. It also commits one path and nothing else, commits a list of files and nothing else, makes a new
/// repo, and runs any call whose answer its caller reads, as a workspace's <c>WorkspaceGit</c> does. A git that does not
/// start, runs past <see cref="Limit"/> or fails for any reason but the one a question expects, such as a directory in
/// no repo, throws a failure: it never reads as an answer.
/// </summary>
internal static class Git
{
    private const string Executable = "git";
    private const string InDirectory = "-C";
    private const string RevParse = "rev-parse";
    private const string ShowTopLevel = "--show-toplevel";
    private const string CommonDir = "--path-format=absolute";
    private const string GitCommonDir = "--git-common-dir";
    private const string Remote = "remote";
    private const string GetUrl = "get-url";
    private const string Origin = "origin";
    private const string Log = "log";
    private const string SubjectFormat = "--format=%s";
    private const string CatFile = "cat-file";

    /// <summary>The exit code of <c>git remote get-url</c> for a remote the repo does not have.</summary>
    private const int NoSuchRemote = 2;

    /// <summary>The exit code of <c>git check-ignore</c> when it ignores none of the paths it was given.</summary>
    private const int NoneIgnored = 1;

    /// <summary>The longest git may run before brain stops it and fails.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(1);

    /// <summary>The top of the checkout <paramref name="directory"/> lies in, or null when it lies in none.</summary>
    public static string? TopLevel(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var run = Run(directory, RevParse, ShowTopLevel);
        return run.Answer(directory, "find the repo's top", run.NotARepo);
    }

    /// <summary>
    /// The main checkout of the repo that <paramref name="directory"/> belongs to: the directory holding the common git
    /// dir, the same for the checkout and every worktree. Null when the directory is not in a repo or the repo is bare.
    /// </summary>
    public static string? MainCheckout(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var run = Run(directory, RevParse, CommonDir, GitCommonDir);
        var common = run.Answer(directory, "find the repo's git dir", run.NotARepo) ?? string.Empty;
        return Path.GetFileName(common) == ".git" ? Path.GetDirectoryName(common) : null;
    }

    /// <summary>The url of the repo's origin remote, or null when it has none.</summary>
    public static string? OriginUrl(string repo)
    {
        var run = Run(repo, Remote, GetUrl, Origin);
        return run.Answer(repo, "read the origin remote", run.Code == NoSuchRemote);
    }

    /// <summary>The subject line of every commit in the repo, newest first; none when the directory is not a repo.</summary>
    public static IReadOnlyList<string> Subjects(string repo)
    {
        if (!Directory.Exists(repo))
        {
            return [];
        }

        var run = Run(repo, Log, SubjectFormat);
        var subjects = run.Answer(repo, "read the commit subjects", run.NotARepo || run.Errors.Contains("does not have any commits yet", StringComparison.Ordinal));
        return subjects?.Split(NodeSyntax.NewLine, StringSplitOptions.RemoveEmptyEntries) ?? [];
    }

    /// <summary>The commit HEAD names in the repo <paramref name="directory"/> lies in, or null when it lies in none or has no commit.</summary>
    public static string? Head(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var run = Run(directory, RevParse, "--verify", "-q", "HEAD");
        return run.Answer(directory, "read HEAD", run.Code == ExitCodes.Failure || run.NotARepo);
    }

    /// <summary>True when <paramref name="commit"/> is <paramref name="head"/> or one of its ancestors; false too for a commit git does not have.</summary>
    public static bool IsAncestor(string directory, string commit, string head)
    {
        var run = Run(directory, "merge-base", "--is-ancestor", commit, head);
        if (run.Ok)
        {
            return true;
        }

        var unknown = run.Errors.Contains("Not a valid commit name", StringComparison.Ordinal) || run.Errors.Contains("Not a valid object name", StringComparison.Ordinal);
        return run.Code == ExitCodes.Failure || unknown ? false : throw run.Failed(directory, $"tell whether {commit} is an ancestor of {head}");
    }

    /// <summary>Where <paramref name="directory"/> lies inside its repo, as git prefixes paths: empty at the top, else ending in a slash; null outside a repo.</summary>
    public static string? Prefix(string directory)
    {
        var run = Run(directory, RevParse, "--show-prefix");
        return run.Answer(directory, "find the directory's place in its repo", run.NotARepo);
    }

    /// <summary>
    /// Each commit that added, changed or deleted a file under <paramref name="directory"/>, oldest first, with each file's
    /// status letter and its path from the repo's top: every commit up to HEAD, or only those after
    /// <paramref name="since"/>. A rename counts as a delete and an add. A history git cannot read is a failure.
    /// </summary>
    public static IReadOnlyList<(string Commit, IReadOnlyList<(char Status, string Path)> Files)> Changes(string directory, string? since)
    {
        const char Mark = '\u0001';
        List<string> arguments = ["-c", "core.quotePath=false", Log, "--reverse", "--no-renames", "--name-status", "--format=%x01%H"];
        if (since is not null)
        {
            arguments.Add(since + "..HEAD");
        }

        arguments.AddRange(["--", "."]);
        var run = Run(directory, [.. arguments]);
        if (!run.Ok)
        {
            throw run.Failed(directory, "read the history");
        }

        return [.. run.Output.Split(Mark, StringSplitOptions.RemoveEmptyEntries).Select(commit =>
        {
            var lines = commit.Split(NodeSyntax.NewLine, StringSplitOptions.RemoveEmptyEntries);
            return (lines[0].Trim(), (IReadOnlyList<(char, string)>)[.. lines.Skip(1).Select(line => line.Split('\t', 2)).Where(parts => parts.Length == 2).Select(parts => (parts[0][0], parts[1]))]);
        })];
    }

    /// <summary>
    /// The text of each object, named as <c>commit:path</c> with the path from the repo's top, read by one git process;
    /// null for an object git does not have, which git says on stdout. A git that fails or answers too few is a failure.
    /// </summary>
    public static IReadOnlyList<string?> Blobs(string directory, IReadOnlyList<string> objects)
    {
        using var process = Start(directory, [CatFile, "--batch"], input: true);
        using var watch = new Watch(process);
        var errors = process.StandardError.ReadToEndAsync();
        var asked = Task.Run(() =>
        {
            foreach (var name in objects)
            {
                process.StandardInput.Write(name + NodeSyntax.NewLine);
            }

            process.StandardInput.Close();
        });
        using var output = new BufferedStream(process.StandardOutput.BaseStream);
        var texts = new List<string?>();
        try
        {
            Read(output, objects.Count, texts);
        }
        catch (EndOfStreamException)
        {
            // git ended in the middle of an object: it was stopped, or it failed, which the checks below say.
        }

        // Before the wait on what was asked, which fails on the closed input of a git that was stopped.
        watch.ThrowIfStopped(directory, CatFile);
        asked.Wait();
        process.WaitForExit();
        var said = errors.Result;
        return process.ExitCode == ExitCodes.Ok && texts.Count == objects.Count
            ? texts
            : throw BrainException.Failed($"git could not read {objects.Count} objects in {directory}, it read {texts.Count} and exited {process.ExitCode}: {said.Trim()}");
    }

    /// <summary>
    /// The paths among <paramref name="paths"/>, each relative to <paramref name="directory"/>, that git ignores there, read
    /// by one git process. A git that fails is a failure.
    /// </summary>
    public static IReadOnlySet<string> Ignored(string directory, IReadOnlyList<string> paths)
    {
        using var process = Start(directory, ["check-ignore", "-z", "--stdin"], input: true);
        using var watch = new Watch(process);
        var errors = process.StandardError.ReadToEndAsync();
        var asked = Task.Run(() =>
        {
            foreach (var path in paths)
            {
                process.StandardInput.Write(path + '\0');
            }

            process.StandardInput.Close();
        });
        var output = process.StandardOutput.ReadToEnd();
        watch.ThrowIfStopped(directory, "check-ignore");
        asked.Wait();
        process.WaitForExit();

        // check-ignore exits 1 when it ignores none of them.
        if (process.ExitCode is not (ExitCodes.Ok or NoneIgnored))
        {
            throw BrainException.Failed($"git could not say which of {paths.Count} paths it ignores in {directory}: {errors.Result.Trim()}");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every file of the checkout under <paramref name="directory"/> that git tracks or does not ignore, relative to it.</summary>
    public static IReadOnlyList<string> Files(string directory)
    {
        var run = Run(directory, "ls-files", "-z", "--cached", "--others", "--exclude-standard");
        ThrowIfFailed(run.Ok ? null : $"git could not list the files in {directory}: {run.Errors.Trim()}");
        return [.. run.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Reads up to <paramref name="count"/> objects of <c>git cat-file --batch</c> into <paramref name="texts"/>.</summary>
    private static void Read(Stream output, int count, List<string?> texts)
    {
        for (var i = 0; i < count; i++)
        {
            var header = ReadLine(output);
            if (header is null)
            {
                break;
            }

            var parts = header.Split(' ');
            if (parts.Length != 3 || !long.TryParse(parts[2], out var size))
            {
                texts.Add(null);
                continue;
            }

            var bytes = new byte[size];
            output.ReadExactly(bytes);
            output.ReadByte();
            texts.Add(parts[1] == "blob" ? Encoding.UTF8.GetString(bytes) : null);
        }
    }

    /// <summary>Refuses when <paramref name="repo"/> has anything staged, or is no repo whose index git can read.</summary>
    public static void RefuseStaged(string repo)
    {
        var staged = Run(repo, "diff", "--cached", "--name-only");
        ThrowIfFailed(staged.Code != ExitCodes.Ok ? $"git could not read {repo}'s index: {staged.Errors.Trim()}" : null);
        BrainException.ThrowIf(staged.Output.Trim().Length > 0 ? $"{repo} has other changes staged; commit or unstage them first" : null);
    }

    /// <summary>
    /// Commits what changed under <paramref name="path"/> and nothing else, leaving its child named
    /// <paramref name="skip"/> alone when one is named, and returns the new commit's hash. Refuses when the repo already
    /// has anything staged or when the path holds nothing new. A commit that fails leaves the index as it found it: empty.
    /// </summary>
    public static string CommitPath(string repo, string path, string? skip, string message)
    {
        var relative = Path.GetRelativePath(repo, path);
        RefuseStaged(repo);
        try
        {
            var add = Run(repo, "add", "-A", "--", relative);
            ThrowIfFailed(add.Code != ExitCodes.Ok ? $"git add failed: {add.Errors.Trim()}" : null);
            if (skip is not null)
            {
                var reset = Run(repo, "reset", "-q", "--", Path.Combine(relative, skip));
                ThrowIfFailed(reset.Code != ExitCodes.Ok ? $"git reset failed: {reset.Errors.Trim()}" : null);
            }

            BrainException.ThrowIf(Run(repo, "diff", "--cached", "--quiet").Code == ExitCodes.Ok ? $"nothing new to commit in {relative}" : null);
            var commit = Run(repo, "commit", "-q", "-m", message);
            ThrowIfFailed(commit.Code != ExitCodes.Ok ? $"git commit failed: {commit.Errors.Trim()}{commit.Output.Trim()}" : null);
        }
        catch (BrainException)
        {
            Run(repo, "reset", "-q", "--", relative);
            throw;
        }

        return Run(repo, RevParse, "HEAD").Output.Trim();
    }

    /// <summary>
    /// The repo <paramref name="file"/> lies in and its path there as git names it, from the repo's top; null when it
    /// lies in no repo. The file and its directories may be gone: the nearest directory that exists is asked.
    /// </summary>
    public static (string Repo, string Path)? Place(string file)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var rest = Path.GetFileName(file);
        while (!Directory.Exists(directory))
        {
            rest = Path.GetFileName(directory) + "/" + rest;
            directory = Path.GetDirectoryName(directory)!;
        }

        if (TopLevel(directory) is not { } repo || Prefix(directory) is not { } prefix)
        {
            return null;
        }

        return (repo, prefix + rest);
    }

    /// <summary>
    /// Commits <paramref name="paths"/>, each from the repo's top, as the working tree holds them and nothing else: a
    /// file that exists as it reads, one that is gone as removed. It commits from an index of its own made from HEAD, so
    /// whatever else is staged or changed stays as it was, and then gives the repo's own index those paths as committed.
    /// Returns the new commit's hash, or null when HEAD holds the paths as they read already.
    /// </summary>
    public static string? CommitFiles(string repo, IReadOnlyList<string> paths, string message)
    {
        var index = Path.Combine(Path.GetTempPath(), "brain-index-" + Guid.NewGuid().ToString("N"));
        string[] update = ["update-index", "--add", "--remove", "--", .. paths];
        try
        {
            if (Head(repo) is not null)
            {
                var read = RunWith(repo, index, "read-tree", "HEAD");
                ThrowIfFailed(read.Ok ? null : $"git could not read HEAD in {repo}: {read.Errors.Trim()}");
            }

            var staged = RunWith(repo, index, update);
            ThrowIfFailed(staged.Ok ? null : $"git could not stage {string.Join(", ", paths)} in {repo}: {staged.Errors.Trim()}");
            if (RunWith(repo, index, "diff", "--cached", "--quiet").Ok)
            {
                return null;
            }

            var commit = RunWith(repo, index, "commit", "-q", "-m", message);
            ThrowIfFailed(commit.Ok ? null : $"git commit failed in {repo}: {commit.Errors.Trim()}{commit.Output.Trim()}");
        }
        finally
        {
            File.Delete(index);
        }

        var own = Run(repo, update);
        ThrowIfFailed(own.Ok ? null : $"git committed in {repo}, but could not stage {string.Join(", ", paths)} as committed: {own.Errors.Trim()}");
        return Run(repo, RevParse, "HEAD").Output.Trim();
    }

    /// <summary>Whether git tracks anything at or under <paramref name="path"/>, a path that may be gone from the working tree.</summary>
    public static bool Tracks(string repo, string path) =>
        Run(repo, "ls-files", "--", Path.GetRelativePath(repo, path)).Output.Trim().Length > 0;

    /// <summary>Makes <paramref name="directory"/> a new git repo, refusing when git fails.</summary>
    public static void Init(string directory)
    {
        var init = Run(directory, "init", "-q");
        ThrowIfFailed(init.Code != ExitCodes.Ok ? $"git init failed in {directory}: {init.Errors.Trim()}" : null);
    }

    /// <summary>Throws <paramref name="problem"/> as a failure of git; null is no problem.</summary>
    private static void ThrowIfFailed(string? problem)
    {
        if (problem is not null)
        {
            throw BrainException.Failed(problem);
        }
    }

    /// <summary>One line of git's output, without its newline; null at the end.</summary>
    private static string? ReadLine(Stream stream)
    {
        var line = new List<byte>();
        for (var next = stream.ReadByte(); next >= 0; next = stream.ReadByte())
        {
            if (next == NodeSyntax.NewLine)
            {
                return Encoding.UTF8.GetString([.. line]);
            }

            line.Add((byte)next);
        }

        return line.Count == 0 ? null : Encoding.UTF8.GetString([.. line]);
    }

    /// <summary>Runs git in <paramref name="directory"/> and returns what it said, whatever its exit code.</summary>
    internal static GitRun Run(string directory, params string[] arguments) => RunWith(directory, null, arguments);

    /// <summary>
    /// Runs git in <paramref name="directory"/> on the index at <paramref name="index"/>, or the repo's own when it is
    /// null, and returns what it said, whatever its exit code.
    /// </summary>
    internal static GitRun RunWith(string directory, string? index, params string[] arguments)
    {
        using var process = Start(directory, arguments, input: false, index);
        using var watch = new Watch(process);
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        watch.ThrowIfStopped(directory, string.Join(' ', arguments));
        return new GitRun(process.ExitCode, output, errors.Result);
    }

    /// <summary>
    /// Starts git in <paramref name="directory"/>, its messages in English so <see cref="GitRun.NotARepo"/> reads them. It
    /// never asks for a password on a terminal: a push that needs one fails and says why.
    /// </summary>
    private static Process Start(string directory, IReadOnlyList<string> arguments, bool input, string? index = null)
    {
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardInput = input,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = input ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) : null,
        };
        start.Environment["LC_ALL"] = "C";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (index is not null)
        {
            start.Environment["GIT_INDEX_FILE"] = index;
        }

        start.ArgumentList.Add(InDirectory);
        start.ArgumentList.Add(directory);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(start) ?? throw BrainException.Failed("git did not start; install git or put it on PATH");
        }
        catch (Win32Exception error)
        {
            throw BrainException.Failed($"git did not start ({error.Message}); install git or put it on PATH", error);
        }
    }

    /// <summary>Stops git once it ran for <see cref="Limit"/>, or for <c>limit</c>: what it printed until then ends, and the run throws.</summary>
    internal sealed class Watch : IDisposable
    {
        private readonly Timer timer;
        private readonly TimeSpan limit;
        private int stopped;

        public Watch(Process process, TimeSpan? limit = null)
        {
            this.limit = limit ?? Limit;
            timer = new Timer(_ =>
            {
                Interlocked.Exchange(ref stopped, 1);
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // git ended on its own just now; the run reads what it said.
                }
            }, null, this.limit, Timeout.InfiniteTimeSpan);
        }

        public void ThrowIfStopped(string directory, string command)
        {
            if (Volatile.Read(ref stopped) == 1)
            {
                throw BrainException.Failed($"git {command} in {directory} ran for {limit.TotalSeconds:0} seconds and was stopped; see what holds the repo, such as a lock or a prompt");
            }
        }

        public void Dispose() => timer.Dispose();
    }

    /// <summary>One git run: its exit code, what it printed and what it said on stderr.</summary>
    internal sealed record GitRun(int Code, string Output, string Errors)
    {
        public bool Ok => Code == ExitCodes.Ok;

        /// <summary>True when git said the directory lies in no repo, or in none with a work tree.</summary>
        public bool NotARepo => Errors.Contains("not a git repository", StringComparison.Ordinal)
            || Errors.Contains("must be run in a work tree", StringComparison.Ordinal);

        /// <summary>The run's output when it succeeded, null for <paramref name="expected"/>, and a failure for anything else.</summary>
        public string? Answer(string directory, string question, bool expected) =>
            Ok ? Output.Trim() : expected ? null : throw Failed(directory, question);

        public BrainException Failed(string directory, string question) =>
            BrainException.Failed($"git could not {question} in {directory}: {Errors.Trim()}");
    }
}
