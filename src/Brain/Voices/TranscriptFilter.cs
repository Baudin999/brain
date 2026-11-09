using System.Text.Json;
using System.Text.RegularExpressions;

namespace Brain.Voices;

/// <summary>
/// Turns one transcript line into the message the human typed, or null. Inside a human turn it drops system reminders,
/// and it drops whole turns that are only a command wrapper, a task notification or an interruption marker.
/// </summary>
internal static partial class TranscriptFilter
{
    private const string UserMarker = "\"user\"";
    private const string TextType = "text";
    private const string TimestampProperty = "timestamp";
    private const string MessageProperty = "message";
    private const string ContentProperty = "content";
    private const string TextProperty = "text";
    private const string Paragraph = "\n\n";
    private const string CommandWrapper = "<command-";
    private const string LocalCommandWrapper = "<local-command-";
    private const string BashWrapper = "<bash-";
    private const string TaskNotification = "<task-notification>";
    private const string HookWrapper = "<user-prompt-submit-hook>";
    private const string Interruption = "[Request interrupted";
    private const string Caveat = "Caveat:";

    private static readonly string[] Wrappers =
        [CommandWrapper, LocalCommandWrapper, BashWrapper, TaskNotification, HookWrapper, Interruption, Caveat];

    public static VoiceMessage? Read(string line)
    {
        if (!line.Contains(UserMarker, StringComparison.Ordinal))
        {
            return null;
        }

        using var document = Parse(line);
        if (document is null || !TranscriptEntry.IsHumanTurn(document.RootElement))
        {
            return null;
        }

        var text = Typed(document.RootElement);
        var timestamp = TranscriptEntry.String(document.RootElement, TimestampProperty) ?? string.Empty;
        return text is null ? null : new VoiceMessage(timestamp, text);
    }

    private static JsonDocument? Parse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Typed(JsonElement entry)
    {
        if (!entry.TryGetProperty(MessageProperty, out var message) || !message.TryGetProperty(ContentProperty, out var content))
        {
            return null;
        }

        var raw = content.ValueKind == JsonValueKind.String ? content.GetString() : string.Join(Paragraph, TextBlocks(content));
        var text = Reminder().Replace(raw ?? string.Empty, string.Empty).Trim();
        return text.Length == 0 || Wrappers.Any(wrapper => text.StartsWith(wrapper, StringComparison.Ordinal)) ? null : text;
    }

    private static IEnumerable<string> TextBlocks(JsonElement content) =>
        content.ValueKind != JsonValueKind.Array
            ? []
            : content.EnumerateArray()
                .Where(block => string.Equals(TranscriptEntry.String(block, TranscriptEntry.TypeProperty), TextType, StringComparison.Ordinal))
                .Select(block => TranscriptEntry.String(block, TextProperty))
                .OfType<string>();

    [GeneratedRegex("<system-reminder>.*?</system-reminder>", RegexOptions.Singleline)]
    private static partial Regex Reminder();
}
