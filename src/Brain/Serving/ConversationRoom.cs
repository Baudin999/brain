using System.Globalization;
using System.Text.RegularExpressions;
using Brain.Children;
using Brain.Conversations;
using Brain.Dreaming;

namespace Brain.Serving;

/// <summary>
/// The conversations inbox of brain's window. It lists conversations from <c>conversations.json</c>, never by walking
/// the folders, and the places Alt+N offers from projects.tsv. Every change goes through <see cref="ConversationStore"/>,
/// the code <c>brain conversation</c> runs, so the window never writes a folder itself. Each sitting talked in has its
/// <see cref="OpenAgent"/>, one at a time per conversation, which keeps its phases in the sitting's trail. The room
/// keeps every agent it started, with its pid, and <see cref="StopAll"/> stops every one when brain serve stops or
/// restarts.
/// </summary>
internal sealed partial class ConversationRoom(BrainEnv env, TimeProvider time)
{
    private const int MaxSaid = 32 * 1024;
    private const int MaxComments = 50;
    private const int MaxQuote = 2000;

    /// <summary>How long a window waits on a sitting that does not change before it asks again.</summary>
    private static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(20);

    private readonly Lock reading = new();
    private readonly Lock sitting = new();
    private readonly Dictionary<string, Seated> agents = new(StringComparer.Ordinal);
    private bool closed;
    private bool rebuilt;

    public ConversationsView List()
    {
        var index = Index();
        return new ConversationsView(
            [.. ConversationPlaces.All(env).Select(place => new PlaceView(place.Name, place.Kind, Origin(place.Kind, place.Name), place.Problem))],
            [.. index.Conversations.Where(conversation => conversation.State.Status == ConversationState.Active).OrderByDescending(conversation => conversation.State.Current.Started).Select(Row)],
            index.Problems);
    }

    /// <summary>
    /// One conversation: its row from the index, its <c>settled.md</c>, its sittings, and the current one's phases. With
    /// <paramref name="seen"/>, the version a window last drew, it waits first until the sitting changes past it.
    /// </summary>
    public ConversationView Open(string reference, long? seen = null)
    {
        var conversation = Indexed(reference);
        var current = conversation.State.Current;
        var agent = Agent(conversation.Ref);
        if (seen is { } version && agent?.Sitting == current.Number)
        {
            agent.Agent.WaitPast(version, LongPoll);
        }

        var settled = Path.Combine(conversation.Dir, ConversationStore.SettledFile);
        var talk = agent?.Sitting == current.Number ? agent.Agent.Talk() : new AgentTalk(Trail.Read(Trail.PathOf(conversation.Dir, current.Number)), Working: false, Activity: null, Problem: null, Version: 0);
        return new ConversationView(
            Row(conversation),
            File.Exists(settled) ? File.ReadAllText(settled) : string.Empty,
            conversation.State.Sittings,
            Dreamer.Unsettled(conversation).Count,
            talk.Phases,
            [.. talk.Phases.SelectMany(phase => (phase.Comments ?? []).Select(comment => new CommentView(comment.Number, comment.Target, phase.Number, comment.Quote, comment.Text)))],
            talk.Working,
            talk.Activity,
            talk.Problem,
            talk.Version);
    }

    /// <summary>The registry's sittings: every live sitting's agent, the oldest first, with what runs under it.</summary>
    public IReadOnlyList<AgentEntry> Entries()
    {
        List<(string Ref, Seated Seated)> live;
        lock (sitting)
        {
            live = [.. agents.Where(seated => seated.Value.Agent.Alive).Select(seated => (seated.Key, seated.Value))];
        }

        return [.. live.OrderBy(seated => seated.Seated.Agent.Started).Select(seated => new AgentEntry(
            WindowTabs.Conversations, AgentEntry.Sitting, seated.Ref, string.Create(CultureInfo.InvariantCulture, $"{seated.Ref}, sitting {seated.Seated.Sitting}"),
            seated.Seated.Agent.Model, seated.Seated.Agent.Effort, seated.Seated.Agent.Started, seated.Seated.Agent.Running()))];
    }

    public ConversationView Start(StartRequest request) =>
        Open(ConversationStore.Start(env, request.Scope, request.Model, request.Effort, time.GetUtcNow()).Ref);

    /// <summary>Ends the sitting, its agent with it, and starts the next; its agent starts on the first words said in it.</summary>
    public ConversationView Sit(SittingRequest request)
    {
        var conversation = ConversationStore.Sit(env, request.Ref, request.Model, request.Effort, time.GetUtcNow());
        Retire(conversation.Ref);
        return Open(conversation.Ref);
    }

    public ConversationView Commit(RefRequest request)
    {
        ConversationStore.Commit(env, request.Ref);
        return Open(request.Ref);
    }

    /// <summary>
    /// Dreams the conversation's unsettled sittings, through the code <c>brain dream</c> runs. Each model the dream asks
    /// runs for the dreams tab while it answers.
    /// </summary>
    public ConversationView Dream(RefRequest request)
    {
        var reference = Indexed(request.Ref).Ref;
        DreamRecord dream;
        using (ClaudeRuns.For(new RunPlace(WindowTabs.Dreams, AgentEntry.Dream, reference, "dream of " + reference)))
        {
            dream = Dreamer.Dream(env, reference, time.GetUtcNow());
        }

        env.Err.WriteLine($"brain serve: dream {dream.Id} on {dream.Conversation}, {dream.Outcome}");
        return Open(request.Ref);
    }

    /// <summary>
    /// Dreams the conversation into brain, through the code <c>brain dream --deep</c> runs: a dream first when sittings are
    /// unsettled, then the open agent, whose writes wait for the user's yes in this window. Both run for the dreams tab.
    /// Refused with why when the agent failed, once the dream is logged.
    /// </summary>
    public ConversationView DeepDream(RefRequest request)
    {
        var reference = Indexed(request.Ref).Ref;
        DreamRecord dream;
        using (ClaudeRuns.For(new RunPlace(WindowTabs.Dreams, AgentEntry.Dream, reference, "dream of " + reference)))
        {
            dream = DeepDreamer.Dream(env, reference, time);
        }

        env.Err.WriteLine($"brain serve: deep dream {dream.Id} on {dream.Conversation}" + (dream.Why is { } failed ? ", failed: " + failed : string.Empty));
        BrainException.ThrowIf(dream.Why is { } why ? $"the deep dream {dream.Id} failed: {why}" : null);
        return Open(request.Ref);
    }

    /// <summary>What dreams changed in the conversation's settled.md since its current sitting started, the user's last.</summary>
    public DreamChangesView Changes(string reference)
    {
        var conversation = Indexed(reference);
        var dreams = DreamLog.Since(env, conversation.Ref, conversation.State.Current.Started);
        return dreams.Count == 0
            ? new DreamChangesView([], string.Empty, string.Empty)
            : new DreamChangesView([.. dreams.Select(DreamRow.Of)], dreams[0].Before, dreams[^1].After);
    }

    public ConversationView Rename(RenameRequest request) =>
        Open(ConversationStore.Rename(env, request.Ref, request.Title).Ref);

    /// <summary>
    /// The user's phase, sent to the sitting's agent on the sitting's model and effort. The first words of a sitting
    /// start its agent, and so do words after the agent stopped by itself; the answer streams in after this returns.
    /// </summary>
    public ConversationView Say(SayRequest request)
    {
        var conversation = Indexed(request.Ref);
        var comments = Comments(request.Comments ?? [], Trail.Read(Trail.PathOf(conversation.Dir, conversation.State.Current.Number)).Count);
        BrainException.ThrowIf(string.IsNullOrWhiteSpace(request.Text) && comments.Count == 0 ? "say something first" : request.Text?.Length > MaxSaid ? $"a phase is at most {MaxSaid} characters" : null);
        lock (sitting)
        {
            BrainException.ThrowIf(closed ? "brain serve is stopping; say it again in the window it opens" : null);
            using var held = ConversationLock.Take(env, conversation);
            var current = conversation.State.Current;
            if (!agents.TryGetValue(conversation.Ref, out var seated) || !seated.Agent.Alive || seated.Sitting != current.Number)
            {
                seated?.Agent.Stop();
                seated = Seat(conversation);
                agents[conversation.Ref] = seated;
                env.Err.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brain serve: agent {seated.Agent.Pid} for {conversation.Ref} sitting {current.Number}"));
            }

            seated.Agent.Say(request.Text?.Trim() ?? string.Empty, comments);
        }

        return Open(conversation.Ref);
    }

    /// <summary>Takes the sitting lock and starts the sitting's agent, which holds it until it ends; the lock goes again when the agent cannot start.</summary>
    private Seated Seat(Conversation conversation)
    {
        IDisposable? held = ConversationLock.TakeSitting(env, conversation);
        try
        {
            var seated = new Seated(conversation.State.Current.Number, Sit(conversation, held));
            held = null;
            return seated;
        }
        finally
        {
            held?.Dispose();
        }
    }

    /// <summary>
    /// Starts the open agent for the conversation's current sitting, in its place's repo, with <c>research/</c> and
    /// <c>TEMP/</c> ready. It starts from the sitting's trail and keeps every phase there.
    /// </summary>
    private OpenAgent Sit(Conversation conversation, IDisposable held)
    {
        Seed.Prepare(conversation);
        var sitting = conversation.State.Current;
        var trail = Trail.PathOf(conversation.Dir, sitting.Number);
        var place = new RunPlace(WindowTabs.Conversations, AgentEntry.Sitting, conversation.Ref, string.Create(CultureInfo.InvariantCulture, $"{conversation.Ref}, sitting {sitting.Number}"));
        var start = new AgentStart(place, sitting.Model, sitting.Effort ?? BrainSettings.Read(env.Home).Sitting.Effort, Trail.Read(trail), words => Seed.For(conversation, words), phase => Trail.Append(trail, phase), held);
        return OpenAgent.Start(start, env.Claude, ConversationPlaces.Named(env, conversation.Place).Repo, time);
    }

    /// <summary>
    /// The comments a window sends with a phase, numbered in the order they came. A quote is one line, as the trail holds
    /// it, and a comment keeps its line breaks; each is about settled or a phase the sitting already has.
    /// </summary>
    private static List<PhaseComment> Comments(IReadOnlyList<CommentDraft> drafts, int phases)
    {
        BrainException.ThrowIf(drafts.Count > MaxComments ? $"a phase holds at most {MaxComments} comments" : null);
        var comments = new List<PhaseComment>();
        foreach (var draft in drafts)
        {
            var quote = Flat(draft.Quote);
            var text = (draft.Text ?? string.Empty).Trim();
            BrainException.ThrowIf(
                draft.Target < Trail.Settled || draft.Target > phases ? $"comment {comments.Count + 1} is about phase {draft.Target}, which this sitting does not have"
                : quote.Length == 0 || quote.Length > MaxQuote ? $"comment {comments.Count + 1} quotes between 1 and {MaxQuote} characters"
                : text.Length == 0 || text.Length > MaxSaid ? $"comment {comments.Count + 1} says nothing, or too much"
                : null);
            comments.Add(new PhaseComment(comments.Count + 1, draft.Target, quote, text));
        }

        return comments;
    }

    private static string Flat(string? text) => WhiteSpace().Replace(text ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    /// <summary>Stops every sitting's agent and everything it started, and starts no more: brain serve stops.</summary>
    public void StopAll()
    {
        List<OpenAgent> stopping;
        lock (sitting)
        {
            closed = true;
            stopping = [.. agents.Values.Select(seated => seated.Agent)];
            agents.Clear();
        }

        foreach (var agent in stopping)
        {
            agent.Stop();
            env.Err.WriteLine(string.Create(CultureInfo.InvariantCulture, $"brain serve: stopped agent {agent.Pid}"));
        }
    }

    private Seated? Agent(string reference)
    {
        lock (sitting)
        {
            return agents.GetValueOrDefault(reference);
        }
    }

    private void Retire(string reference)
    {
        Seated? seated;
        lock (sitting)
        {
            agents.Remove(reference, out seated);
        }

        seated?.Agent.Stop();
    }

    /// <summary>
    /// The index, rebuilt from every state.md the first time this brain serve reads it, so conversations a pull or a clone
    /// brought in while it was down show at once; after that as the last change wrote it.
    /// </summary>
    private ConversationIndex Index()
    {
        lock (reading)
        {
            if (rebuilt)
            {
                return ConversationStore.Index(env);
            }

            rebuilt = true;
            return ConversationStore.Rebuild(env);
        }
    }

    private Conversation Indexed(string reference) =>
        Index().Conversations.FirstOrDefault(conversation => conversation.Ref == reference)
        ?? throw new BrainException($"no conversation {reference} is in the index");

    private static ConversationRow Row(Conversation conversation) =>
        new(conversation.Ref, conversation.Place, conversation.State.Title, conversation.State.Status, conversation.State.Born, conversation.State.Sittings.Count, conversation.State.Current, Origin(conversation.Kind, conversation.Place));

    /// <summary>A project wears its own color, as every inbox shows it; personal and global are neutral and named.</summary>
    public static OriginView Origin(string kind, string place) =>
        kind == ScopeNames.Project ? ReviewViews.Project(place) : new OriginView(kind, null, null);
}

/// <summary>The open agent of a conversation and the sitting it talks in.</summary>
internal sealed record Seated(int Sitting, OpenAgent Agent);

/// <summary>A place Alt+N offers: its name, its kind, its chip, and why it cannot hold a conversation when it cannot.</summary>
internal sealed record PlaceView(string Name, string Kind, OriginView Origin, string? Problem);

/// <summary>A conversation as the inbox lists it.</summary>
internal sealed record ConversationRow(string Ref, string Scope, string Title, string Status, DateOnly Born, int Sittings, Sitting Current, OriginView Origin);

internal sealed record ConversationsView(IReadOnlyList<PlaceView> Places, IReadOnlyList<ConversationRow> Conversations, IReadOnlyList<string> Problems);

/// <summary>
/// A conversation open in the window: how many sittings no dream settled yet, the current sitting's phases and the
/// comments in their margin, whether the agent is answering and the tool it runs, why it stopped when it did, and the
/// version a window waits past.
/// </summary>
internal sealed record ConversationView(
    ConversationRow Row,
    string Settled,
    IReadOnlyList<Sitting> Sittings,
    int Unsettled,
    IReadOnlyList<Phase> Phases,
    IReadOnlyList<CommentView> Comments,
    bool Working,
    string? Activity,
    string? Problem,
    long Version);

/// <summary>
/// A comment in the margin of a trail: its number in the phase that carries it, the phase it anchors to (0 is settled),
/// that carrying phase, the text it quotes there, and what it says. It anchors to the phase and the quote, never to a
/// character offset, so a rewritten passage leaves it orphaned, not lost.
/// </summary>
internal sealed record CommentView(int Number, int Phase, int In, string Quote, string Text);

/// <summary>A comment the window sends with a phase: the phase it is about, 0 for settled, the text it quotes and what it says.</summary>
internal sealed record CommentDraft(int Target, string Quote, string Text);

internal sealed record StartRequest(string Scope, string Model, string Effort);

internal sealed record SittingRequest(string Ref, string Model, string Effort);

/// <summary>A call that names one conversation: a commit, a dream or a deep dream.</summary>
internal sealed record RefRequest(string Ref);

internal sealed record RenameRequest(string Ref, string Title);

internal sealed record SayRequest(string Ref, string? Text, IReadOnlyList<CommentDraft>? Comments = null);
