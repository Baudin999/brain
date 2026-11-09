using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brain.Conversations;
using Brain.Search;

namespace Brain;

/// <summary>
/// The user's settings: <c>settings.json</c> at the data repo's root, committed there, so a fresh install with one pull
/// has the same settings. It is no graph node and passes no gate. A missing file or a missing key is that key's default.
/// A key holds a choice of a Claude model and its effort, the dreamer's two choices, a switch, or the name of the
/// embedding model search runs on, which only a finished switch of models sets. Every effort is one a
/// sitting runs at. A file written before that may still name the effort <c>default</c>, which reads as medium, the
/// model's own default effort. A value the key cannot hold is refused, when read as when saved, and a key brain does not
/// know is kept as it is.
/// </summary>
internal sealed class BrainSettings
{
    public const string FileName = "settings.json";

    /// <summary>The key of the embedding model search runs on.</summary>
    public const string EmbedderKey = "embedder";
    private const string OldDefaultEffort = "default";
    private const string Medium = "medium";
    private const string ModelField = "model";
    private const string EffortField = "effort";
    private const string ChatKey = "chat";
    private const string SittingKey = "sitting";
    private const string JudgeKey = "judge";
    private const string DreamerKey = "dreamer";
    private const string DeepDreamKey = "deep-dream";

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>What a model setting holds, in words.</summary>
    private static readonly string Expected =
        $"a model of {string.Join(", ", ConversationStore.Models)} with an effort of {string.Join(", ", ConversationStore.Efforts)}";

    /// <summary>The closed agents the open agent sends, in the order the file lists them.</summary>
    private static readonly string[] AgentKeys = ["builder-easy", "builder-difficult", "validator", "readiness"];

    /// <summary>Every key, in the order the file and the window list them, with its default.</summary>
    private static readonly Setting[] Known =
    [
        Model(AgentKeys[0], "sonnet", Medium),
        Model(AgentKeys[1], "opus", Medium),
        Model(AgentKeys[2], "opus", Medium),
        Model(AgentKeys[3], "sonnet", Medium),
        Model(JudgeKey, "sonnet", Medium),
        new(DreamerKey, new JsonArray(Choice("haiku", Medium), Choice("sonnet", Medium)), "two, each " + Expected, IsPair),
        Model(DeepDreamKey, "opus", Medium),
        Model(ChatKey, "sonnet", Medium),
        Model(SittingKey, "sonnet", "low"),
        Switch("starfield"),
        Switch("motion"),
        Switch("glow"),
        Switch("sparks"),
        Switch("newstars"),
        Switch("fading"),
        Switch("glide"),
        Switch("atlas"),
        new(
            EmbedderKey,
            EmbedderProfiles.Default.Name,
            "one of " + string.Join(", ", EmbedderProfiles.All.Select(profile => profile.Name)),
            node => IsOneOf(node, [.. EmbedderProfiles.All.Select(profile => profile.Name)])),
    ];

    /// <summary>Held while a change writes and commits the file, so two changes never commit at once.</summary>
    private static readonly Lock Changing = new();

    private readonly JsonObject values;

    private BrainSettings(JsonObject values) => this.values = values;

    /// <summary>The model and effort a new chat starts on.</summary>
    public (string Model, string Effort) Chat => Chosen(ChatKey);

    /// <summary>The model and effort a new sitting starts on.</summary>
    public (string Model, string Effort) Sitting => Chosen(SittingKey);

    /// <summary>The model and effort the open agent of a deep dream runs on.</summary>
    public (string Model, string Effort) DeepDream => Chosen(DeepDreamKey);

    /// <summary>The model and effort the judge asks.</summary>
    public (string Model, string Effort) Judge => Chosen(JudgeKey);

    /// <summary>Each closed agent's key with the model and effort the open agent sends it with, in the order the file lists them.</summary>
    public IReadOnlyList<(string Key, string Model, string Effort)> Agents =>
        [.. AgentKeys.Select(key =>
        {
            var (model, effort) = Chosen(key);
            return (key, model, effort);
        })];

    /// <summary>The name of the embedding model search runs on.</summary>
    public string Embedder => values[EmbedderKey]!.GetValue<string>();

    /// <summary>The dreamer's two models, each with its effort, in the order the file names them.</summary>
    public IReadOnlyList<(string Model, string Effort)> Dreamer => [.. values[DreamerKey]!.AsArray().Select(choice => Of(choice!))];

    /// <summary>The settings in the data repo now: each key's value in the file, else its default.</summary>
    public static BrainSettings Read(string home)
    {
        var file = Stored(home);
        var values = new JsonObject();
        foreach (var setting in Known)
        {
            var value = AtMedium(file[setting.Key]?.DeepClone()) ?? setting.Default.DeepClone();
            BrainException.ThrowIf(setting.Fits(value) ? null : $"{FileName} in {home}: {setting.Key} is {value.ToJsonString()}, not {setting.Expects}");
            values[setting.Key] = value;
        }

        return new BrainSettings(values);
    }

    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="value"/>: writes the file and commits it in the data repo with only
    /// <c>settings.json</c> staged, refusing when anything else is staged there. A value equal to the one in effect
    /// changes nothing. When the commit fails the file is put back as it was. Changes in one process go one at a time.
    /// </summary>
    public static BrainSettings Change(string home, string key, JsonNode? value)
    {
        lock (Changing)
        {
            return Commit(home, key, value);
        }
    }

    /// <summary>Every key with the value in effect, as the window reads them.</summary>
    public string ToJson() => values.ToJsonString();

    private static BrainSettings Commit(string home, string key, JsonNode? value)
    {
        var setting = Known.FirstOrDefault(known => known.Key == key) ?? throw new BrainException($"{key} is no setting");
        BrainException.ThrowIf(value is not null && setting.Fits(value) ? null : $"{key} is {value?.ToJsonString() ?? "null"}, not {setting.Expects}");
        var now = Read(home);
        if (JsonNode.DeepEquals(now.values[key], value))
        {
            return now;
        }

        var path = Path.Combine(home, FileName);
        var before = File.Exists(path) ? File.ReadAllText(path) : null;
        var file = Stored(home);
        file[key] = value!.DeepClone();
        AtomicFile.Write(path, file.ToJsonString(Written) + "\n");
        try
        {
            Git.CommitPath(home, path, skip: null, $"Settings: {key} is {Spoken(value)}");
        }
        catch (BrainException)
        {
            if (before is null)
            {
                File.Delete(path);
            }
            else
            {
                AtomicFile.Write(path, before);
            }

            throw;
        }

        return Read(home);
    }

    private static JsonObject Stored(string home)
    {
        var path = Path.Combine(home, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllBytes(path)) as JsonObject ?? throw new BrainException($"{path} is not a JSON object");
        }
        catch (JsonException error)
        {
            throw new BrainException($"{path} is not JSON: {error.Message}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new BrainException($"{path} cannot be opened: {error.Message}");
        }
    }

    private static string Spoken(JsonNode value) => value switch
    {
        JsonArray pair => string.Join(" and ", pair.Select(each => Spoken(each!))),
        JsonObject choice => $"{choice[ModelField]!.GetValue<string>()} at {choice[EffortField]!.GetValue<string>()}",
        JsonValue on when on.TryGetValue(out bool yes) => yes ? "on" : "off",
        JsonValue named when named.TryGetValue(out string? text) => text,
        _ => value.ToJsonString(),
    };

    /// <summary><paramref name="value"/> with each choice that names the old effort <c>default</c> at medium instead.</summary>
    private static JsonNode? AtMedium(JsonNode? value)
    {
        foreach (var choice in value is JsonArray pair ? [.. pair] : (JsonNode?[])[value])
        {
            if (choice is JsonObject named && IsOneOf(named[EffortField], [OldDefaultEffort]))
            {
                named[EffortField] = Medium;
            }
        }

        return value;
    }

    private static (string Model, string Effort) Of(JsonNode choice) =>
        (choice[ModelField]!.GetValue<string>(), choice[EffortField]!.GetValue<string>());

    private (string Model, string Effort) Chosen(string key) => Of(values[key]!);

    private static JsonObject Choice(string model, string effort) => new() { [ModelField] = model, [EffortField] = effort };

    private static Setting Model(string key, string model, string effort) => new(key, Choice(model, effort), Expected, IsChoice);

    /// <summary>A model of <see cref="ConversationStore.Models"/> and an effort of <see cref="ConversationStore.Efforts"/>, and nothing more.</summary>
    private static bool IsChoice(JsonNode? node) =>
        node is JsonObject { Count: 2 } choice && IsOneOf(choice[ModelField], ConversationStore.Models) && IsOneOf(choice[EffortField], ConversationStore.Efforts);

    private static Setting Switch(string key) => new(key, true, "true or false", node => node is JsonValue value && value.TryGetValue(out bool _));

    private static bool IsOneOf(JsonNode? node, IReadOnlyList<string> choices) =>
        node is JsonValue value && value.TryGetValue(out string? text) && choices.Contains(text);

    private static bool IsPair(JsonNode? node) =>
        node is JsonArray { Count: 2 } pair && pair.All(IsChoice);

    /// <summary>One key: its name, its default, what it holds in words, and the check that a value is one it holds.</summary>
    private sealed record Setting(string Key, JsonNode Default, string Expects, Func<JsonNode?, bool> Fits);
}
