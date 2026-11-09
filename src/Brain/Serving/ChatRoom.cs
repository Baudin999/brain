using System.Globalization;
using System.Security.Cryptography;
using Brain.Children;
using Brain.Conversations;

namespace Brain.Serving;

/// <summary>
/// The chats of brain's window and the registry of what runs for them, in brain serve's memory only. A chat is the open
/// agent, summoned on one of the window's tabs and gone when the user closes it. It is the same agent a sitting runs,
/// on the model and effort the settings name for a chat unless the user picks another. Each tab holds at most one chat, and the
/// chats of several tabs live at once: switching tabs ends none. A chat keeps nothing: no folder, no trail and no index
/// entry, only the phases its agent holds in memory, so closing it or brain serve stopping ends it for good. The
/// registry is keyed by tab and holds each chat's model, effort and start, and through its agent what runs under it.
/// Nothing of it is ever written to disk.
/// </summary>
internal sealed class ChatRoom(BrainEnv env, TimeProvider time)
{
    private const int MaxSaid = 32 * 1024;
    private const int MaxTab = 64;

    /// <summary>The most bytes a summons may send, its context included: the one limit on a chat's context.</summary>
    public const int MaxStartBody = 1024 * 1024;

    private const int IdLength = 16;

    /// <summary>How long a window waits on a chat that does not change before it asks again.</summary>
    private static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(20);

    private readonly Lock gate = new();
    private readonly Dictionary<string, Chat> chats = new(StringComparer.Ordinal);
    private bool closed;

    /// <summary>
    /// Summons a chat on a tab that holds none. It starts with the context the window sends, what was in view when the
    /// user summoned it, or with none. Its agent starts on the first words said, in the data repo, as a personal or
    /// global sitting's does.
    /// </summary>
    public ChatView Start(ChatStartRequest request)
    {
        var tab = request.Tab?.Trim() ?? string.Empty;
        var place = request.Place?.Trim() is { Length: > 0 } named ? named : tab;
        BrainException.ThrowIf(tab.Length == 0 ? "a chat is summoned on a tab" : !Fits(tab) || !Fits(place) ? "that is no tab of the window" : null);
        var context = request.Context?.Trim() ?? string.Empty;
        var first = request.Model is null || request.Effort is null ? BrainSettings.Read(env.Home).Chat : default;
        var model = request.Model ?? first.Model;
        var effort = request.Effort ?? first.Effort;
        ConversationStore.CheckSitting(model, effort);
        lock (gate)
        {
            BrainException.ThrowIf(
                closed ? "brain serve is stopping; summon the chat again in the window it opens"
                : chats.ContainsKey(tab) ? "this tab holds a chat already; close it first"
                : null);
            var started = new Chat(RandomNumberGenerator.GetHexString(IdLength, lowercase: true), tab, place, context, model, effort, time.GetUtcNow());
            chats[tab] = started;
            return View(started);
        }
    }

    /// <summary>The chat now. With <paramref name="seen"/>, the version a window last drew, it waits first until it changes past it.</summary>
    public ChatView Open(string id, long? seen = null)
    {
        var open = Named(id);
        if (seen is { } version)
        {
            open.Agent?.WaitPast(version, LongPoll);
        }

        return View(open);
    }

    /// <summary>The registry's chats: every chat open, by tab, the oldest first, with what runs under it.</summary>
    public IReadOnlyList<AgentEntry> Entries()
    {
        List<Chat> open;
        lock (gate)
        {
            open = [.. chats.Values.OrderBy(chat => chat.Started)];
        }

        return [.. open.Select(chat => new AgentEntry(chat.Tab, AgentEntry.Chat, chat.Id, chat.Place, chat.Model, chat.Effort, chat.Started, chat.Agent?.Running() ?? []))];
    }

    /// <summary>The user's words, sent to the chat's agent; the first words start it. The answer streams in after this returns.</summary>
    public ChatView Say(ChatSayRequest request)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        BrainException.ThrowIf(text.Length == 0 ? "say something first" : text.Length > MaxSaid ? $"a chat's words are at most {MaxSaid} characters" : null);
        lock (gate)
        {
            var open = Named(request.Id ?? string.Empty);
            BrainException.ThrowIf(open.Agent is { Alive: false } ? "this chat's agent stopped; close it and summon a new one" : null);
            if (open.Agent is null)
            {
                var start = new AgentStart(new RunPlace(open.Tab, AgentEntry.Chat, open.Id, open.Place), open.Model, open.Effort, [], words => Seed(open.Place, open.Context, words), Keep: _ => { });
                open.Agent = OpenAgent.Start(start, env.Claude, env.Home, time);
                env.Err.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brain serve: agent {open.Agent.Pid} for a chat on {open.Tab}"));
            }

            open.Agent.Say(text, []);
            return View(open);
        }
    }

    /// <summary>
    /// Closes the chat: its agent and everything it started stop, and nothing of it is left. While an agent it sent or a
    /// process it started still runs, it closes only on <paramref name="stop"/>, the user's word that those stop too, and
    /// otherwise returns what runs, so the window can name it and ask. Closing one already gone changes nothing.
    /// </summary>
    public IReadOnlyList<AgentWork> Close(string id, bool stop)
    {
        Chat? ended;
        lock (gate)
        {
            ended = chats.Values.FirstOrDefault(chat => chat.Id == id);
            if (ended is null)
            {
                return [];
            }

            if (!stop && ended.Agent?.Running() is { Count: > 0 } running)
            {
                return running;
            }

            chats.Remove(ended.Tab);
        }

        End(ended);
        return [];
    }

    /// <summary>Ends every chat and starts no more: brain serve stops.</summary>
    public void StopAll()
    {
        List<Chat> ended;
        lock (gate)
        {
            closed = true;
            ended = [.. chats.Values];
            chats.Clear();
        }

        foreach (var chat in ended)
        {
            End(chat);
        }
    }

    /// <summary>
    /// What the agent reads before the user's first words in a chat: the tab, and the context the window sent, which
    /// stays what was in view at the summons however the user moves afterwards.
    /// </summary>
    public static string Seed(string place, string context, string words)
    {
        var start = context.Length == 0
            ? "it starts with no context."
            : $"it starts with the context below, what was in view when they summoned it. It does not change when they select something else; to talk about something else they close the chat and summon a new one.\n\n<context>\n{context}\n</context>";
        return $"""
        brain serve started you for a chat in brain's window. The user talks with you there, not in a terminal, and what you write is drawn as markdown, mermaid included. The user summoned this chat on the {place} tab of the window, and {start}

        A chat keeps nothing: no folder, no trail, and no list of past chats. When the user closes it, you stop, and what it taught lasts only where you put it, in the graph through the brain CLI, waiting for the user's yes in the window, or in a commit.

        The user:


        """ + words;
    }

    /// <summary>True for a tab's name or label: short, one line.</summary>
    private static bool Fits(string tab) => tab.Length <= MaxTab && !tab.Any(char.IsControl);

    private void End(Chat ended)
    {
        if (ended.Agent is { } agent)
        {
            agent.Stop();
            env.Err.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brain serve: stopped agent {agent.Pid}"));
        }
    }

    private Chat Named(string id)
    {
        lock (gate)
        {
            return chats.Values.FirstOrDefault(chat => chat.Id == id) ?? throw new BrainException("this chat is closed; summon a new one");
        }
    }

    private static ChatView View(Chat chat)
    {
        var talk = chat.Agent?.Talk() ?? new AgentTalk([], Working: false, Activity: null, Problem: null, Version: 0);
        return new ChatView(chat.Id, chat.Tab, chat.Place, chat.Model, chat.Effort, talk.Phases, talk.Working, talk.Activity, talk.Problem, talk.Version);
    }

    /// <summary>A chat open on a tab: its id, which only the window holds, its model, effort and start, and its agent once it started.</summary>
    private sealed class Chat(string id, string tab, string place, string context, string model, string effort, DateTimeOffset started)
    {
        public string Id { get; } = id;

        public string Tab { get; } = tab;

        /// <summary>The tab's label, as the window names it.</summary>
        public string Place { get; } = place;

        /// <summary>What was in view when the chat was summoned, as the window described it; empty for none.</summary>
        public string Context { get; } = context;

        public string Model { get; } = model;

        public string Effort { get; } = effort;

        public DateTimeOffset Started { get; } = started;

        public OpenAgent? Agent { get; set; }
    }
}

/// <summary>A chat open in the window: its tab and the tab's label, what was said, whether the agent answers and the tool it runs, why it stopped when it did, and the version a window waits past.</summary>
internal sealed record ChatView(string Id, string Tab, string Place, string Model, string Effort, IReadOnlyList<Phase> Phases, bool Working, string? Activity, string? Problem, long Version);


/// <summary>Summons a chat on a tab, named by the label it shows, with the context the window drew from what was in view; a model and an effort left out are Sonnet and medium.</summary>
internal sealed record ChatStartRequest(string? Tab = null, string? Place = null, string? Context = null, string? Model = null, string? Effort = null);

internal sealed record ChatSayRequest(string? Id, string? Text);

/// <summary>Closes a chat; with stop, also while what it started still runs.</summary>
internal sealed record ChatCloseRequest(string? Id, bool Stop = false);

/// <summary>A close refused while work runs under the chat: why, and what would stop.</summary>
internal sealed record ChatRunningView(string Error, IReadOnlyList<AgentWork> Running);
