using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Brain.Questions;
using Brain.Search;
using Brain.Staging;

namespace Brain.Serving;

/// <summary>
/// What brain's window reads: every write before the user in full, every write the user answered in the last two days,
/// a node with its edges resolved to their neighbours, and search hits from the ranking lookup uses. It reads only;
/// answers go through <see cref="PendingStore.Answer"/> and undos through <see cref="PendingStore.Undo"/>.
/// </summary>
internal static class ReviewViews
{
    private const string New = "new";
    private const string Forgotten = "forgotten";
    private const string Rewritten = "rewritten";
    private const string EdgesOnly = "edges";
    private const string FromProject = "project";
    private const int Wheel = 360;

    /// <summary>
    /// Changes as soon as a write is asked or answered, or a node file of personal, global or a registered project is
    /// added, removed or changed by anything, so the window refetches only then. It reads the graph folders' file names,
    /// sizes and modification times, never a node, because the window asks for it on every poll.
    /// </summary>
    public static string Version(BrainEnv env)
    {
        var asked = new PendingStore(env.Home).Asked().Select(write => write.Digest);
        var files = Focuses(env)
            .Select(focus => focus.Path)
            .Where(Directory.Exists)
            .SelectMany(dir => new DirectoryInfo(dir).EnumerateFiles(Layout.NodePattern))
            .Select(file => string.Create(CultureInfo.InvariantCulture, $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"))
            .Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', asked.Concat(files)))));
    }

    /// <summary>
    /// What the window can focus on: personal, global and every project in projects.tsv, in that order, each with the
    /// folder of its graph. A project's folder comes from <see cref="ProjectScaffold.GraphDir"/>, not
    /// <see cref="ScopeResolver.ForRepo"/>, whose edge log key runs git once per project, and this runs on every poll.
    /// </summary>
    public static IReadOnlyList<FocusView> Focuses(BrainEnv env) =>
    [
        new FocusView(ScopeNames.Personal, null, ScopeResolver.BrainScope(env.Home, ScopeNames.Personal).GraphDir),
        new FocusView(ScopeNames.Global, null, ScopeResolver.BrainScope(env.Home, ScopeNames.Global).GraphDir),
        .. ProjectRegistry.All(env.Home).Select(project => new FocusView(project.Name, Hue(project.Name), ProjectScaffold.GraphDir(project.Root(env.UserHome)))),
    ];

    public static IReadOnlyList<ReviewItem> Pending(BrainEnv env)
    {
        var graph = Graph.Load(env);
        return [.. new PendingStore(env.Home).Asked().Select(asked => Item(env, graph, asked))];
    }

    /// <summary>
    /// Every write the user answered in the last two days, the newest first, each in full as it was asked, with its
    /// answer, its time and, for a yes not undone, every node that no longer holds the text the yes left, which an undo
    /// refuses on.
    /// </summary>
    public static IReadOnlyList<AnsweredItem> Answered(BrainEnv env)
    {
        var graph = Graph.Load(env);
        return [.. new PendingStore(env.Home).Answered().Select(answered => Item(env, graph, answered))];
    }

    public static NodeView Node(BrainEnv env, string reference)
    {
        var graph = Graph.Load(env);
        var node = graph.Require(reference);
        return View(graph, node, node.Ref, node.Node);
    }

    public static SearchResult Search(BrainEnv env, string words)
    {
        if (string.IsNullOrWhiteSpace(words))
        {
            return new SearchResult([], KeywordsOnly: null);
        }

        var ranked = Ranking.Rank(env, Graph.Load(env), node => true, words);
        return new SearchResult(
            [.. ranked.Hits.Select(SearchHit.Of)],
            ranked.KeywordsOnly ? env.Embedder.Silence : null);
    }

    /// <summary>
    /// The entries nearest to each node a write before the user writes, by <see cref="Brain.Search.Nearest"/>, the query
    /// the command line printed under it. It reads only, so nothing here changes what a yes lands.
    /// </summary>
    public static IReadOnlyList<NearestView> Nearest(BrainEnv env, string id)
    {
        var asked = new PendingStore(env.Home).Asked().FirstOrDefault(write => write.Write.Id == id)
            ?? throw new BrainException($"no write {id} is before you");
        return
        [
            .. asked.Write.Changes
                .Select(change => (change.Ref, Ranked: Brain.Search.Nearest.ToChange(env, change, Ranking.Limit)))
                .Where(near => near.Ranked is not null)
                .Select(near => new NearestView(near.Ref, [.. near.Ranked!.Hits.Select(Near)], near.Ranked.KeywordsOnly ? env.Embedder.Silence : null)),
        ];
    }

    /// <summary>An entry with its scope, and for a registered project the project's hue, so it wears the project's color.</summary>
    private static NearHit Near(Found hit) =>
        new(
            hit.Node.Ref,
            hit.Node.Node.Title,
            hit.Node.Node.Field(FieldNames.Kind),
            hit.Snippet,
            hit.Node.Scope.Name,
            GraphOf(hit.Node.Scope).Hue);

    /// <summary>
    /// A write with the graphs its changes land in, each once: the graph a node file is in decides, never the session
    /// that staged it, so a personal note staged from a project session is personal's.
    /// </summary>
    private static ReviewItem Item(BrainEnv env, Graph graph, AskedWrite asked) =>
        new(
            asked.Write.Id,
            asked.Digest,
            asked.AskedAt,
            asked.Write.Lines,
            [.. asked.Write.Changes.Select(change => Change(graph, change))],
            GraphsOf(env, asked.Write));

    /// <summary>An answered write as the window lists it, in the graphs its changes land in, as a write before the user is.</summary>
    private static AnsweredItem Item(BrainEnv env, Graph graph, AnsweredWrite answered)
    {
        var record = answered.Record;
        var landed = record.Answer.Verdict == PendingAnswer.Yes && record.UndoneAt is null;
        return new AnsweredItem(
            record.Write.Id,
            answered.Digest,
            record.AnsweredAt,
            record.Answer.Verdict,
            record.Answer.Reason,
            record.Answer.Refusal,
            record.UndoneAt,
            record.Write.Lines,
            [.. record.Write.Changes.Select(change => Change(graph, change))],
            landed ? [.. record.Write.Changes.Select(Since).OfType<ChangedSince>()] : [],
            GraphsOf(env, record.Write));
    }

    /// <summary>The node as the yes left it and as it reads now, when the two differ; null when it holds what the yes left.</summary>
    private static ChangedSince? Since(StagedChange change)
    {
        var current = File.Exists(change.Path) ? File.ReadAllText(change.Path) : null;
        return string.Equals(current, change.After, StringComparison.Ordinal) ? null : new ChangedSince(change.Ref, change.After, current);
    }

    private static IReadOnlyList<GraphView> GraphsOf(BrainEnv env, PendingWrite write) =>
        [.. write.Changes.Select(change => Brain.Search.Nearest.ScopeOf(env, change.Path)).OfType<Scope>().Select(GraphOf).Distinct()];

    /// <summary>A graph by its name, with a hue only for a project in projects.tsv, so only a project wears a color.</summary>
    private static GraphView GraphOf(Scope scope) =>
        new(scope.Name, scope.Repo is not null && scope.Name != ScopeNames.Project ? Hue(scope.Name) : null);

    /// <summary>The project of projects.tsv by its name with its hue, or nothing when the name is null.</summary>
    public static IReadOnlyList<GraphView> Graphs(string? project) =>
        project is null ? [] : [new GraphView(project, Hue(project))];

    /// <summary>A project by its name in projects.tsv, with its hue.</summary>
    public static OriginView Project(string project) => new(FromProject, project, Hue(project));

    /// <summary>A project's spot on the color wheel, from its name alone, so it is the same on every machine and no other project moves it.</summary>
    public static int Hue(string project) =>
        (int)(BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(project))) % Wheel);

    private static ReviewChange Change(Graph graph, StagedChange change)
    {
        var state = change switch
        {
            { Before: null } => New,
            { After: null } => Forgotten,
            _ when !StagedText.Shows(change) => EdgesOnly,
            _ => Rewritten,
        };
        var current = File.Exists(change.Path) ? File.ReadAllText(change.Path) : null;
        var node = NodeParser.Parse(Path.GetFileNameWithoutExtension(change.Path), change.After ?? change.Before ?? string.Empty);
        var scope = graph.Scopes.FirstOrDefault(scope => SameDirectory(scope.GraphDir, Path.GetDirectoryName(change.Path)));
        var holder = scope is null ? null : new PlacedNode(scope, node);
        return new ReviewChange(
            change.Ref,
            state,
            change.Before,
            change.After,
            !string.Equals(current, change.Before, StringComparison.Ordinal),
            View(graph, holder, change.Ref, node));
    }

    private static NodeView View(Graph graph, PlacedNode? holder, string reference, Node node) =>
        new(
            reference,
            node.Slug,
            node.Title,
            node.Field(FieldNames.Kind),
            Subjects.Of(node),
            node.Field(FieldNames.NamedBy),
            node.Field(FieldNames.Born),
            node.Body,
            node.Why,
            [.. node.Edges.Select(edge => Edge(graph, holder, edge))]);

    private static EdgeView Edge(Graph graph, PlacedNode? holder, Edge edge) =>
        holder is not null && Ref.Parse(edge.Target).Node is { } reference && reference.Problem() is null && graph.Target(holder, reference) is { } target
            ? new EdgeView(edge.Rel, edge.Target, target.Ref, target.Node.Title)
            : new EdgeView(edge.Rel, edge.Target, null, null);

    private static bool SameDirectory(string left, string? right) =>
        right is not null
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.Ordinal);
}

/// <summary>A write before the user, as the window shows it.</summary>
internal sealed record ReviewItem(string Id, string Digest, DateTime AskedAt, IReadOnlyList<string> Lines, IReadOnlyList<ReviewChange> Changes, IReadOnlyList<GraphView> Graphs);

/// <summary>
/// A write the user answered, as the window lists it: the write in full as it was asked, the verdict, yes, no or refused,
/// the user's own words for a change, why a yes landed nothing, when it was answered and undone, every node that changed
/// since a yes that still stands, and the graphs it lands in.
/// </summary>
internal sealed record AnsweredItem(
    string Id,
    string Digest,
    DateTime AnsweredAt,
    string Verdict,
    string? Reason,
    string? Refusal,
    DateTime? UndoneAt,
    IReadOnlyList<string> Lines,
    IReadOnlyList<ReviewChange> Changes,
    IReadOnlyList<ChangedSince> Since,
    IReadOnlyList<GraphView> Graphs);

/// <summary>A node a yes changed that reads otherwise now: its ref, the text the yes left and the text now, null for no file.</summary>
internal sealed record ChangedSince(string Ref, string? Then, string? Now);

/// <summary>
/// A graph the window can focus on: personal, global, or a project by its name in projects.tsv with its hue on the
/// wheel. A project brain nobody registered goes by project, without a hue.
/// </summary>
internal sealed record GraphView(string Name, int? Hue);

/// <summary>A graph the window can focus on, as <see cref="GraphView"/> names it, with the path of its graph on disk.</summary>
internal sealed record FocusView(string Name, int? Hue, string Path);

/// <summary>
/// A conversation's place as its chip shows it: kind project with the project's name and hue, or kind personal or
/// global with neither.
/// </summary>
internal sealed record OriginView(string Kind, string? Project, int? Hue);

/// <summary>
/// One node file of a write: new, rewritten, forgotten, or edges only; its text before and after; stale when the file
/// changed since the write was staged, so a yes would land nothing; and the node it leaves, or forgets, parsed.
/// </summary>
internal sealed record ReviewChange(string Ref, string State, string? Before, string? After, bool Stale, NodeView Node);

internal sealed record NodeView(
    string Ref,
    string Slug,
    string? Title,
    string? Kind,
    IReadOnlyList<string> Subjects,
    string? NamedBy,
    string? Born,
    string Body,
    string Why,
    IReadOnlyList<EdgeView> Edges);

/// <summary>An edge as written, and the neighbour it reaches when it reaches one.</summary>
internal sealed record EdgeView(string Rel, string Target, string? Ref, string? Title);

/// <summary>
/// A search hit as the window lists it, with its tier, sure or likely. Snippet is the FTS5 snippet, its matched words
/// between \u0002 and \u0003.
/// </summary>
internal sealed record SearchHit(string Ref, string? Title, string? Kind, string? Snippet, string Tier)
{
    public static SearchHit Of(Hit hit) =>
        new(hit.Node.Ref, hit.Node.Node.Title, hit.Node.Node.Field(FieldNames.Kind), hit.Snippet, hit.Tier.Name());
}

/// <summary>A search's hits, and why they are by keywords only, or null when meaning ranked them too.</summary>
internal sealed record SearchResult(IReadOnlyList<SearchHit> Hits, string? KeywordsOnly);

/// <summary>
/// The entries nearest to one node of a write, under the node's ref as the write names it, and why they are by keywords
/// only, or null when meaning ranked them too.
/// </summary>
internal sealed record NearestView(string Ref, IReadOnlyList<NearHit> Hits, string? KeywordsOnly);

/// <summary>An entry near a write: its ref, title, kind and snippet, its scope's name, and its project's hue when it is a registered project's.</summary>
internal sealed record NearHit(string Ref, string? Title, string? Kind, string? Snippet, string Scope, int? Hue);

/// <summary>What the window polls: the version of the writes before the user.</summary>
internal sealed record WindowState(string Version);

/// <summary>What a brain serve says to its key: that it is brain's, and the directory of the build it runs from.</summary>
internal sealed record Hello(string App, string? Build);

internal sealed record WindowReply(string Window);

internal sealed record LaunchRequest(string Code);

internal sealed record LaunchReply(string Token);

internal sealed record AnswerRequest(string Id, string Digest, string Answer, string? Reason);

/// <summary>An undo the window sends: the answered write's id and the digest of its record as the window showed it.</summary>
internal sealed record UndoRequest(string Id, string Digest);

/// <summary>What an undo did: the verdict it undid, yes or no, the commits an undone yes made, and why it did not commit.</summary>
internal sealed record UndoReply(string Id, string Undid, IReadOnlyList<string> Commits, string? NotCommitted);

/// <summary>One change the settings page saves: a key of <see cref="BrainSettings"/> and its new value.</summary>
internal sealed record SettingChange(string? Key, JsonNode? Value);

/// <summary>A refusal. <c>Alarm</c> says a spent launch code was tried, so the window stays open to show it.</summary>
internal sealed record Problem(string Error, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Alarm = false);

/// <summary>The JSON of brain's window API, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<ReviewItem>))]
[JsonSerializable(typeof(IReadOnlyList<AnsweredItem>))]
[JsonSerializable(typeof(UndoRequest))]
[JsonSerializable(typeof(UndoReply))]
[JsonSerializable(typeof(NodeView))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(IReadOnlyList<NearestView>))]
[JsonSerializable(typeof(WindowState))]
[JsonSerializable(typeof(Hello))]
[JsonSerializable(typeof(WindowReply))]
[JsonSerializable(typeof(LaunchRequest))]
[JsonSerializable(typeof(LaunchReply))]
[JsonSerializable(typeof(AnswerRequest))]
[JsonSerializable(typeof(PendingAnswer))]
[JsonSerializable(typeof(Problem))]
[JsonSerializable(typeof(IReadOnlyList<SourceView>))]
[JsonSerializable(typeof(IReadOnlyList<FocusView>))]
[JsonSerializable(typeof(ContextView))]
[JsonSerializable(typeof(IReadOnlyList<ProblemView>))]
[JsonSerializable(typeof(CodeView))]
[JsonSerializable(typeof(QuestionAnswerRequest))]
[JsonSerializable(typeof(SourceAnswer))]
[JsonSerializable(typeof(ConversationsView))]
[JsonSerializable(typeof(ConversationView))]
[JsonSerializable(typeof(StartRequest))]
[JsonSerializable(typeof(SittingRequest))]
[JsonSerializable(typeof(RenameRequest))]
[JsonSerializable(typeof(RefRequest))]
[JsonSerializable(typeof(DreamChangesView))]
[JsonSerializable(typeof(IReadOnlyList<DreamRow>))]
[JsonSerializable(typeof(DreamView))]
[JsonSerializable(typeof(SayRequest))]
[JsonSerializable(typeof(SkyView))]
[JsonSerializable(typeof(ChatView))]
[JsonSerializable(typeof(ChatStartRequest))]
[JsonSerializable(typeof(ChatSayRequest))]
[JsonSerializable(typeof(ChatCloseRequest))]
[JsonSerializable(typeof(AgentsView))]
[JsonSerializable(typeof(ChatRunningView))]
[JsonSerializable(typeof(SettingChange))]
[JsonSerializable(typeof(EmbedderView))]
[JsonSerializable(typeof(EmbedderCost))]
[JsonSerializable(typeof(SwitchRequest))]
[JsonSerializable(typeof(PerfBatch))]
[JsonSerializable(typeof(PerfReply))]
[JsonSerializable(typeof(GpuView))]
internal sealed partial class ReviewJson : JsonSerializerContext;
