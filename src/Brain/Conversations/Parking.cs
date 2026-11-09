using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Voices;

namespace Brain.Conversations;

/// <summary>
/// Parks a Claude Code session into a conversation. It reads the session's transcript, <c>&lt;dir&gt;/&lt;id&gt;.jsonl</c>
/// under the transcripts directory, and keeps what a trail keeps: the user's words and the agent's answer text. Thinking,
/// tool calls, tool results, meta messages and sub-agent lines stay out.
/// </summary>
internal static partial class Parking
{
    private const string Extension = ".jsonl";

    /// <summary>
    /// The phases the session's transcript holds, in order: each human turn, and each answer as one phase, the agent's text
    /// between two human turns joined, and the model the last assistant line that names a known one ran on, or null. Refuses
    /// when there is no readable transcript or no words in it.
    /// </summary>
    public static (IReadOnlyList<(string Who, DateTimeOffset At, string Text)> Words, string? Model) Read(BrainEnv env, string session)
    {
        BrainException.ThrowIf(SessionId().IsMatch(session) ? null : $"{session} is not a session id");
        var path = Directory.Exists(env.Transcripts)
            ? Directory.EnumerateDirectories(env.Transcripts).Select(dir => Path.Combine(dir, session + Extension)).FirstOrDefault(File.Exists)
            : null;
        BrainException.ThrowIf(path is null ? $"no transcript of session {session} under {env.Transcripts}" : null);
        var words = new List<(string Who, DateTimeOffset At, string Text)>();
        string? model = null;
        try
        {
            foreach (var line in File.ReadLines(path!).Where(line => line.Length > 0))
            {
                using var document = JsonDocument.Parse(line);
                model = ModelOf(document.RootElement) ?? model;
                if (TranscriptFilter.Read(line) is { } human && Time(human.Timestamp) is { } typed)
                {
                    words.Add((Trail.User, typed, human.Text));
                }
                else if (Answer(document.RootElement) is { } answer)
                {
                    if (words.Count > 0 && words[^1].Who == Trail.Agent)
                    {
                        words[^1] = words[^1] with { Text = words[^1].Text + "\n\n" + answer.Text };
                    }
                    else
                    {
                        words.Add((Trail.Agent, answer.At, answer.Text));
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new BrainException($"the transcript {path} cannot be read: {error.Message}");
        }

        BrainException.ThrowIf(words.Count > 0 ? null : $"the transcript {path} holds no words to park");
        return (words, model);
    }

    /// <summary>The model of <see cref="ConversationStore.Models"/> an assistant line names, matched by its name, or null.</summary>
    private static string? ModelOf(JsonElement line) =>
        TranscriptEntry.String(line, TranscriptEntry.TypeProperty) == "assistant" && !Flag(line, "isSidechain")
        && line.TryGetProperty("message", out var message) && TranscriptEntry.String(message, "model") is { } name
            ? ConversationStore.Models.FirstOrDefault(known => name.Contains(known, StringComparison.OrdinalIgnoreCase))
            : null;

    private static (DateTimeOffset At, string Text)? Answer(JsonElement line)
    {
        if (TranscriptEntry.String(line, TranscriptEntry.TypeProperty) != "assistant" || Flag(line, "isMeta") || Flag(line, "isSidechain")
            || !line.TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array || Time(TranscriptEntry.String(line, "timestamp")) is not { } at)
        {
            return null;
        }

        var text = string.Join("\n\n", content.EnumerateArray()
            .Where(part => TranscriptEntry.String(part, TranscriptEntry.TypeProperty) == "text")
            .Select(part => TranscriptEntry.String(part, "text")?.Trim())
            .Where(part => !string.IsNullOrEmpty(part)));
        return text.Length == 0 ? null : (at, text);
    }

    private static DateTimeOffset? Time(string? stamp) =>
        DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static bool Flag(JsonElement line, string name) => line.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^[0-9a-zA-Z-]{8,64}$")]
    private static partial Regex SessionId();
}
