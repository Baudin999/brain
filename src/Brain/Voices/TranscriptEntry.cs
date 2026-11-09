using System.Text.Json;

namespace Brain.Voices;

/// <summary>
/// The shape of one Claude Code transcript line: whether it is a turn the human typed, and its fields. Tool results,
/// meta entries, sidechains, compaction summaries and automated prompts are not human turns.
/// </summary>
internal static class TranscriptEntry
{
    public const string TypeProperty = "type";
    private const string UserType = "user";
    private const string OriginProperty = "origin";
    private const string KindProperty = "kind";
    private const string HumanOrigin = "human";
    private const string PromptSourceProperty = "promptSource";
    private const string MetaFlag = "isMeta";
    private const string SidechainFlag = "isSidechain";
    private const string CompactSummaryFlag = "isCompactSummary";
    private const string ToolResult = "toolUseResult";
    private const string SystemSource = "system";
    private const string SdkSource = "sdk";

    private static readonly string[] Disqualifiers = [MetaFlag, SidechainFlag, CompactSummaryFlag, ToolResult];
    private static readonly string[] AutomatedSources = [SystemSource, SdkSource];

    public static bool IsHumanTurn(JsonElement entry)
    {
        var isUser = string.Equals(String(entry, TypeProperty), UserType, StringComparison.Ordinal);
        var disqualified = Disqualifiers.Any(name => IsSet(entry, name));
        return isUser && !disqualified && IsHumanSource(entry);
    }

    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsHumanSource(JsonElement entry)
    {
        var origin = entry.TryGetProperty(OriginProperty, out var value) ? String(value, KindProperty) : null;
        var automated = AutomatedSources.Contains(String(entry, PromptSourceProperty), StringComparer.Ordinal);
        return !automated && origin is null or HumanOrigin;
    }

    private static bool IsSet(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);
}
