using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Brain.Children.ClaudeStream;

namespace Brain.Code;

/// <summary>
/// One Claude Code transcript, a session's or an agent's it sent, read as brain's <see cref="CodeEvent"/>s. It reads
/// only what was added since it last read, so a long session costs its new lines only. Of each line it keeps what the
/// window draws, with when it was written: the user's words with the images he attached, the agent's text, each tool
/// call with what it works on, each result with the images it carries, each agent the session sent, and each message
/// another agent sent it, as a validator's findings reach its builder. Its thinking, its hooks' and harness's notes,
/// and Claude Code's own bookkeeping are left out.
///
/// An agent the session sent keeps its own transcript beside the session's, in
/// <c>&lt;session id&gt;/subagents/agent-&lt;agent id&gt;.jsonl</c>, and a <c>.meta.json</c> next to it whose
/// <c>toolUseId</c> is the id of the Agent call that sent it.
/// </summary>
internal sealed partial class Transcript(string path)
{
    /// <summary>The most of a tool's result the window is sent: its head, enough to see what it said.</summary>
    private const int ResultLength = 12000;
    private const int InputLength = 12000;
    private const int SubjectLength = 200;
    private const string Extension = ".jsonl";
    private const string Meta = ".meta.json";
    private const string AgentPrefix = "agent-";

    /// <summary>What Claude Code writes as the user's words when he interrupted a turn, with or without more after it.</summary>
    private const string InterruptedNote = "[Request interrupted by user";

    /// <summary>The type of an agent sent without one.</summary>
    private const string GeneralPurpose = "general-purpose";

    /// <summary>The inputs that say what a tool call works on, the first a call has; <c>to</c> names whom a message goes to.</summary>
    private static readonly string[] ToolSubjects = ["description", "command", "file_path", "pattern", "query", "url", "skill", "prompt", "to"];

    /// <summary>The tools whose call sends an agent: Agent, and Task, its older name.</summary>
    private static readonly string[] AgentTools = ["Agent", "Task"];

    private static long readings;

    private readonly Lock gate = new();
    private readonly List<CodeEvent> events = [];
    private readonly HashSet<string> spoken = new(StringComparer.Ordinal);
    private readonly BrainCalls brainCalls = new();
    private readonly StringBuilder partial = new();
    private long offset;
    private string generation = NextGeneration();

    /// <summary>The transcript's file.</summary>
    public string Location { get; } = path;

    /// <summary>The folder the agents the session sent keep their transcripts in.</summary>
    public string Subagents => Path.Combine(Path.ChangeExtension(Location, null), "subagents");

    /// <summary>
    /// The events from the one numbered <paramref name="from"/> on, with what was written since the last read, when
    /// <paramref name="seen"/> is this reading's generation. Every event from the first comes back when the reader holds
    /// another reading, or more events than there are: the file was written anew, or the reader read another file.
    /// <paramref name="live"/>, the text the agent is writing now, comes with them until this reading holds the text of
    /// its message, so the window draws it once: live, or as the event it became.
    /// </summary>
    // navarone: 0000166
    // navarone: 0000178
    public CodeConversation From(int from, string? seen, CodeLive? live = null)
    {
        lock (gate)
        {
            Read();
            var start = from;
            if (seen != generation || from < 0 || from > events.Count)
            {
                start = 0;
            }

            string? writing = null;
            if (live is not null && !spoken.Contains(live.Message))
            {
                writing = live.Text;
            }

            return new CodeConversation(start, events[start..], Found: true, generation) { Live = writing };
        }
    }

    /// <summary>How many events the transcript holds, with what was written since the last read.</summary>
    public int Count()
    {
        lock (gate)
        {
            Read();
            return events.Count;
        }
    }

    /// <summary>Every event of the transcript, with what was written since the last read.</summary>
    public IReadOnlyList<CodeEvent> All()
    {
        lock (gate)
        {
            Read();
            return [.. events];
        }
    }

    /// <summary>Reads what the file holds past the last read; a line still being written waits for its end.</summary>
    // navarone: 0000187
    private void Read()
    {
        using var stream = new FileStream(Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < offset)
        {
            // The file was written anew; read it from the start, as a new reading.
            offset = 0;
            partial.Clear();
            events.Clear();
            spoken.Clear();
            brainCalls.Clear();
            generation = NextGeneration();
        }

        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var text = reader.ReadToEnd();
        offset = stream.Length;
        partial.Append(text);
        var whole = partial.ToString();
        var end = whole.LastIndexOf('\n');
        if (end < 0)
        {
            return;
        }

        partial.Clear();
        partial.Append(whole[(end + 1)..]);
        foreach (var line in whole[..end].Split('\n'))
        {
            var (said, message) = Parse(line);
            events.AddRange(said.Select(brainCalls.Itemised));
            if (message is not null && said.Any(one => one.Kind == CodeEvent.Said))
            {
                spoken.Add(message);
            }
        }
    }

    /// <summary>The folder the transcript says its session ran in, from the first line that names one, or null.</summary>
    public string? Cwd()
    {
        foreach (var line in File.ReadLines(Location))
        {
            var cwd = Field(line, "cwd");
            if (cwd.Length > 0)
            {
                return cwd;
            }
        }

        return null;
    }

    /// <summary>
    /// The id of the Agent call that sent the subagent <paramref name="agentId"/>, from its
    /// <c>agent-&lt;agent id&gt;.meta.json</c>, or null when there is none yet.
    /// </summary>
    public string? SentBy(string agentId)
    {
        if (!AgentIdShape().IsMatch(agentId))
        {
            return null;
        }

        var meta = Path.Combine(Subagents, AgentPrefix + agentId + Meta);
        if (!File.Exists(meta))
        {
            return null;
        }

        return AgentMeta(File.ReadAllText(meta), agentId, meta[..^Meta.Length] + Extension)?.ToolUseId;
    }

    /// <summary>Each agent the session sent, by its <c>.meta.json</c>, in no set order.</summary>
    public IReadOnlyList<SentMeta> Sent()
    {
        if (!Directory.Exists(Subagents))
        {
            return [];
        }

        var sent = new List<SentMeta>();
        foreach (var meta in Directory.EnumerateFiles(Subagents, AgentPrefix + "*" + Meta))
        {
            var agent = Path.GetFileName(meta)[AgentPrefix.Length..^Meta.Length];
            var transcript = meta[..^Meta.Length] + Extension;
            if (AgentMeta(File.ReadAllText(meta), agent, transcript) is { } read)
            {
                sent.Add(read);
            }
        }

        return sent;
    }

    /// <summary>
    /// What a <c>.meta.json</c> says of its agent <paramref name="agent"/>, whose transcript is
    /// <paramref name="transcript"/>: the id of the Agent call that sent it, its type and description, the worktree it
    /// builds in, whether it finished, and whether the user stopped it, as an interrupt of the session's turn stops it:
    /// Claude Code marks that <c>stoppedByUser</c> and does not always write <c>finished</c>. Null for a meta that is no
    /// JSON object or names no call.
    /// </summary>
    private static SentMeta? AgentMeta(string json, string agent, string transcript)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var toolUseId = Text(root, "toolUseId");
            if (toolUseId.Length == 0)
            {
                return null;
            }

            var type = Text(root, "agentType");
            if (type.Length == 0)
            {
                type = GeneralPurpose;
            }

            string? worktree = Text(root, "worktreePath");
            if (worktree.Length == 0)
            {
                worktree = null;
            }

            var finished = IsTrue(root, "finished");
            var stopped = IsTrue(root, "stoppedByUser");
            return new SentMeta(agent, toolUseId, type, Text(root, "description"), worktree, finished, stopped, transcript);
        }
    }

    /// <summary>The events one line of a transcript holds: none for a line that is no message the window draws.</summary>
    internal static IReadOnlyList<CodeEvent> Events(string line) => Parse(line).Events;

    /// <summary>The events one line of a transcript holds, and the id of the message it is part of, when it names one.</summary>
    // navarone: 0000188
    private static (IReadOnlyList<CodeEvent> Events, string? Message) Parse(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return ([], null);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ([], null);
            }

            var at = At(root);
            if (Peer(root, at) is { } peer)
            {
                return ([peer], null);
            }

            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
            {
                return ([], null);
            }

            if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                return ([], null);
            }

            (List<CodeEvent> Events, string? Message) said = Text(root, "type") switch
            {
                "user" => (FromUser(message), null),
                "assistant" => (FromAgent(message), MessageId(message)),
                _ => ([], null),
            };

            return ([.. said.Events.Select(one => one with { At = at })], said.Message);
        }
    }

    /// <summary>The id of the message a line is part of, or null when it names none.</summary>
    private static string? MessageId(JsonElement message)
    {
        var id = Text(message, "id");
        if (id.Length == 0)
        {
            return null;
        }

        return id;
    }

    /// <summary>When a line was written, or null for a line that does not say.</summary>
    private static DateTimeOffset? At(JsonElement root)
    {
        if (!DateTimeOffset.TryParse(Text(root, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return null;
        }

        return at;
    }

    /// <summary>
    /// The message another agent sent, as a validator's findings reach its builder: a line whose <c>origin</c> is a
    /// peer's, with the sender's id, its name and the message whole. Null for any other line.
    /// </summary>
    private static CodeEvent? Peer(JsonElement root, DateTimeOffset? at)
    {
        if (!root.TryGetProperty("origin", out var origin) || origin.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Text(origin, "kind") != "peer")
        {
            return null;
        }

        var body = Text(origin, "body").Trim();
        if (body.Length == 0)
        {
            return null;
        }

        return new CodeEvent(CodeEvent.Message, Text: body, Id: Text(origin, "from"), Name: Text(origin, "name"), At: at);
    }

    /// <summary>What a user line holds: the user's words, or the results of the agent's tool calls.</summary>
    private static List<CodeEvent> FromUser(JsonElement message)
    {
        var said = new List<CodeEvent>();
        if (!message.TryGetProperty("content", out var content))
        {
            return said;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            AddWords(said, content.GetString()!);
            return said;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return said;
        }

        foreach (var block in content.EnumerateArray())
        {
            var kind = Text(block, "type");
            if (kind == "text")
            {
                AddWords(said, Text(block, "text"));
            }

            if (kind == "tool_result")
            {
                var failed = block.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
                block.TryGetProperty("content", out var gave);
                var images = ImagesOf(gave);
                said.Add(new CodeEvent(CodeEvent.Result, Text: Cut(ResultText(block), ResultLength), Id: Text(block, "tool_use_id"), Failed: failed, Images: images));
            }
        }

        return WithImages(said, ImagesOf(content));
    }

    /// <summary>
    /// The user's words with the images he attached: they go with his first words, or stand as words of their own when
    /// he wrote none.
    /// </summary>
    private static List<CodeEvent> WithImages(List<CodeEvent> said, IReadOnlyList<string>? images)
    {
        if (images is null)
        {
            return said;
        }

        var first = said.FindIndex((one) => one.Kind == CodeEvent.User);
        if (first < 0)
        {
            said.Add(new CodeEvent(CodeEvent.User, Text: string.Empty, Images: images));
            return said;
        }

        said[first] = said[first] with { Images = images };
        return said;
    }

    /// <summary>
    /// The images among the blocks of <paramref name="content"/>, each as a data URL of its media type and its base64
    /// bytes; null when it holds none.
    /// </summary>
    private static List<string>? ImagesOf(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var images = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (DataUrl(block) is { } image)
            {
                images.Add(image);
            }
        }

        if (images.Count == 0)
        {
            return null;
        }

        return images;
    }

    /// <summary>An image block as a data URL, or null for any other block, and for an image that is no base64 image.</summary>
    private static string? DataUrl(JsonElement block)
    {
        if (Text(block, "type") != "image")
        {
            return null;
        }

        if (!block.TryGetProperty("source", out var source))
        {
            return null;
        }

        if (Text(source, "type") != "base64")
        {
            return null;
        }

        var type = Text(source, "media_type");
        if (!type.StartsWith("image/", StringComparison.Ordinal))
        {
            return null;
        }

        var data = Text(source, "data");
        if (data.Length == 0)
        {
            return null;
        }

        return "data:" + type + ";base64," + data;
    }

    /// <summary>
    /// The user's words, unless they are the harness's: a line that opens with a tag, as a slash command's output or a
    /// note the harness put in, is no words of the user's, and Claude Code's note that the user interrupted the turn is
    /// an interruption.
    /// </summary>
    private static void AddWords(List<CodeEvent> said, string words)
    {
        var trimmed = words.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('<'))
        {
            return;
        }

        if (trimmed.StartsWith(InterruptedNote, StringComparison.Ordinal))
        {
            said.Add(new CodeEvent(CodeEvent.Interrupted));
            return;
        }

        said.Add(new CodeEvent(CodeEvent.User, Text: trimmed));
    }

    /// <summary>What an assistant line holds: the agent's text, and each tool call, an agent it sent among them. Its thinking is left out.</summary>
    private static List<CodeEvent> FromAgent(JsonElement message)
    {
        var said = new List<CodeEvent>();
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return said;
        }

        foreach (var block in content.EnumerateArray())
        {
            var kind = Text(block, "type");
            if (kind == "text" && Text(block, "text").Trim() is { Length: > 0 } text)
            {
                said.Add(new CodeEvent(CodeEvent.Said, Text: text));
            }

            if (kind == "tool_use")
            {
                said.Add(ToolCall(block));
            }
        }

        return said;
    }

    /// <summary>
    /// A tool call: an agent sent, by its type and what it was asked, or a tool, by its name and what it works on. A
    /// call that sends a message, to another agent or as the agent's report, keeps the message whole as its text, and
    /// every call keeps its input fields.
    /// </summary>
    private static CodeEvent ToolCall(JsonElement block)
    {
        var id = Text(block, "id");
        var name = Text(block, "name");
        var input = block.TryGetProperty("input", out var given) ? given : default;
        if (AgentTools.Contains(name, StringComparer.Ordinal))
        {
            var type = Text(input, "subagent_type");
            if (type.Length == 0)
            {
                type = GeneralPurpose;
            }

            return new CodeEvent(CodeEvent.Agent, Id: id, Name: type, Subject: Cut(Text(input, "description"), SubjectLength));
        }

        var subject = string.Empty;
        foreach (var key in ToolSubjects)
        {
            subject = Text(input, key);
            if (subject.Length > 0)
            {
                break;
            }
        }

        var line = subject.Split('\n')[0];
        BrainQuery? brain = null;
        if (name == "Bash")
        {
            brain = BrainQuery.Of(Text(input, "command"));
        }

        if (brain is not null)
        {
            line = brain.Asked;
        }

        return new CodeEvent(CodeEvent.Tool, Text: Whole(input), Id: id, Name: name, Subject: Cut(line, SubjectLength), Input: InputOf(input), Brain: brain?.Verb);
    }

    /// <summary>Each field of a tool call's input by its name, as text: a string whole, any other value as its JSON, each cut.</summary>
    private static Dictionary<string, string> InputOf(JsonElement input)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (input.ValueKind != JsonValueKind.Object)
        {
            return fields;
        }

        foreach (var field in input.EnumerateObject())
        {
            var text = field.Value.GetRawText();
            if (field.Value.ValueKind == JsonValueKind.String)
            {
                text = field.Value.GetString()!;
            }

            fields[field.Name] = Cut(text, InputLength);
        }

        return fields;
    }

    /// <summary>The message a tool call sends, kept whole; null for a call that sends none.</summary>
    private static string? Whole(JsonElement input)
    {
        var text = Text(input, "message").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text;
    }

    /// <summary>A tool result's text: a string, or the text of each of its blocks.</summary>
    private static string ResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString()!;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var texts = content.EnumerateArray().Select(part => Text(part, "text")).Where(text => text.Length > 0);
        return string.Join('\n', texts);
    }

    private static string NextGeneration() => "r" + Interlocked.Increment(ref readings).ToString(CultureInfo.InvariantCulture);

    /// <summary>The string field <paramref name="name"/> of the JSON object <paramref name="json"/>, or empty.</summary>
    internal static string Field(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Text(document.RootElement, name);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>True when the field <paramref name="name"/> of <paramref name="element"/> is true.</summary>
    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Cut(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        return text[..length] + "...";
    }

    /// <summary>A subagent's id as Claude Code names it, so it is a file's name and never a path.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex AgentIdShape();
}

/// <summary>
/// An agent a session sent, as its <c>.meta.json</c> says: its id, the id of the Agent call that sent it, its type and
/// description, the worktree it builds in or null, whether it finished or the user stopped it, and its transcript. It
/// has ended once it did either.
/// </summary>
internal sealed record SentMeta(string Agent, string ToolUseId, string Type, string Description, string? Worktree, bool Finished, bool Stopped, string Transcript)
{
    public bool Ended => Finished || Stopped;
}
