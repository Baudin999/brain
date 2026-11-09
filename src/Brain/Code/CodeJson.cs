using System.Text.Json;
using System.Text.Json.Serialization;
using Brain.Children;

namespace Brain.Code;

/// <summary>
/// One thing said in a session, as the window draws it: brain's own shape of it, read from Claude Code's transcript and
/// never text the window scrapes. A kind is one of <see cref="CodeEvent.User"/>, <see cref="CodeEvent.Said"/>,
/// <see cref="CodeEvent.Tool"/>, <see cref="CodeEvent.Result"/>, <see cref="CodeEvent.Agent"/> and
/// <see cref="CodeEvent.Interrupted"/>. A question the agent asks through <c>brain ask</c> is no event: it is the
/// server's own, in <see cref="CodeQuestions"/>, placed after the events written before it.
/// </summary>
/// <param name="Text">The words: the user's, the agent's, a result's or a message's, and of a tool call the message it sends, whole.</param>
/// <param name="Id">A tool call's id, which its result and, for an agent, its subagent's conversation are found by.</param>
/// <param name="Name">A tool's name, or the type of the agent a call sent, as builder.</param>
/// <param name="Subject">What a tool call works on, in one line, or what the agent it sent was asked to do.</param>
/// <param name="Failed">A result that is the tool's error.</param>
/// <param name="Input">What a tool call was given, each field of its input by name, as text.</param>
/// <param name="Brain">The brain reading command a Bash call runs, <c>lookup</c>, <c>nearest</c>, <c>node</c> or <c>walk</c>.</param>
/// <param name="Items">The nodes a brain query's result lists, empty when it found none.</param>
/// <param name="At">When the transcript says the line was written, or null when it does not.</param>
/// <param name="Images">The images the user's words carry, or a result carries, each as a data URL; null for none.</param>
internal sealed record CodeEvent(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject = null,
    bool Failed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? At = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Input = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Brain = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BrainItem>? Items = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Images = null)
{
    /// <summary>The user's words.</summary>
    public const string User = "user";

    /// <summary>The agent's text, markdown.</summary>
    public const string Said = "text";

    /// <summary>A call of a tool.</summary>
    public const string Tool = "tool";

    /// <summary>What a tool call gave back.</summary>
    public const string Result = "result";

    /// <summary>An agent the session sent, as a builder or a validator, whose own conversation opens from it.</summary>
    public const string Agent = "agent";

    /// <summary>The user interrupted the agent's turn, as Ctrl+C does in a terminal.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>A message another agent sent this one, as a validator's findings to its builder: Id is the sender's agent id, Name its name.</summary>
    public const string Message = "message";

    /// <summary>The ids of the calls of <paramref name="calls"/> whose result was an error.</summary>
    public static HashSet<string> FailedCalls(IReadOnlyList<CodeEvent> calls)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var one in calls)
        {
            if (one is { Kind: CodeEvent.Result, Failed: true, Id: { } call })
            {
                failed.Add(call);
            }
        }

        return failed;
    }
}

/// <summary>
/// A conversation's events from the one numbered <c>From</c>, and whether its transcript is there yet. Its reading is
/// <c>Generation</c>: the window sends it back, and a <c>From</c> of 0 under a new generation means the window's events
/// are to be read again from the start. Whether an agent it sent runs is the session's agents' to say, as
/// <see cref="CodeSent"/>. <c>Live</c> is the text the session's own agent is writing now, until it is an event of the
/// conversation; an agent's conversation has none.
/// </summary>
internal sealed record CodeConversation(int From, IReadOnlyList<CodeEvent> Events, bool Found, string? Generation = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Live { get; init; }
}

/// <summary>
/// One session as the window lists it: its id, which is Claude Code's session id, its agent and folder, whether its
/// claude still runs and is answering, why it stopped, when it started, what runs under it, the permission requests
/// that wait for the user, the questions it asked him through <c>brain ask</c>, open and answered, and how many of the
/// agents it sent still run.
/// </summary>
internal sealed record CodeSessionView(
    string Id,
    string Agent,
    string Cwd,
    bool Alive,
    bool Working,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Problem,
    DateTimeOffset Started,
    IReadOnlyList<RunningJob> Running,
    IReadOnlyList<CodeAsk> Asks,
    IReadOnlyList<CodeQuestions> Questions,
    int Agents)
{
    /// <summary>
    /// Whether the session runs work a reload would stop: a turn, a process or an agent it started, or a permission
    /// request or a question that waits for the user's answer.
    /// </summary>
    public bool Runs()
    {
        if (!Alive)
        {
            return false;
        }

        if (Working)
        {
            return true;
        }

        if (Running.Count > 0)
        {
            return true;
        }

        if (Agents > 0)
        {
            return true;
        }

        if (Asks.Count > 0)
        {
            return true;
        }

        return Questions.Any(asked => asked.State == CodeQuestions.Open);
    }
}

/// <summary>
/// A tool call of a session that would have asked the user's permission, as <see cref="PermissionHook"/> hands it to the
/// server: the session, the tool and its input, and for a subagent's call its id and its type.
/// </summary>
internal sealed record CodePermission(string? Session, string? Tool, JsonElement Input, string? AgentId, string? AgentType);

/// <summary>
/// A permission request that waits for the user, as the window draws it: its number in the session, the tool and its
/// input, and, when a subagent asked, its type, its id and the id of the Agent call that sent it, once its meta says so.
/// </summary>
internal sealed record CodeAsk(
    string Id,
    string Tool,
    JsonElement Input,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Agent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AgentId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Call);

/// <summary>The user's answer to a permission request: allow, or deny with the words that go back to the agent.</summary>
internal sealed record CodeAnswer(string? Session, string? Ask, bool Allow, string? Words);

/// <summary>What the hook writes back: allow, or deny with a message the agent reads.</summary>
internal sealed record CodeDecision(bool Allow, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message)
{
    public const string Denied = "The user denied this tool call in brain code's window.";

    public static readonly CodeDecision Allowed = new(true, null);

    public static CodeDecision Deny(string message) => new(false, message);

    /// <summary>The user's deny in the window: his words, when he gave any, are the reason the agent reads.</summary>
    public static CodeDecision DeniedByUser(string? words)
    {
        var said = words?.Trim() ?? string.Empty;
        if (said.Length == 0)
        {
            return Deny(Denied);
        }

        return Deny("The user denied this tool call in brain code's window: " + said);
    }
}

/// <summary>
/// One agent a session sent, as the window lists it: its id, the id of the call that sent it, its type and the kind
/// of view it opens in, what it was sent to do, whether it runs, finished, was stopped or failed, when it started and
/// last wrote, the id of the builder whose work it is part of, a readiness check's verdict, and the worktree it builds
/// in, with when it was opened and whether it was removed. A readiness check carries the call id of the agent it
/// checked, <c>Attached</c>, and none when no agent was sent after it yet, and whether its verdict asks questions; a
/// running agent what it does now; a builder the outline of the steps of its work and the Navarone signals it touched.
/// </summary>
internal sealed record CodeSent(
    string Agent,
    string Tool,
    string Type,
    string Kind,
    string Description,
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Started,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Ended,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Work,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Verdict,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CodeWorktree? Worktree = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Attached = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Now = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CodeOutline>? Steps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Asks = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CodeSignals? Signals = null)
{
    public const string Builder = "builder";
    public const string Validator = "validator";
    public const string Ready = "ready";
    public const string Other = "other";

    public const string Running = "running";
    public const string Finished = "finished";

    /// <summary>The user stopped it, as an interrupt of the session's turn stops the agents it sent, before it finished.</summary>
    public const string Stopped = "stopped";

    public const string Failed = "failed";
}

/// <summary>
/// The worktree an agent builds in: its full path, when it was opened, whether it was removed, by a call that removed it
/// or by its folder being gone, and when that call ran.
/// </summary>
internal sealed record CodeWorktree(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Opened,
    bool Removed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? RemovedAt);

/// <summary>
/// A step of a piece of work, short, for the list: its <see cref="Trail"/> kind, the call id of the agent that wrote it,
/// when, its round, how many commits it made, and for a landing its range.
/// </summary>
internal sealed record CodeOutline(
    string Kind,
    string Tool,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Round,
    int Commits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Landed);

/// <summary>Every agent a session sent, in the order it sent them.</summary>
internal sealed record CodeSentList(IReadOnlyList<CodeSent> Agents);

/// <summary>
/// One agent as its own view draws it: the agent, the brief it was sent with, its
/// report once it gave one, the trail of the work it is part of, for a builder and a validator, and for a readiness
/// check the questions of its report: what follows its verdict.
/// </summary>
internal sealed record CodeAgentView(
    CodeSent Sent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Brief,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Report,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CodeTrail? Trail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Questions);

/// <summary>The trail of one piece of work: its builder's id, its steps in the order they happened, and the Navarone signals it touched.</summary>
internal sealed record CodeTrail(string Builder, IReadOnlyList<CodeStep> Steps, CodeSignals Signals);

/// <summary>
/// One step of a trail, of a <see cref="Trail"/> kind: the agent that wrote it, when, its words as markdown, the commits
/// it made, for a validator's report that landed the work the range it moved the default branch by, and the round a
/// step is of: findings by their place among the messages validators sent the builder, from 1; the builder's word by how
/// many of those it had been sent before it, its report being round 0; null for a validator's report.
/// </summary>
internal sealed record CodeStep(
    string Kind,
    string Agent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? At,
    string Text,
    IReadOnlyList<CodeCommit> Commits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Landed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Round);

/// <summary>A commit, by its short hash and its subject.</summary>
internal sealed record CodeCommit(string Hash, string Subject);

/// <summary>The Navarone signals a piece of work touched; <c>Read</c> is false when brain has no Navarone log to read.</summary>
internal sealed record CodeSignals(bool Read, IReadOnlyList<CodeSignal> All);

/// <summary>
/// One Navarone signal and how it ended, one of <see cref="WorkSignals"/>' ends: its id and check, the code it is on in
/// the code's own words, the builder's reason or a large change's text, the validator's finding, and once the user
/// judged it, his keep or replace and his words.
/// </summary>
internal sealed record CodeSignal(
    string Id,
    string Check,
    string Name,
    string Ended,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Finding,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Judgement,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Words);

/// <summary>Every session of the window, the oldest first, and the folder a new one starts in unless the window says.</summary>
/// <summary>
/// Every session, the oldest first, and the user's home; whether a newer build waits for a reload, <see
/// cref="CodeReload.Running"/> while a session runs and <see cref="CodeReload.Ready"/> once none does, and nothing until
/// one is published; and the session the window showed before a reload, which the window opens in view.
/// </summary>
internal sealed record CodeSessionList(
    IReadOnlyList<CodeSessionView> Sessions,
    string Home,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reload,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Opened);

/// <summary>What a newer build's reload waits for, as the session list says it.</summary>
internal static class CodeReload
{
    public const string Running = "running";
    public const string Ready = "ready";
}

/// <summary>
/// A session as a reload carries it to the successor: the Claude Code session id it runs as, its agent and its folder,
/// whether it was alive, why it ended, and when it started.
/// </summary>
internal sealed record CodeCarried(string Id, string Agent, string Cwd, bool Alive, string? Problem, DateTimeOffset Started);

/// <summary>What a reload hands its successor: every session, the oldest first, and the id of the one the window showed.</summary>
internal sealed record CodeHandover(IReadOnlyList<CodeCarried> Sessions, string? Opened);

/// <summary>A session to start: its agent, its folder, and the id of a Claude Code session to continue, or null for a new one.</summary>
internal sealed record CodeStart(string? Agent, string? Cwd, string? Resume);

/// <summary>
/// What the window shows: of the second flow, the tool calls and the brain queries a conversation holds, each kind
/// drawn or hidden, both on at the start; and how the agents panel draws the agents a session sent, as a flow or as a
/// list, a flow at the start.
/// </summary>
internal sealed record CodeSettingsView(bool Tools = true, bool Brain = true, string Agents = CodeSettingsView.Flow)
{
    public const string Flow = "flow";
    public const string List = "list";
}

/// <summary>The user's words to a session, and the images he attached to them.</summary>
internal sealed record CodeSay(string? Session, string? Words, IReadOnlyList<CodeImage>? Images = null);

/// <summary>An image as the window attaches it and the explorer reads it: its file's name, its media type and its bytes in base64.</summary>
internal sealed record CodeImage(string? Name, string? MediaType, string? Data);

/// <summary>A past session of the open agent as the history lists it: Claude Code's session id, its first words, and when it was last written.</summary>
internal sealed record CodePast(string Id, string Title, DateTimeOffset At);

/// <summary>The past sessions of the open agent in one folder, newest first.</summary>
internal sealed record CodePastList(IReadOnlyList<CodePast> Sessions);

/// <summary>A folder as the file explorer shows it: its full path, its parent's or null at the root, and the names of its folders and its images.</summary>
internal sealed record CodeFolder(string Path, string? Parent, IReadOnlyList<string> Folders, IReadOnlyList<string> Images);

/// <summary>The file explorer's places: the folder it opens in, ~/Pictures, and the folders last attached from, the newest first.</summary>
internal sealed record CodePlaces(string Start, string Pictures, IReadOnlyList<string> Recent);

/// <summary>The folder the user attached images from.</summary>
internal sealed record CodeAttached(string? Folder);

/// <summary>The session a call stops.</summary>
internal sealed record CodeSessionRef(string? Session);

/// <summary>A process of a session the user stops: the session, and the process by its pid.</summary>
internal sealed record CodeProcessRef(string? Session, int Pid);

/// <summary>What a stop of a process did: it ended, or it was gone already.</summary>
internal sealed record CodeProcessEnd(bool Gone);

/// <summary>The agents a session can start with: each agent file's name in Claude Code's agents folder.</summary>
internal sealed record CodeAgents(IReadOnlyList<string> Agents);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CodeConversation))]
[JsonSerializable(typeof(CodeSessionView))]
[JsonSerializable(typeof(CodeSessionList))]
[JsonSerializable(typeof(CodeHandover))]
[JsonSerializable(typeof(CodeStart))]
[JsonSerializable(typeof(CodeSay))]
[JsonSerializable(typeof(CodeSettingsView))]
[JsonSerializable(typeof(CodeImage))]
[JsonSerializable(typeof(CodeFolder))]
[JsonSerializable(typeof(CodePlaces))]
[JsonSerializable(typeof(CodeAttached))]
[JsonSerializable(typeof(CodeSessionRef))]
[JsonSerializable(typeof(CodeProcessRef))]
[JsonSerializable(typeof(CodeProcessEnd))]
[JsonSerializable(typeof(CodeAgents))]
[JsonSerializable(typeof(CodePermission))]
[JsonSerializable(typeof(CodeAnswer))]
[JsonSerializable(typeof(CodeDecision))]
[JsonSerializable(typeof(AskDocument))]
[JsonSerializable(typeof(CodeQuestionsIn))]
[JsonSerializable(typeof(CodeQuestionsOut))]
[JsonSerializable(typeof(CodeReply))]
[JsonSerializable(typeof(CodeDismiss))]
[JsonSerializable(typeof(CodeSentList))]
[JsonSerializable(typeof(CodeAgentView))]
[JsonSerializable(typeof(CodePastList))]
internal sealed partial class CodeJson : JsonSerializerContext;
