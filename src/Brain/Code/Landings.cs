using System.Collections.Concurrent;
using Brain.Windows;

namespace Brain.Code;

/// <summary>
/// The commits a landing put on the default branch of the repo at <paramref name="repo"/>: the range a validator's
/// fast-forward moved the branch by, as git lists it, the oldest first. Each git call is a record. The commits of a
/// range never change, so a range is asked of git once for the life of the process.
/// </summary>
internal sealed class Landings(string repo, Records records)
{
    private static readonly ConcurrentDictionary<(string Repo, string Range), IReadOnlyList<CodeCommit>> Known = new();

    /// <summary>The step with the commits it put on the default branch, for a step that landed the work; any other step as it is.</summary>
    public CodeStep Told(CodeStep step)
    {
        if (step.Landed is not { } range)
        {
            return step;
        }

        return step with { Commits = Commits(range) };
    }

    /// <summary>The commits of <paramref name="range"/>; none when git cannot list them, as when the repo is gone.</summary>
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
}
