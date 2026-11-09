using Brain.Children;

namespace Brain.Serving;

/// <summary>
/// The registry of every agent brain serve runs, in its memory only and never on disk, keyed by the window's tab: each
/// tab's chat, each conversation's live sitting and each model a running dream asks, with what runs under each. The
/// window marks every tab that has one. Everything in it is a descendant of brain serve, since every claude starts
/// through <see cref="ClaudeRuns"/>, so after brain serve stops there is nothing left to list.
/// </summary>
internal static class AgentRegistry
{
    public static AgentsView Of(ChatRoom chats, ConversationRoom conversations) =>
        new([.. chats.Entries(), .. conversations.Entries(), .. Dreams()]);

    /// <summary>Each model a dream asks while it answers, which the door knows by the place the dream set.</summary>
    private static IEnumerable<AgentEntry> Dreams() =>
        ClaudeRuns.Now()
            .Where(run => run.Place is { Kind: AgentEntry.Dream })
            .Select(run => new AgentEntry(run.Place!.Tab, AgentEntry.Dream, run.Place.Id, run.Place.Name + ": " + run.Model, run.Model, Effort: null, run.Started, run.Processes()));
}

/// <summary>The tabs of the window that brain serve names itself, as the window's scripts name them: CONVERSATIONS and DREAMS.</summary>
internal static class WindowTabs
{
    public const string Conversations = ":conversations";
    public const string Dreams = ":dreams";
}

/// <summary>The registry, the chats first, then the sittings, then the dreams.</summary>
internal sealed record AgentsView(IReadOnlyList<AgentEntry> Agents);

/// <summary>
/// One agent in the registry: its tab, what kind it is, its chat's id or its conversation's ref, how the window names it,
/// its model and effort, when it started and what runs under it.
/// </summary>
internal sealed record AgentEntry(string Tab, string Kind, string Id, string Name, string Model, string? Effort, DateTimeOffset Started, IReadOnlyList<AgentWork> Running)
{
    public const string Chat = "chat";
    public const string Sitting = "sitting";
    public const string Dream = "dream";
}
