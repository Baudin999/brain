using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Brain.Installing;

/// <summary>
/// The settings.json brain writes into Claude Code's config directory: the harness's settings.json, then the data
/// repo's own harness/settings.json when it has one, then the permission mode the user picked and, when the brain
/// lives anywhere but its default place, BRAIN_HOME. Objects merge key by key, lists append and anything else takes
/// the later value. Then every PreToolUse command hook, brain's and the data repo's alike, runs through refusal.sh,
/// once, so each refusal lands in the usage log, and every command hook finds the hooks in the real config directory:
/// the ~/.claude/ the harness writes becomes that directory as the shell spells it. It is written as jq writes it, two
/// spaces deep.
/// </summary>
internal static class SettingsFile
{
    private const string Permissions = "permissions";
    private const string DefaultMode = "defaultMode";
    private const string Env = "env";
    private const string HomeVariable = "BRAIN_HOME";
    private const string ShippedDir = "~/.claude/";
    private const string Refusal = ShippedDir + "hooks/refusal.sh";

    private static readonly JsonSerializerOptions Written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The settings merged from <paramref name="sources"/>, with <paramref name="mode"/> and, unless null,
    /// <paramref name="brainHome"/>, the hook commands reaching the config directory as <paramref name="claudeHome"/>
    /// spells it.
    /// </summary>
    public static byte[] Merge(IEnumerable<byte[]> sources, string mode, string? brainHome, string claudeHome)
    {
        JsonNode? merged = new JsonObject();
        foreach (var source in sources)
        {
            merged = Merged(merged, JsonNode.Parse(source));
        }

        var last = new JsonObject { [Permissions] = new JsonObject { [DefaultMode] = mode } };
        if (brainHome is not null)
        {
            last[Env] = new JsonObject { [HomeVariable] = brainHome };
        }

        var settings = Merged(merged, last)!;
        foreach (var hook in CommandHooks(settings["hooks"]?["PreToolUse"]))
        {
            var command = hook["command"]!.GetValue<string>();
            if (!command.StartsWith(Refusal, StringComparison.Ordinal))
            {
                hook["command"] = Refusal + " " + command;
            }
        }

        foreach (var hook in CommandHooks(settings["hooks"]))
        {
            hook["command"] = hook["command"]!.GetValue<string>().Replace(ShippedDir, claudeHome, StringComparison.Ordinal);
        }

        return Encoding.UTF8.GetBytes(settings.ToJsonString(Written) + "\n");
    }

    /// <summary>
    /// The config directory <paramref name="claudeHome"/> as a hook command spells it, with a slash after it: under ~
    /// when it lies in <paramref name="userHome"/>, so ~/.claude stays ~/.claude, else in full, and quoted when the
    /// shell would split it.
    /// </summary>
    public static string Spelled(string claudeHome, string userHome)
    {
        var relative = Path.GetRelativePath(userHome, claudeHome);
        var spelled = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? claudeHome : "~/" + relative;
        return spelled.All(each => char.IsAsciiLetterOrDigit(each) || "/._-+~".Contains(each, StringComparison.Ordinal))
            ? spelled + "/"
            : "'" + claudeHome.Replace("'", "'\\''", StringComparison.Ordinal) + "'/";
    }

    /// <summary>Every command hook under <paramref name="node"/>: an event's list of matchers, or the hooks of every event.</summary>
    private static IEnumerable<JsonObject> CommandHooks(JsonNode? node)
    {
        var matchers = node switch
        {
            JsonArray list => list,
            JsonObject events => events.Select(each => each.Value).OfType<JsonArray>().SelectMany(list => list),
            _ => [],
        };
        return matchers.Select(matcher => matcher?["hooks"]).OfType<JsonArray>().SelectMany(hooks => hooks).OfType<JsonObject>()
            .Where(hook => hook["type"]?.GetValue<string>() == "command" && hook["command"] is JsonValue);
    }

    /// <summary>The permission mode a settings.json sets, or null when it sets none or cannot be read.</summary>
    public static string? Mode(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllBytes(path))?[Permissions]?[DefaultMode] is JsonValue value && value.TryGetValue(out string? mode) ? mode : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static JsonNode? Merged(JsonNode? first, JsonNode? second)
    {
        if (first is JsonObject left && second is JsonObject right)
        {
            var result = (JsonObject)left.DeepClone();
            foreach (var (key, value) in right)
            {
                result[key] = Merged(result[key], value);
            }

            return result;
        }

        if (first is JsonArray head && second is JsonArray tail)
        {
            return new JsonArray([.. head.Concat(tail).Select(item => item?.DeepClone())]);
        }

        return second?.DeepClone();
    }
}
