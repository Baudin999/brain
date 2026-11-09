using Brain.Dreaming;

namespace Brain.Serving;

/// <summary>
/// A dream as the window's log lists it: its id, when, its conversation and that conversation's scope or project, its
/// title then, the sittings it settled, agreed or escalated and why, the models it asked, and its chip.
/// </summary>
internal sealed record DreamRow(
    string Id,
    DateTimeOffset At,
    string Conversation,
    string Scope,
    string Title,
    IReadOnlyList<int> Sittings,
    string Outcome,
    string? Why,
    IReadOnlyList<string> Models,
    OriginView Origin)
{
    public static DreamRow Of(DreamRecord record)
    {
        var scope = record.Conversation[..Math.Max(0, record.Conversation.IndexOf(':', StringComparison.Ordinal))];
        var kind = scope is ScopeNames.Personal or ScopeNames.Global ? scope : ScopeNames.Project;
        return new DreamRow(
            record.Id,
            record.At,
            record.Conversation,
            scope,
            record.Title,
            record.Sittings,
            record.Outcome,
            record.Why,
            [.. record.Drafts.Select(draft => draft.Model).Concat(record.Escalation is { } escalation ? [escalation.Model] : []).Concat(record.Deep is { } deep ? [deep.Model] : [])],
            ConversationRoom.Origin(kind, scope));
    }
}

/// <summary>One dream, read whole: its row, the shape of its task, each draft, the comparison, settled.md before and after, and a deep dream's agent and final words.</summary>
internal sealed record DreamView(DreamRow Row, DreamShape Shape, IReadOnlyList<DreamDraft> Drafts, DreamComparison? Comparison, DreamDraft? Escalation, string Before, string After, DeepDream? Deep)
{
    public static DreamView Of(DreamRecord record) =>
        new(DreamRow.Of(record), record.Shape, record.Drafts, record.Comparison, record.Escalation, record.Before, record.After, record.Deep);
}

/// <summary>
/// What dreams changed in a conversation's settled.md since the user's last sitting: the dreams, oldest first, settled.md
/// before the first and after the last. No dream is an empty list.
/// </summary>
internal sealed record DreamChangesView(IReadOnlyList<DreamRow> Dreams, string Before, string After);
