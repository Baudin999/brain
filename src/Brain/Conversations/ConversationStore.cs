using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Brain.Conversations;

/// <summary>
/// One conversation: its ref, <c>&lt;place&gt;:&lt;slug&gt;</c>, the place it lives in and the kind of that place, its
/// slug, its folder and its state.
/// </summary>
internal sealed record Conversation(string Ref, string Place, string Kind, string Slug, string Dir, ConversationState State);

/// <summary>Every conversation of every place, and each <c>state.md</c> that could not be read, with why.</summary>
internal sealed record ConversationIndex(IReadOnlyList<Conversation> Conversations, IReadOnlyList<string> Problems);

/// <summary>
/// Conversations on disk. Each lives in its own folder, <c>&lt;place&gt;/&lt;slug&gt;/</c>, its slug fixed at birth from
/// the day and a random id so a new title never moves it, with its <c>state.md</c> and its <c>settled.md</c>. Every
/// change rebuilds <c>conversations.json</c> in the data repo's cache from scratch, from every <c>state.md</c>, so the
/// window reads one file and never walks the folders.
/// </summary>
internal static partial class ConversationStore
{
    public const string StateFile = "state.md";
    public const string SettledFile = "settled.md";
    public const string IndexFile = "conversations.json";
    public static readonly IReadOnlyList<string> Models = ["haiku", "sonnet", "opus", "fable"];
    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    private const char RefSeparator = ':';
    private const int IdLength = 6;

    /// <summary>Starts a conversation in <paramref name="place"/>, with its first sitting on the model and effort given.</summary>
    public static Conversation Start(BrainEnv env, string place, string model, string effort, DateTimeOffset now) =>
        Create(env, place, model, effort, now, ended: null, fill: _ => { });

    /// <summary>
    /// Parks a Claude Code session: reads its words first, so a transcript that cannot be read writes nothing, then makes a
    /// conversation in <paramref name="place"/> whose sitting 1 starts at the first word, holds them as its trail and is
    /// already ended. <c>state.md</c> is written last, once, so the index never lists a half-written sitting. A dream can
    /// settle it, and the next sitting resumes from it. The folder is not committed. The sitting runs on <paramref name="model"/>
    /// when given, else on the model the transcript records, else on <paramref name="fallback"/>. A transcript records no
    /// effort, so the sitting names <paramref name="effort"/> only when given.
    /// </summary>
    public static Conversation Park(BrainEnv env, string place, string session, string? model, string? effort, string fallback, DateTimeOffset now)
    {
        var (words, recorded) = Parking.Read(env, session);
        model ??= recorded ?? fallback;
        return Create(env, place, model, effort, words[0].At, Trim(now), dir =>
        {
            var number = 0;
            foreach (var (who, at, text) in words)
            {
                Trail.Append(Trail.PathOf(dir, 1), new Phase(++number, who, at, who == Trail.Agent ? model : null, who == Trail.Agent ? effort : null, text));
            }
        });
    }

    private static Conversation Create(BrainEnv env, string place, string model, string? effort, DateTimeOffset started, DateTimeOffset? ended, Action<string> fill)
    {
        CheckSitting(model, effort);
        var at = ConversationPlaces.Named(env, place);
        Directory.CreateDirectory(at.Dir);
        string slug;
        string dir;
        do
        {
            slug = env.Today.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture) + "-" + RandomNumberGenerator.GetHexString(IdLength, lowercase: true);
            dir = Path.Combine(at.Dir, slug);
        }
        while (Directory.Exists(dir));

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SettledFile), string.Empty);
        fill(dir);
        var state = new ConversationState(ConversationState.FirstTitle, ConversationState.Active, env.Today, [new Sitting(1, model, effort, Trim(started), ended)]);
        AtomicFile.Write(Path.Combine(dir, StateFile), state.Render());
        Rebuild(env);
        return Read(at, slug);
    }

    /// <summary>Ends the conversation's current sitting and starts the next, on the model and effort given.</summary>
    public static Conversation Sit(BrainEnv env, string reference, string model, string effort, DateTimeOffset now)
    {
        CheckSitting(model, effort);
        using var held = ConversationLock.Take(env, Find(env, reference));
        var conversation = Find(env, reference);
        var state = conversation.State;
        var started = Trim(now);
        var sittings = state.Sittings.Select(sitting => sitting.Ended is null ? sitting with { Ended = started } : sitting).ToList();
        sittings.Add(new Sitting(state.Current.Number + 1, model, effort, started, Ended: null));
        return Save(env, conversation, state with { Sittings = sittings });
    }

    /// <summary>Gives the conversation a new title; its folder stays where it is.</summary>
    public static Conversation Rename(BrainEnv env, string reference, string title)
    {
        BrainException.ThrowIf(ConversationState.TitleProblem(title));
        using var held = ConversationLock.Take(env, Find(env, reference));
        var conversation = Find(env, reference);
        return Save(env, conversation, conversation.State with { Title = title.Trim() });
    }

    /// <summary>
    /// Commits the conversation's folder, TEMP excluded, in its scope's repo, with the conversation's title and the
    /// current sitting's date as the message. A conversation whose folder is gone, removed, commits its deletion with
    /// "removed &lt;ref&gt;" as the message. Returns the commit's hash.
    /// </summary>
    public static string Commit(BrainEnv env, string reference)
    {
        var (place, slug) = Locate(env, reference);
        var dir = Path.Combine(place.Dir, slug);
        if (!Directory.Exists(dir) && Git.Tracks(place.Repo, dir))
        {
            return Git.CommitPath(place.Repo, dir, Seed.TempDir, $"removed {place.Name}{RefSeparator}{slug}");
        }

        var conversation = Find(env, reference);
        var day = conversation.State.Current.Started.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture);
        return Git.CommitPath(place.Repo, dir, Seed.TempDir, $"{conversation.State.Title}, {day}");
    }

    /// <summary>
    /// Deletes the conversation's whole folder and rebuilds the index. It refuses, deleting nothing, while a dream holds
    /// the conversation's lock or brain serve has a live sitting agent on it. It does not commit.
    /// </summary>
    public static Conversation Remove(BrainEnv env, string reference)
    {
        var conversation = Find(env, reference);
        using (ConversationLock.Take(env, conversation))
        using (ConversationLock.TakeSitting(env, conversation))
        {
            Directory.Delete(conversation.Dir, recursive: true);
            Rebuild(env);
        }

        return conversation;
    }

    /// <summary>The conversation <paramref name="reference"/> names, read from its own <c>state.md</c>.</summary>
    public static Conversation Find(BrainEnv env, string reference)
    {
        var (place, slug) = Locate(env, reference);
        var path = Path.Combine(place.Dir, slug, StateFile);
        BrainException.ThrowIf(File.Exists(path) ? null : $"no conversation {reference}: {path} does not exist");
        return Read(place, slug);
    }

    private static (ConversationPlace Place, string Slug) Locate(BrainEnv env, string reference)
    {
        var at = reference.IndexOf(RefSeparator, StringComparison.Ordinal);
        var slug = at < 0 ? string.Empty : reference[(at + 1)..];
        BrainException.ThrowIf(SlugPattern().IsMatch(slug) ? null : $"{reference} is not a conversation's ref, <scope|project>:<yyyy-mm-dd-id>");
        return (ConversationPlaces.Named(env, reference[..at]), slug);
    }

    /// <summary>Writes <c>conversations.json</c> from scratch, from every <c>state.md</c> of every place.</summary>
    public static ConversationIndex Rebuild(BrainEnv env)
    {
        var conversations = new List<Conversation>();
        var problems = new List<string>();
        foreach (var place in ConversationPlaces.All(env).Where(place => place.Problem is null && Directory.Exists(place.Dir)))
        {
            foreach (var dir in Directory.EnumerateDirectories(place.Dir).Order(StringComparer.Ordinal))
            {
                var slug = Path.GetFileName(dir);
                if (!SlugPattern().IsMatch(slug) || !File.Exists(Path.Combine(dir, StateFile)))
                {
                    continue;
                }

                try
                {
                    conversations.Add(Read(place, slug));
                }
                catch (BrainException unreadable)
                {
                    problems.Add(unreadable.Message);
                }
            }
        }

        var index = new ConversationIndex(conversations, problems);
        var cache = Path.Combine(env.Home, Layout.CacheDir);
        Directory.CreateDirectory(cache);
        AtomicFile.Write(Path.Combine(cache, IndexFile), JsonSerializer.Serialize(index, ConversationJson.Default.ConversationIndex));
        return index;
    }

    /// <summary>The index as the last change wrote it; rebuilt when there is none yet or it cannot be read.</summary>
    public static ConversationIndex Index(BrainEnv env)
    {
        var path = Path.Combine(env.Home, Layout.CacheDir, IndexFile);
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), ConversationJson.Default.ConversationIndex) ?? Rebuild(env);
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return Rebuild(env);
        }
    }

    /// <summary>The conversation in <paramref name="place"/>'s folder <paramref name="slug"/>, read from its <c>state.md</c>.</summary>
    private static Conversation Read(ConversationPlace place, string slug)
    {
        var dir = Path.Combine(place.Dir, slug);
        var path = Path.Combine(dir, StateFile);
        return new Conversation(place.Name + RefSeparator + slug, place.Name, place.Kind, slug, dir, ConversationState.Parse(File.ReadAllText(path), path));
    }

    /// <summary>Writes the conversation's <c>state.md</c> and rebuilds the index; the caller holds its lock.</summary>
    public static Conversation Save(BrainEnv env, Conversation conversation, ConversationState state)
    {
        AtomicFile.Write(Path.Combine(conversation.Dir, StateFile), state.Render());
        Rebuild(env);
        return conversation with { State = state };
    }

    /// <summary>Refuses a model or an effort a sitting cannot run on; a chat offers the same choices.</summary>
    public static void CheckSitting(string model, string? effort)
    {
        BrainException.ThrowIf(Models.Contains(model, StringComparer.Ordinal) ? null : $"model {model} is not one of {string.Join(", ", Models)}");
        BrainException.ThrowIf(effort is null || Efforts.Contains(effort, StringComparer.Ordinal) ? null : $"effort {effort} is not one of {string.Join(", ", Efforts)}");
    }

    private static DateTimeOffset Trim(DateTimeOffset now) => new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9a-f]{6}$")]
    private static partial Regex SlugPattern();
}

/// <summary>The JSON of <c>conversations.json</c>, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConversationIndex))]
internal sealed partial class ConversationJson : JsonSerializerContext;
