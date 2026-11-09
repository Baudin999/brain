using System.Text;

namespace Brain.Code;

/// <summary>
/// Where Claude Code keeps what brain code reads, under its config folder, <c>$CLAUDE_CONFIG_DIR</c> or
/// <c>~/.claude</c>: a session's transcript, <c>projects/&lt;folder&gt;/&lt;session id&gt;.jsonl</c>, the folder named
/// for the directory the session runs in, and the agents a session can start with, the files of <c>agents</c>. The
/// transcripts of the agents a session sent lie beside its own, as <see cref="Transcript"/> finds them.
/// </summary>
internal sealed class ClaudeFiles(string config)
{
    private const string Extension = ".jsonl";

    /// <summary>Claude Code's config folder for the user whose home is <paramref name="userHome"/>.</summary>
    public static ClaudeFiles Of(string userHome)
    {
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrEmpty(folder))
        {
            folder = Path.Combine(userHome, ".claude");
        }

        return new ClaudeFiles(folder);
    }

    /// <summary>True when <paramref name="id"/> is a session id, as Claude Code names them: a GUID.</summary>
    public static bool IsSessionId(string id) => Guid.TryParseExact(id, "D", out _);

    /// <summary>The transcript of the session <paramref name="id"/>, in whichever project folder holds it, or null when none does yet.</summary>
    public string? Session(string id)
    {
        var projects = Path.Combine(config, "projects");
        if (!IsSessionId(id) || !Directory.Exists(projects))
        {
            return null;
        }

        foreach (var folder in Directory.EnumerateDirectories(projects))
        {
            var transcript = Path.Combine(folder, id + Extension);
            if (File.Exists(transcript))
            {
                return transcript;
            }
        }

        return null;
    }

    /// <summary>The transcripts of the sessions that ran in <paramref name="folder"/>, newest first; a subagent's, which lies below a session's own folder, is none of them.</summary>
    public IReadOnlyList<FileInfo> Past(string folder)
    {
        var project = Path.Combine(config, "projects", ProjectName(folder));
        if (!Directory.Exists(project))
        {
            return [];
        }

        var files = new DirectoryInfo(project).EnumerateFiles("*" + Extension).Where(file => IsSessionId(Path.GetFileNameWithoutExtension(file.Name)));
        return [.. files.OrderByDescending(file => file.LastWriteTimeUtc)];
    }

    /// <summary>The name Claude Code gives the project folder of <paramref name="folder"/>: every character but a letter or a digit becomes a dash.</summary>
    private static string ProjectName(string folder)
    {
        var name = new StringBuilder(folder.Length);
        foreach (var character in folder)
        {
            name.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        return name.ToString();
    }

    /// <summary>The name of each agent file of Claude Code's agents folder, sorted.</summary>
    public IReadOnlyList<string> AgentNames()
    {
        var agents = Path.Combine(config, "agents");
        if (!Directory.Exists(agents))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(agents, "*.md").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];
    }
}
