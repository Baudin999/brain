using System.Text;
using System.Text.Json;
using static Brain.Children.ClaudeStream;

namespace Brain.Code;

/// <summary>
/// Claude Code's <c>PermissionRequest</c> hook, as brain code's sessions carry it: each session's own
/// <c>--settings</c> runs <c>brain code --permission &lt;session id&gt;</c> for every tool call that would have asked the
/// user, and only for the sessions brain code starts. The hook reads the request from stdin, as
/// <c>{"tool_name", "tool_input", "agent_id", "agent_type", ...}</c>, where <c>agent_id</c> is there only for a request
/// of a subagent, and the decision it writes is
/// <c>{"hookSpecificOutput": {"hookEventName": "PermissionRequest", "decision": {"behavior": "allow"}}}</c>, or
/// <c>"deny"</c> with a <c>message</c> that goes back to the agent. The harness's PreToolUse hooks run before it and keep
/// refusing what they refuse.
/// </summary>
internal static class PermissionHook
{
    /// <summary>The flag of <c>brain code</c> the hook runs.</summary>
    public const string Flag = "permission";

    /// <summary>
    /// How long a request waits for the user: a week. Claude Code documents no limit on a command hook's timeout, which
    /// it counts in seconds, and its timer holds at most 2^31 - 1 milliseconds, about 24 days.
    /// </summary>
    public static readonly TimeSpan Longest = TimeSpan.FromDays(7);

    private const string Event = "PermissionRequest";

    /// <summary>The settings a session starts with: the hook, for every tool, waiting up to <see cref="Longest"/>.</summary>
    public static string Settings(string brain, string session)
    {
        var command = Quoted(brain) + " code --" + Flag + " " + session;
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("hooks");
            json.WriteStartArray(Event);
            json.WriteStartObject();
            json.WriteString("matcher", "*");
            json.WriteStartArray("hooks");
            json.WriteStartObject();
            json.WriteString("type", "command");
            json.WriteString("command", command);
            json.WriteNumber("timeout", (long)Longest.TotalSeconds);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The request the hook was given, for the session <paramref name="session"/>; null when it is no request of a tool.</summary>
    public static CodePermission? Request(string json, string session)
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
            var tool = Text(root, "tool_name");
            if (tool.Length == 0)
            {
                return null;
            }

            var input = default(JsonElement);
            if (root.TryGetProperty("tool_input", out var given))
            {
                input = given.Clone();
            }

            // Only a subagent's request carries its id; agent_type alone names the session's own agent.
            string? agentId = null;
            string? agentType = null;
            if (Text(root, "agent_id") is { Length: > 0 } id)
            {
                agentId = id;
                agentType = Text(root, "agent_type");
            }

            return new CodePermission(session, tool, input, agentId, agentType);
        }
    }

    /// <summary>The hook's output for <paramref name="decision"/>, one JSON object.</summary>
    public static string Output(CodeDecision decision)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartObject("hookSpecificOutput");
            json.WriteString("hookEventName", Event);
            json.WriteStartObject("decision");
            if (decision.Allow)
            {
                json.WriteString("behavior", "allow");
            }
            else
            {
                json.WriteString("behavior", "deny");
                json.WriteString("message", decision.Message ?? CodeDecision.Denied);
            }

            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The path in single quotes, for the shell that runs the hook's command.</summary>
    private static string Quoted(string path) => "'" + path.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
