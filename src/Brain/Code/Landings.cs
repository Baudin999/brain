using System.Collections.Concurrent;
using Brain.Windows;

namespace Brain.Code;

/// <summary>
/// Whether a piece of work landed, asked of git in the repo at <paramref name="repo"/>: the default branch, the branch
/// the primary checkout is on, landed the work when its reflog holds a fast-forward of the work's branch, as the
/// validator's <c>git merge --ff-only &lt;branch&gt;</c> writes it, after the builder started and by the time the last
/// validator reported, as a validator lands before it reports. The reflog keeps it once the worktree is removed and the
/// branch deleted, and its range holds the commits as they landed, rebased or not. Each git call is a record. The
/// reflog is read once for the life of this instance, and the commits of a range, which never change, once for the
/// life of the process.
/// </summary>
internal sealed class Landings(string repo, Records records)
{
    private const string FastForward = ": Fast-forward";
    private const string Merge = "merge ";
    private const string BranchLine = "branch refs/heads/";

    private static readonly ConcurrentDictionary<(string Repo, string Range), IReadOnlyList<CodeCommit>> Known = new();

    private IReadOnlyList<Move>? moves;

    /// <summary>
    /// The steps of a work on <paramref name="branch"/> whose builder started at <paramref name="since"/>; when the work
    /// landed, its last validator's report carries the range and the commits the landing put on the default branch.
    /// </summary>
    // navarone: 0000220
    public IReadOnlyList<CodeStep> Told(IReadOnlyList<CodeStep> steps, string? branch, DateTimeOffset? since)
    {
        if (branch is null)
        {
            return steps;
        }

        var report = LastVerdict(steps);
        if (report < 0)
        {
            return steps;
        }

        var range = LandingOf(branch, since, steps[report].At);
        if (range is null)
        {
            return steps;
        }

        var told = steps.ToList();
        told[report] = told[report] with { Landed = range, Commits = Commits(range) };
        return told;
    }

    /// <summary>Where the last validator's report is among <paramref name="steps"/>; -1 when there is none.</summary>
    private static int LastVerdict(IReadOnlyList<CodeStep> steps)
    {
        for (var at = steps.Count - 1; at >= 0; at--)
        {
            if (steps[at].Kind == Trail.Verdict)
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>
    /// The range of the first fast-forward of <paramref name="branch"/> from <paramref name="since"/> to
    /// <paramref name="until"/>, <c>a..b</c>; null when there is none. With no time, either end is open. The reflog is
    /// newest first, so the last one met is the first.
    /// </summary>
    private string? LandingOf(string branch, DateTimeOffset? since, DateTimeOffset? until)
    {
        var said = Merge + branch + FastForward;
        var from = since?.ToUnixTimeSeconds() ?? long.MinValue;
        var to = until?.ToUnixTimeSeconds() ?? long.MaxValue;
        string? landed = null;
        foreach (var move in Moves())
        {
            if (move.Said != said)
            {
                continue;
            }

            if (move.At < from)
            {
                continue;
            }

            if (move.At > to)
            {
                continue;
            }

            landed = move.Range;
        }

        return landed;
    }

    /// <summary>Each move of the default branch, as its reflog holds it, the newest first; none when git cannot tell.</summary>
    private IReadOnlyList<Move> Moves()
    {
        moves ??= ReadMoves();
        return moves;
    }

    private List<Move> ReadMoves()
    {
        if (!Directory.Exists(repo))
        {
            return [];
        }

        var branch = DefaultBranch();
        if (branch is null)
        {
            return [];
        }

        var run = records.Time("git", "reflog " + branch, null, () => Git.Run(repo, "log", "-g", "--date=unix", "--format=%h%x09%gd%x09%gs", "refs/heads/" + branch));
        if (!run.Ok)
        {
            return [];
        }

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var read = new List<Move>();
        for (var at = 0; at + 1 < lines.Length; at++)
        {
            var entry = lines[at].Split('\t', 3);
            if (entry.Length != 3)
            {
                continue;
            }

            var when = When(entry[1]);
            if (when is null)
            {
                continue;
            }

            var before = lines[at + 1].Split('\t', 2)[0];
            read.Add(new Move(before + ".." + entry[0], when.Value, entry[2]));
        }

        return read;
    }

    /// <summary>The branch the primary checkout of the repo is on, the default branch; null when it is on none.</summary>
    private string? DefaultBranch()
    {
        var run = records.Time("git", "worktree list", null, () => Git.Run(repo, "worktree", "list", "--porcelain"));
        if (!run.Ok)
        {
            return null;
        }

        foreach (var line in run.Output.Split('\n'))
        {
            if (line.Length == 0)
            {
                return null;
            }

            if (line.StartsWith(BranchLine, StringComparison.Ordinal))
            {
                return line[BranchLine.Length..];
            }
        }

        return null;
    }

    /// <summary>The time of a reflog selector as git prints it with unix dates, <c>main@{1794556489}</c>; null for any other.</summary>
    private static long? When(string selector)
    {
        var open = selector.LastIndexOf('{');
        if (open < 0 || !selector.EndsWith('}'))
        {
            return null;
        }

        if (!long.TryParse(selector[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return seconds;
    }

    /// <summary>The commits of <paramref name="range"/>, the oldest first; none when git cannot list them.</summary>
    private IReadOnlyList<CodeCommit> Commits(string range)
    {
        if (Known.TryGetValue((repo, range), out var known))
        {
            return known;
        }

        var run = records.Time("git", "log " + range, null, () => Git.Run(repo, "log", "--reverse", "--format=%h%x09%s", range));
        if (!run.Ok)
        {
            return [];
        }

        var commits = new List<CodeCommit>();
        foreach (var line in run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
            {
                commits.Add(new CodeCommit(parts[0], parts[1]));
            }
        }

        Known[(repo, range)] = commits;
        return commits;
    }

    /// <summary>One move of the default branch: the range it moved by, when, in unix seconds, and what git said of it.</summary>
    private sealed record Move(string Range, long At, string Said);
}
