using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Brain.Dreaming;

/// <summary>
/// One draft of settled.md: the model that wrote it, whether it said it was sure, why it doubted when it did, and the
/// draft itself.
/// </summary>
internal sealed record DreamDraft(string Model, bool Sure, string? Doubt, string Text)
{
    /// <summary>
    /// Reads a dreamer's answer: its first line <c>SURE</c> or <c>UNSURE: why</c>, then the draft. An answer that opens
    /// with neither is unsure, with the whole answer as its draft, and so is an answer with no draft after its first
    /// line, so a cut-off answer never empties settled.md. A draft fenced whole as markdown loses the fence.
    /// </summary>
    public static DreamDraft Parse(string model, string answer)
    {
        var read = Read(model, answer);
        return read.Text.Length == 0 ? read with { Sure = false, Doubt = "its answer holds no draft" } : read;
    }

    private static DreamDraft Read(string model, string answer)
    {
        var text = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var end = text.IndexOf('\n', StringComparison.Ordinal);
        var first = (end < 0 ? text : text[..end]).Trim();
        var rest = end < 0 ? string.Empty : text[(end + 1)..];
        return first == DreamPrompt.Sure ? new DreamDraft(model, Sure: true, Doubt: null, Unfenced(rest))
            : first.StartsWith(DreamPrompt.Unsure, StringComparison.Ordinal) ? new DreamDraft(model, Sure: false, first[DreamPrompt.Unsure.Length..].TrimStart(':', ' ') is { Length: > 0 } why ? why : "it did not say why", Unfenced(rest))
            : new DreamDraft(model, Sure: false, $"its answer opened with neither {DreamPrompt.Sure} nor {DreamPrompt.Unsure}", Unfenced(text));
    }

    private static string Unfenced(string draft)
    {
        var text = draft.Trim();
        var open = text.IndexOf('\n', StringComparison.Ordinal);
        return text.StartsWith("```", StringComparison.Ordinal) && text.EndsWith("```", StringComparison.Ordinal) && open > 0 && open < text.Length - 3
            ? text[(open + 1)..^3].Trim()
            : text;
    }
}

/// <summary>What the comparison of the two drafts said: the model, whether they agree, and what differs when they do not.</summary>
internal sealed record DreamComparison(string Model, bool Agreed, string? Why)
{
    /// <summary>Reads the comparison's answer, <c>AGREE</c> or <c>DIVERGE: what differs</c>; anything else diverges.</summary>
    public static DreamComparison Parse(string model, string answer)
    {
        var line = answer.Trim().Split('\n')[0].Trim();
        return line == DreamPrompt.Agree ? new DreamComparison(model, Agreed: true, Why: null)
            : line.StartsWith(DreamPrompt.Diverge, StringComparison.Ordinal) ? new DreamComparison(model, Agreed: false, line[DreamPrompt.Diverge.Length..].TrimStart(':', ' ') is { Length: > 0 } why ? why : "it did not say what differs")
            : new DreamComparison(model, Agreed: false, $"its answer was neither {DreamPrompt.Agree} nor {DreamPrompt.Diverge}: {line}");
    }
}

/// <summary>The shape of a dream's task: how many sittings, phases and characters of trail, and how long settled.md was.</summary>
internal sealed record DreamShape(int Sittings, int Phases, int Characters, int SettledCharacters);

/// <summary>
/// What the open agent of a deep dream ran on, the dream that settled the conversation's sittings first when one did,
/// and the agent's final words: its own account of what it changed in the graph.
/// </summary>
internal sealed record DeepDream(string Model, string Effort, string? Settled, string Words);

/// <summary>
/// One dream, as the log keeps it: when, on which conversation and which sittings, the shape of the task, each cheap
/// draft, what the comparison said when both were sure, whether it agreed or escalated and why, the escalation's draft,
/// and settled.md before and after. A deep dream settles no sitting and changes no settled.md: it keeps its agent in
/// <see cref="Deep"/>, and why it failed when it did.
/// </summary>
internal sealed record DreamRecord(
    string Id,
    DateTimeOffset At,
    string Conversation,
    string Title,
    IReadOnlyList<int> Sittings,
    DreamShape Shape,
    IReadOnlyList<DreamDraft> Drafts,
    DreamComparison? Comparison,
    string Outcome,
    string? Why,
    DreamDraft? Escalation,
    string Before,
    string After,
    DeepDream? Deep = null)
{
    public const string Agreed = "agreed";
    public const string Escalated = "escalated";
    public const string Deeply = "deep";
}

/// <summary>
/// The dream log: one JSON file per dream in the data repo's <c>dreams/</c>, named by the dream's id, the day and a
/// random id. A dream commits its own file there, so the log is in git.
/// </summary>
internal static partial class DreamLog
{
    public const string Dir = "dreams";
    private const string Extension = ".json";
    private const int IdLength = 6;

    /// <summary>A fresh id for a dream on <paramref name="now"/>'s day, with no file of that name yet.</summary>
    public static string NewId(BrainEnv env, DateTimeOffset now)
    {
        string id;
        do
        {
            id = now.UtcDateTime.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture) + "-" + RandomNumberGenerator.GetHexString(IdLength, lowercase: true);
        }
        while (File.Exists(PathOf(env, id)));
        return id;
    }

    public static string PathOf(BrainEnv env, string id) => Path.Combine(env.Home, Dir, id + Extension);

    public static void Write(BrainEnv env, DreamRecord record)
    {
        Directory.CreateDirectory(Path.Combine(env.Home, Dir));
        AtomicFile.Write(PathOf(env, record.Id), JsonSerializer.Serialize(record, DreamJson.Default.DreamRecord));
    }

    /// <summary>Every dream in the log, the newest first. A file that is not a dream is left out.</summary>
    public static IReadOnlyList<DreamRecord> All(BrainEnv env)
    {
        var dir = Path.Combine(env.Home, Dir);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(dir, "*" + Extension)
            .Where(path => IdPattern().IsMatch(Path.GetFileNameWithoutExtension(path)))
            .Select(Read)
            .OfType<DreamRecord>()
            .OrderByDescending(record => record.At)];
    }

    /// <summary>The dream <paramref name="id"/> names; refuses an id that is none.</summary>
    public static DreamRecord Find(BrainEnv env, string id) =>
        (IdPattern().IsMatch(id) && File.Exists(PathOf(env, id)) ? Read(PathOf(env, id)) : null)
        ?? throw new BrainException($"no dream {id} is in the log");

    /// <summary>The dreams that settled sittings of <paramref name="conversation"/> since <paramref name="since"/>, the oldest first; a deep dream changes no settled.md.</summary>
    public static IReadOnlyList<DreamRecord> Since(BrainEnv env, string conversation, DateTimeOffset since) =>
        [.. All(env).Where(record => record.Conversation == conversation && record.At >= since && record.Deep is null).OrderBy(record => record.At)];

    /// <summary>The dream in the file; null when it is not one, hand-edited past reading, since git keeps what it was.</summary>
    private static DreamRecord? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), DreamJson.Default.DreamRecord);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}$")]
    private static partial Regex IdPattern();
}

/// <summary>The JSON of a dream's file, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DreamRecord))]
internal sealed partial class DreamJson : JsonSerializerContext;
