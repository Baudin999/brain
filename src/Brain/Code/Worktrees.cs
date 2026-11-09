using System.Text.RegularExpressions;

namespace Brain.Code;

/// <summary>
/// The worktree an agent builds in, read from what its work did: the worktree Claude Code gave it, as its meta names, or
/// the one its own Bash call added with <c>git worktree add</c>, opened when that call ran; and removed when a Bash call
/// of the agent or of a validator of its work ran <c>git worktree remove</c> on it, or once its folder is gone. A call
/// whose result was an error opened or removed nothing. A relative path is in the folder of <c>git -C</c>, else of the
/// command line's last <c>cd</c>, else of the session's folder; a path the shell would expand from a variable is none.
/// </summary>
internal static partial class Worktrees
{
    private const string Bash = "Bash";
    private const string Add = "add";
    private const string Remove = "remove";

    /// <summary>The words that end one command of a Bash line and start the next.</summary>
    private static readonly string[] Separators = ["&&", "||", ";", "\n", "|"];

    /// <summary>The options of <c>git worktree add</c> that take the next word as their value.</summary>
    private static readonly HashSet<string> Valued = new(StringComparer.Ordinal) { "-b", "-B", "--reason" };

    /// <summary>
    /// The worktree of an agent that started at <paramref name="started"/>, given by <paramref name="given"/> or opened by
    /// its own Bash calls in <paramref name="agent"/>, and whether it or one of <paramref name="others"/> removed it; null
    /// for an agent that has none. <paramref name="cwd"/> is the folder the session ran in.
    /// </summary>
    // navarone: 0000218
    public static CodeWorktree? Of(string? given, DateTimeOffset? started, IReadOnlyList<CodeEvent> agent, IReadOnlyList<IReadOnlyList<CodeEvent>> others, string? cwd)
    {
        var path = given;
        var opened = started;
        if (path is null)
        {
            var adds = Ran(agent, Add, cwd);
            if (adds.Count == 0)
            {
                return null;
            }

            (path, opened) = adds[0];
        }

        var removedAt = RemovedAt(path, others.Prepend(agent), cwd);
        var removed = removedAt is not null || !Directory.Exists(path);
        return new CodeWorktree(path, opened, removed, removedAt);
    }

    /// <summary>When the first Bash call of <paramref name="agents"/> that removed the worktree at <paramref name="path"/> ran; null when none did.</summary>
    private static DateTimeOffset? RemovedAt(string path, IEnumerable<IReadOnlyList<CodeEvent>> agents, string? cwd)
    {
        foreach (var events in agents)
        {
            foreach (var (removed, at) in Ran(events, Remove, cwd))
            {
                if (removed == path)
                {
                    return at;
                }
            }
        }

        return null;
    }

    /// <summary>Each worktree a Bash call of <paramref name="events"/> ran <c>git worktree</c> <paramref name="verb"/> on, with when it ran, the earliest first; a call whose result was an error is left out.</summary>
    private static List<(string Path, DateTimeOffset? At)> Ran(IReadOnlyList<CodeEvent> events, string verb, string? cwd)
    {
        var failed = CodeEvent.FailedCalls(events);
        var ran = new List<(string, DateTimeOffset?)>();
        foreach (var call in events)
        {
            if (call.Kind != CodeEvent.Tool || call.Name != Bash || failed.Contains(call.Id ?? string.Empty))
            {
                continue;
            }

            var command = call.Input?.GetValueOrDefault("command") ?? string.Empty;
            if (Named(command, verb, cwd) is { } path)
            {
                ran.Add((path, call.At));
            }
        }

        return ran;
    }

    /// <summary>
    /// The full path of the worktree a <c>git worktree</c> command of <paramref name="verb"/> in the Bash line
    /// <paramref name="command"/> names, or null when the line runs none.
    /// </summary>
    internal static string? Named(string command, string verb, string? cwd)
    {
        var folder = cwd;
        foreach (var part in command.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cd = ChangeFolder().Match(part);
            if (cd.Success)
            {
                folder = Resolved(cd.Groups["to"].Value, folder);
                continue;
            }

            var git = WorktreeCommand().Match(part);
            if (!git.Success || git.Groups["verb"].Value != verb)
            {
                continue;
            }

            if (PathAfterOptions(git.Groups["rest"].Value) is not { } path)
            {
                continue;
            }

            var under = folder;
            if (git.Groups["git"].Success)
            {
                under = Resolved(git.Groups["git"].Value, folder);
            }

            return Resolved(path, under);
        }

        return null;
    }

    /// <summary>The first word of <paramref name="words"/> that is no option and no option's value; null when there is none.</summary>
    private static string? PathAfterOptions(string words)
    {
        var all = words.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (var at = 0; at < all.Length; at++)
        {
            if (Valued.Contains(all[at]))
            {
                at++;
                continue;
            }

            if (!all[at].StartsWith('-'))
            {
                return all[at];
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="path"/> as a full path with no slash at its end: a leading <c>~</c> is the home folder, and a
    /// relative path is taken in <paramref name="folder"/>. Null for a path the shell would expand from a variable, and
    /// for a relative one with no folder known.
    /// </summary>
    private static string? Resolved(string path, string? folder)
    {
        var full = path.Trim('"', '\'');
        if (full.Contains('$', StringComparison.Ordinal))
        {
            return null;
        }

        if (full == "~" || full.StartsWith("~/", StringComparison.Ordinal))
        {
            full = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + full[1..];
        }

        if (Path.IsPathRooted(full))
        {
            return Path.TrimEndingDirectorySeparator(full);
        }

        if (folder is null)
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(folder, full)));
    }

    /// <summary>A command that changes the folder: <c>cd folder</c>.</summary>
    [GeneratedRegex(@"^cd\s+(?<to>\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChangeFolder();

    /// <summary>A <c>git [-C folder] worktree add|remove</c> command, with the words after its verb.</summary>
    [GeneratedRegex(@"^git(?:\s+-C\s+(?<git>\S+))?\s+worktree\s+(?<verb>add|remove)\s+(?<rest>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex WorktreeCommand();
}
