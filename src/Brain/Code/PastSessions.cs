namespace Brain.Code;

/// <summary>
/// The open agent's past sessions in one folder, read from the transcripts Claude Code keeps: brain keeps no history
/// of its own. A session is the open agent's when its transcript names it, <c>agent-setting</c>, near its start; a
/// builder's or a validator's, a plain claude's and every subagent's transcript is left out. A past session is read
/// only: it can be read, never continued.
/// </summary>
internal sealed class PastSessions(ClaudeFiles claude)
{
    /// <summary>How many lines a transcript may begin with before it names its agent.</summary>
    private const int HeadLines = 30;

    private const int TitleLength = 120;

    /// <summary>The open agent's sessions that ran in <paramref name="folder"/>, newest first.</summary>
    public CodePastList List(string folder)
    {
        var past = new List<CodePast>();
        foreach (var file in claude.Past(folder))
        {
            if (Title(file.FullName) is { } title)
            {
                past.Add(new CodePast(Path.GetFileNameWithoutExtension(file.Name), title, file.LastWriteTimeUtc));
            }
        }

        return new CodePastList(past);
    }

    /// <summary>The conversation of the past session <paramref name="id"/> that ran in <paramref name="folder"/>; refused when it is none of the open agent's there.</summary>
    public CodeConversation Read(string folder, string id)
    {
        var path = claude.Past(folder).FirstOrDefault(file => Path.GetFileNameWithoutExtension(file.Name) == id)?.FullName;
        if (path is null || Title(path) is null)
        {
            throw new BrainException($"{id} is no past session of the open agent in {folder}");
        }

        return new CodeConversation(0, new Transcript(path).All(), Found: true);
    }

    /// <summary>The first words of the open agent's transcript <paramref name="path"/>, or null when it is another agent's or holds no words of the user.</summary>
    private static string? Title(string path)
    {
        if (AgentOf(path) != CodeSessions.OpenAgent)
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (FirstWords(line) is { } words)
            {
                return words;
            }
        }

        return null;
    }

    /// <summary>The agent a transcript names in its first lines, or null when it names none there.</summary>
    private static string? AgentOf(string path)
    {
        foreach (var line in File.ReadLines(path).Take(HeadLines))
        {
            var agent = Transcript.Field(line, "agentSetting");
            if (agent.Length > 0)
            {
                return agent;
            }
        }

        return null;
    }

    private static string? FirstWords(string line)
    {
        foreach (var one in Transcript.Events(line))
        {
            if (one.Kind != CodeEvent.User || string.IsNullOrWhiteSpace(one.Text))
            {
                continue;
            }

            return Headline(one.Text);
        }

        return null;
    }

    /// <summary>The first words of the user's <paramref name="prompt"/>: its first line, cut when long.</summary>
    public static string Headline(string prompt)
    {
        var first = prompt.Trim().Split('\n')[0].Trim();
        if (first.Length > TitleLength)
        {
            return first[..TitleLength] + "...";
        }

        return first;
    }
}
