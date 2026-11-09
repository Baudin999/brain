using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// The agents the metrics read meaning into, by their types as the harness names them in harness/agents, and which agent
/// a message reached.
/// </summary>
internal static class Agents
{
    public const string Builder = "builder";
    public const string Validator = "validator";
    public const string ReadinessCheck = "brain-ready";

    /// <summary>
    /// The agents the Agent tool spawned as <paramref name="subagentType"/>, each under every handle a message in its
    /// session can reach it by, its id and its name, and mapped to its id.
    /// </summary>
    public static Dictionary<(string? Session, string Handle), string> Spawned(IReadOnlyList<UsageEvent> events, string subagentType)
    {
        var spawned = new Dictionary<(string? Session, string Handle), string>();
        foreach (var spawn in events.Where(usage => usage.Event == UsageEvent.Spawn && usage.Spawned is not null && usage.SubagentType == subagentType))
        {
            spawned.TryAdd((spawn.Session, spawn.Spawned!), spawn.Spawned!);
            if (spawn.Name is { } name)
            {
                spawned[(spawn.Session, name)] = spawn.Spawned!;
            }
        }

        return spawned;
    }

    /// <summary>The id of the agent in <paramref name="spawned"/> a message reached, or null when it reached none of them.</summary>
    public static string? Reached(Dictionary<(string? Session, string Handle), string> spawned, UsageEvent message) =>
        message.Event == UsageEvent.Message && message.To is { } to && spawned.TryGetValue((message.Session, to), out var id) ? id : null;
}
