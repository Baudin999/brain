using System.Text.Json.Serialization;

namespace Brain.Usage;

/// <summary>
/// One line of the usage log, a fact and never a metric: when it happened, what kind of event it is and the Claude Code
/// session it happened in, when there was one. A <c>call</c> is one run of the CLI, an <c>answer</c> the user's answer
/// to a pending write, a <c>spawn</c> an agent the Agent tool started, a <c>message</c> one SendMessage, a <c>skill</c>
/// one skill loaded, a <c>readiness</c> the verdict a readiness check stopped on, a <c>refusal</c> one tool call a hook
/// refused, an <c>error</c> one failure of the CLI, of brain serve, of a landing write or of a hook, <c>told</c> the
/// moment the user was shown every error before it, and <c>duplicate</c>, <c>contradiction</c> and
/// <c>missing-edges</c> one node write the judge's gate refused for saying the same as a nearest entry, for
/// contradicting one, or for carrying no edge while the judge named edges. Each kind fills its own fields and leaves
/// the rest null, which the log leaves out.
/// </summary>
internal sealed record UsageEvent(string At, string Event, string? Session)
{
    public const string Call = "call";
    public const string Answer = "answer";
    public const string Spawn = "spawn";
    public const string Message = "message";
    public const string Skill = "skill";
    public const string Readiness = "readiness";
    public const string Refusal = "refusal";
    public const string Error = "error";
    public const string Told = "told";
    public const string Duplicate = "duplicate";
    public const string Contradiction = "contradiction";
    public const string MissingEdges = "missing-edges";

    /// <summary>The most characters of a validator's message the log keeps.</summary>
    public const int TextLimit = 2048;

    /// <summary>A call's caller: an AI session, where Claude Code runs the command.</summary>
    public const string Ai = "ai";

    /// <summary>A call's caller: the user's own terminal, where no Claude Code session runs.</summary>
    public const string User = "user";

    /// <summary>An answer: no with the user's words on what to change.</summary>
    public const string Change = "change";

    /// <summary>The skill whose loads are brain's fixed cost; a skill event carries its size.</summary>
    public const string BrainSkill = "brain";

    /// <summary>A call's caller, <see cref="Ai"/> or <see cref="User"/>.</summary>
    public string? Caller { get; init; }

    /// <summary>A call's command, the first argument after <c>--project</c> and its value.</summary>
    public string? Command { get; init; }

    /// <summary>A call's arguments after the command.</summary>
    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>The characters a call printed, stdout and stderr together, or the brain skill's characters when it loaded.</summary>
    public long? Chars { get; init; }

    /// <summary>How long a call ran, in milliseconds.</summary>
    public long? Ms { get; init; }

    /// <summary>A call's exit status.</summary>
    public int? Exit { get; init; }

    /// <summary>The refs a lookup or a nearest returned, best first.</summary>
    public IReadOnlyList<string>? Returned { get; init; }

    /// <summary>The nodes a node or a walk opened.</summary>
    public IReadOnlyList<OpenedNode>? Opened { get; init; }

    /// <summary>An answer's pending write id; a told's own id, which the start that wrote it reads back to.</summary>
    public string? Id { get; init; }

    /// <summary>An answer: yes, no, or <see cref="Change"/>. A readiness check: the first line it stopped on, READY or NOT READY.</summary>
    public string? Verdict { get; init; }

    /// <summary>An answer's yes that landed; false when a node changed since it was staged and nothing landed.</summary>
    public bool? Landed { get; init; }

    /// <summary>
    /// An answer's title: the title of the first node its write changes, as the window showed it. A judge's refusal:
    /// the title of the write it refused.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>An answer's words: what the user said to change, on a <see cref="Change"/>.</summary>
    public string? Words { get; init; }

    /// <summary>The id of the agent that spawned, sent, loaded, stopped or was refused, null for the session's root agent.</summary>
    public string? AgentId { get; init; }

    /// <summary>The type of the agent that spawned, sent, loaded, stopped or was refused, such as brain, builder or validator.</summary>
    public string? AgentType { get; init; }

    /// <summary>A spawn's subagent type, such as builder, validator or brain-ready.</summary>
    public string? SubagentType { get; init; }

    /// <summary>A spawn's model: the one the Agent tool was asked for, else the one it resolved.</summary>
    public string? Model { get; init; }

    /// <summary>A spawn's new agent id, which a message names to reach it.</summary>
    public string? Spawned { get; init; }

    /// <summary>A spawn's name, when the Agent tool was given one, which a message may name instead of the id.</summary>
    public string? Name { get; init; }

    /// <summary>A spawn's brief: the first line of the prompt the new agent was given.</summary>
    public string? Brief { get; init; }

    /// <summary>A message's recipient as the sender named it: an agent id, a name, or main.</summary>
    public string? To { get; init; }

    /// <summary>A validator's message text, cut to its first <see cref="TextLimit"/> characters; no other agent's message carries text.</summary>
    public string? Text { get; init; }

    /// <summary>A skill load's skill name.</summary>
    public string? SkillName { get; init; }

    /// <summary>A refusal's hook, by its file name without .sh, such as graph-door.</summary>
    public string? Hook { get; init; }

    /// <summary>A refusal's tool, such as Bash or Edit.</summary>
    public string? Tool { get; init; }

    /// <summary>
    /// A refusal's reason: the first line the hook said. A contradiction: what the write contradicts, as the judge said
    /// it.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>A judge's refusal: the ref of the node the write would have written.</summary>
    public string? Ref { get; init; }

    /// <summary>A duplicate or a contradiction: the ref of the nearest entry the write matched.</summary>
    public string? Match { get; init; }

    /// <summary>A missing-edges: the edges the judge named, each <c>rel -> target</c>.</summary>
    public IReadOnlyList<string>? Edges { get; init; }

    /// <summary>An error's what: the command, the window's call, the write or the hook that failed, such as <c>brain context</c>.</summary>
    public string? Failed { get; init; }

    /// <summary>An error's problem: the one line the user is shown of why it failed.</summary>
    public string? Problem { get; init; }

    /// <summary>An error's detail: the whole exception with its stack trace, which the user is never shown in a line.</summary>
    public string? Detail { get; init; }
}

/// <summary>A node a call opened: its ref, the day it was born and the session that wrote it, when the node keeps one.</summary>
internal sealed record OpenedNode(string Ref, string? Born, string? Session);

/// <summary>The JSON of a usage log line, one compact object per line, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UsageEvent))]
internal sealed partial class UsageJson : JsonSerializerContext;
