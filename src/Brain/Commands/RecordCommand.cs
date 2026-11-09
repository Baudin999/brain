using System.Text.Json;
using Brain.Stats;
using Brain.Usage;

namespace Brain.Commands;

/// <summary>
/// <c>brain record</c>: the harness's hook pipes the JSON of a Claude Code tool call into it, and it appends what that
/// JSON carries to the usage log as a fact, never a metric: an Agent call as a spawn, a SendMessage as a message, a
/// Skill as a skill load and the stop of a readiness check as its verdict. Any other tool or stop records nothing. With
/// <c>--refused</c>, which the harness's refusal.sh passes when a hook refused the call, it records the refusal instead.
/// What the facts mean is the work of <c>brain stats</c>.
/// </summary>
internal static class RecordCommand
{
    public const string Name = "record";
    private const string RefusedOption = "refused";
    private const string ReasonOption = "reason";
    private const string SubagentStop = "SubagentStop";

    /// <summary>The most characters a line taken from free text keeps: a brief's, a verdict's or a refusal's.</summary>
    private const int LineLimit = 200;

    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, RefusedOption, ReasonOption);
        options.Expect(0, Usage);
        using var hook = Parse(env.In.ReadToEnd());
        var root = hook.RootElement;
        var usage = options.Get(RefusedOption) is { } refused
            ? Event(root, UsageEvent.Refusal) with { Hook = refused, Tool = Text(root, "tool_name"), Reason = FirstLine(options.Get(ReasonOption)) }
            : Text(root, "hook_event_name") == SubagentStop ? Stop(root) : Tool(env, root);
        if (usage is not null)
        {
            UsageLog.Append(env.Home, usage, env.Err);
        }

        return ExitCodes.Ok;
    }

    /// <summary>
    /// A readiness check's verdict, the first line of the last message it stopped on. A second stop after a hook kept the
    /// agent going is not a verdict of its own, and the stop of any other agent records nothing.
    /// </summary>
    private static UsageEvent? Stop(JsonElement root) =>
        Text(root, "agent_type") == Agents.ReadinessCheck && Member(root, "stop_hook_active") is not { ValueKind: JsonValueKind.True }
            ? Event(root, UsageEvent.Readiness) with { Verdict = FirstLine(Text(root, "last_assistant_message")) }
            : null;

    private static UsageEvent? Tool(BrainEnv env, JsonElement root)
    {
        var input = Member(root, "tool_input");
        var response = Member(root, "tool_response");
        return Text(root, "tool_name") switch
        {
            "Agent" or "Task" => Event(root, UsageEvent.Spawn) with
            {
                SubagentType = Text(input, "subagent_type"),
                Model = Text(input, "model") ?? Text(response, "resolvedModel"),
                Spawned = Text(response, "agentId"),
                Name = Text(input, "name"),
                Brief = FirstLine(Text(input, "prompt")),
            },
            "SendMessage" => Event(root, UsageEvent.Message) with { To = Text(input, "to"), Text = Text(root, "agent_type") == Agents.Validator ? Cut(Text(input, "message")) : null },
            "Skill" => Event(root, UsageEvent.Skill) with
            {
                SkillName = Text(input, "skill"),
                Chars = string.Equals(Text(input, "skill"), UsageEvent.BrainSkill, StringComparison.Ordinal) && env.SkillFile is { } file && File.Exists(file)
                    ? File.ReadAllText(file).Length
                    : null,
            },
            _ => null,
        };
    }

    /// <summary>The first line of <paramref name="text"/> that holds more than blanks, trimmed and cut to <see cref="LineLimit"/>.</summary>
    private static string? FirstLine(string? text) =>
        text?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) is { } line
            ? line[..Math.Min(line.Length, LineLimit)]
            : null;

    /// <summary>The first <see cref="UsageEvent.TextLimit"/> characters of <paramref name="text"/>, never ending on half a surrogate pair.</summary>
    private static string? Cut(string? text)
    {
        if (text is null || text.Length <= UsageEvent.TextLimit)
        {
            return text;
        }

        var length = char.IsHighSurrogate(text[UsageEvent.TextLimit - 1]) ? UsageEvent.TextLimit - 1 : UsageEvent.TextLimit;
        return text[..length];
    }

    private static UsageEvent Event(JsonElement root, string kind) =>
        new(UsageLog.Now(), kind, Text(root, "session_id")) { AgentId = Text(root, "agent_id"), AgentType = Text(root, "agent_type") };

    private static JsonDocument Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new BrainException("record reads the JSON of a Claude Code hook on stdin");
        }
    }

    private static JsonElement? Member(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } found && found.TryGetProperty(name, out var member) ? member : null;

    private static string? Text(JsonElement? element, string name) =>
        Member(element, name) is { ValueKind: JsonValueKind.String } value && value.GetString() is { Length: > 0 } text ? text : null;
}
