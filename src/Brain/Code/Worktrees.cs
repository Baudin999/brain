using System.Text.RegularExpressions;

namespace Brain.Code;

/// <summary>
/// The worktree an agent builds in, read from what its work did: the worktree Claude Code gave it, as its meta names, or
/// the one its own Bash call added with <c>git worktree add</c>, opened when that call ran; and removed when a Bash call
/// of the agent or of a validator of its work ran <c>git worktree remove</c> on it, or once its folder is gone. A call
/// whose result was an error opened or removed nothing. A relative path is in the folder of <c>git -C</c>, else of the
/// command line's last <c>cd</c>, else of the session's folder.
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
            (path, opened) = Ran(agent, Add, cwd);
        }

        if (path is null)
        {
            return null;
        }

        DateTimeOffset? removedAt = null;
        foreach (var events in others.Prepend(agent))
        {
            var (removed, at) = Ran(events, Remove, cwd);
            if (removed == path)
            {
                removedAt = at;
                break;
            }
        }

        var gone = removedAt is not null || !Directory.Exists(path);
        return new CodeWorktree(path, opened, gone, removedAt);
    }

    /// <summary>The worktree the first Bash call of <paramref name="events"/> that ran <c>git worktree</c> <paramref name="verb"/> named, and when it ran.</summary>
    private static (string? Path, DateTimeOffset? At) Ran(IReadOnlyList<CodeEvent> events, string verb, string? cwd)
    {
        var failed = SentAgents.FailedCalls(events);
        foreach (var call in events)
        {
            if (call.Kind != CodeEvent.Tool || call.Name != Bash || failed.Contains(call.Id ?? string.Empty))
            {
                continue;
            }

            var command = call.Input?.GetValueOrDefault("command") ?? string.Empty;
            if (Named(command, verb, cwd) is { } path)
            {
                return (path, call.At);
            }
        }

        return (null, null);
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

    /// <summary><paramref name="path"/> as a full path, a relative one taken in <paramref name="folder"/>, with no slash at its end.</summary>
    private static string Resolved(string path, string? folder)
    {
        var full = path.Trim('"', '\'');
        if (!Path.IsPathRooted(full) && folder is not null)
        {
            full = Path.GetFullPath(Path.Combine(folder, full));
        }

        return Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>A command that changes the folder: <c>cd folder</c>.</summary>
    [GeneratedRegex(@"^cd\s+(?<to>\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChangeFolder();

    /// <summary>A <c>git [-C folder] worktree add|remove</c> command, with the words after its verb.</summary>
    [GeneratedRegex(@"^git(?:\s+-C\s+(?<git>\S+))?\s+worktree\s+(?<verb>add|remove)\s+(?<rest>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex WorktreeCommand();
}
