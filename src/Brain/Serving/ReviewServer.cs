using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Brain.Children;
using Brain.Dreaming;
using Brain.Staging;
using Brain.Stats;
using static Brain.Serving.WebCalls;

namespace Brain.Serving;

/// <summary>
/// <c>brain serve</c>: brain's window on 127.0.0.1 only. It reads the graph and the writes before the user through the
/// CLI's own code, and answers a write only through <see cref="PendingStore.Answer"/> and undoes an answer only through
/// <see cref="PendingStore.Undo"/>, so the window can never write past
/// the CLI's checks. Every request must name this server's host exactly, which shuts out DNS rebinding. A POST from a
/// browser must come from this origin. Hello, window and restart need the key in <see cref="ServeFiles"/>, which only the
/// user's account can read; launch needs a launch code, which only a launch file of the user's holds; everything else
/// under <c>/api</c> needs the session token of a window, which <see cref="WindowKeys"/> hands out once per launch code.
/// So another account on the machine reaches nothing but the page's static files. <c>/theme.css</c>, the active Omarchy
/// theme from <see cref="OmarchyTheme"/>, is one of them; the window's event stream, which says when the theme switched
/// and when the window must close, needs a session token. A conversation's calls drive the open agent of a sitting, so
/// like an answer they need a session that is armed, and so do a chat's and a settings change, which commits
/// <see cref="BrainSettings"/> in the data repo. Before it stops, brain serve tells every window to close, or on a
/// restart hands the window that listens on to its successor with a <see cref="HandedLaunch"/>, and it stops
/// every agent it runs, a sitting's, a chat's or a dream's, with everything each started. Every one starts through
/// <see cref="ClaudeRuns"/>, so what an agent started stops with brain serve however brain serve stops.
/// <see cref="AgentRegistry"/> lists them for the window. Search runs on the embedder <see cref="ModelSwitch"/> holds
/// now, and a switch of models starts only from an armed session, like a settings change. The window's perf rows go to
/// the <see cref="PerfLog"/>, each with the GPU's state <see cref="GpuWatch"/> read for its second.
/// </summary>
internal sealed class ReviewServer(
    BrainEnv env, int port, Func<string, bool> openWindow, TimeProvider time, OmarchyTheme theme, Action successor, ModelSwitch models, GpuWatch gpu)
{
    public const string SessionHeader = "X-Brain-Session";
    public const string App = "brain";
    private const string Get = "GET";
    private const string Post = "POST";
    private const string ConversationCalls = "/api/conversation/";
    private const string ChatCalls = "/api/chat/";
    private const string EmbedderCall = "/api/embedder";
    private const string PerfCall = "/api/perf";

    /// <summary>425 Too Early, which HttpStatusCode does not name: the window's session is not armed yet.</summary>
    private const HttpStatusCode TooEarly = (HttpStatusCode)425;

    /// <summary>The event stream says it is alive this often, so the stream of a closed window or session ends.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    /// <summary>How long a stopping brain serve waits for its event streams to carry close to their windows.</summary>
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, (string Resource, string Type)> Assets = WithModules(WebAssets.With(
        ("/", "web/index.html", WebAssets.Page),
        ("/app.js", "web/app.js", WebAssets.Script),
        ("/questions.js", "web/questions.js", WebAssets.Script),
        ("/highlight.js", "web/highlight.js", WebAssets.Script),
        ("/prism.js", "shared/vendor/prism.js", WebAssets.Script),
        ("/menu.js", "web/menu.js", WebAssets.Script),
        ("/conversations.js", "web/conversations.js", WebAssets.Script),
        ("/chat.js", "web/chat.js", WebAssets.Script),
        ("/dreams.js", "web/dreams.js", WebAssets.Script),
        ("/evidence.js", "web/evidence.js", WebAssets.Script),
        ("/home.js", "web/home.js", WebAssets.Script),
        ("/perf.js", "web/perf.js", WebAssets.Script),
        ("/seam.js", "web/seam.js", WebAssets.Script),
        ("/state.js", "web/state.js", WebAssets.Script),
        ("/stylebook.js", "web/stylebook.js", WebAssets.Script),
        ("/app.css", "web/app.css", WebAssets.Sheet),
        ("/conversations.css", "web/conversations.css", WebAssets.Sheet),
        ("/home.css", "web/home.css", WebAssets.Sheet),
        ("/backdrop.png", "web/backdrop.png", "image/png"),
        ("/settings.css", "web/settings.css", WebAssets.Sheet),
        ("/questions.css", "web/questions.css", WebAssets.Sheet),
        ("/stylebook.css", "web/stylebook.css", WebAssets.Sheet)));

    private readonly Lock answering = new();
    private readonly ServeFiles files = new(env.Home);
    private readonly Inboxes inboxes = new(env, time);
    private readonly NavaroneTab navarone = new(env);
    private readonly ConversationRoom conversations = new(env, time);
    private readonly ChatRoom chats = new(env, time);
    private readonly PerfLog perf = new(env.Home, time);
    private string key = string.Empty;
    private CancellationTokenSource? serving;
    private bool stopped;
    private bool restarting;
    private int streams;

    public WindowKeys Keys { get; } = new(time);

    public string Origin { get; } = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>The environment a search runs in: one embedder from start to end, the one search runs on now.</summary>
    private BrainEnv Searching => env with { Embedder = models.Embedder };

    /// <summary>
    /// Listens until <paramref name="stop"/> or a restart call; opens a window first when <paramref name="window"/> holds,
    /// unless its predecessor handed it a launch code, which the window already open trades as it moves here. After a
    /// restart call it starts its successor once its port is free and no answer is in flight.
    /// </summary>
    // navarone: 0000139
    // navarone: 0000216
    public async Task Serve(bool window, CancellationToken stop)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(Origin + "/");

        // A handed code is admitted before the port answers, so the moving window that finds the page can trade it.
        var handed = HandedLaunch.Take(files, time);
        if (handed is not null)
        {
            Keys.Admit(handed);
        }

        try
        {
            listener.Start();
        }
        catch (HttpListenerException error)
        {
            throw new BrainException($"127.0.0.1:{port} is taken ({error.Message}): another brain serve runs there, or set BRAIN_PORT to a free port");
        }

        key = files.WriteKey();
        perf.Prune();
        using var watching = theme.Watch(time);
        await env.Err.WriteLineAsync($"brain serve: listening on {Origin}").ConfigureAwait(false);
        if (window && handed is null)
        {
            OpenWindow();
        }

        // Every window is told to close while the port still answers. The launch files go before the port frees, so a
        // successor started once this one stopped answering keeps its own.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop);
        serving = stopping;
        using var closing = stopping.Token.Register(() =>
        {
            CloseWindows();
            files.RemoveLaunches();
            listener.Close();
        });
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => Handle(context), CancellationToken.None);
        }

        // An answer in flight lands whole before the process ends, and one that comes later is refused: it waits for the
        // next window.
        lock (answering)
        {
            stopped = true;
        }

        // Every sitting's agent, and everything it started, stops with brain serve, before a successor starts its own.
        conversations.StopAll();
        chats.StopAll();
        ClaudeRuns.StopAll();
        if (restarting)
        {
            // The listening socket shuts a moment after Close returns, once the accept pending on it lets go.
            SpinWait.SpinUntil(() => !Answers(), Drain);
            successor();
        }
    }

    /// <summary>
    /// The restart: while a window listens, it hands the successor a launch code and tells the window, on its own event
    /// stream, the launch URL it goes on at once the successor serves; then the server stops.
    /// </summary>
    private void Restart()
    {
        restarting = true;
        if (Volatile.Read(ref streams) > 0)
        {
            Keys.Move(Origin + "/#launch=" + HandedLaunch.Hand(files, time));
            theme.Wake();
        }

        serving?.Cancel();
    }

    /// <summary>True while something accepts a connection on this server's port.</summary>
    private bool Answers()
    {
        try
        {
            using var probe = new TcpClient();
            probe.Connect(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a window with a fresh launch code, through a launch file, so the browser's command line names the file and
    /// never the code. False when no browser could be started.
    /// </summary>
    public bool OpenWindow() => openWindow(new Uri(files.WriteLaunch(Origin + "/#launch=" + Keys.Issue())).AbsoluteUri);

    /// <summary>Tells every window to close over its event stream, and waits a moment for the streams to say it.</summary>
    private void CloseWindows()
    {
        Keys.CloseAll();
        theme.Wake();
        SpinWait.SpinUntil(() => Volatile.Read(ref streams) == 0, Drain);
    }

    /// <summary>Answers one request in the boundary <see cref="WebCalls.Answer"/> gives every server.</summary>
    public void Handle(HttpListenerContext context) => WebCalls.Answer(context, env.Home, "brain serve", env.Err, Route);

    // navarone: 0000026
    // navarone: 0000049
    private void Route(HttpListenerRequest request, HttpListenerResponse response)
    {
        var path = request.Url?.AbsolutePath ?? string.Empty;
        if (RefusedStranger(request, response, Origin))
        {
            return;
        }

        // A server that restarts answers nothing new: the window that moves knows the page it finds is the successor's.
        if (restarting)
        {
            Refuse(response, HttpStatusCode.ServiceUnavailable, "brain serve is restarting on a new build");
            return;
        }

        var origin = request.Headers["Origin"];

        switch (request.HttpMethod, path)
        {
            case (Get, "/api/hello") or (Post, "/api/window") or (Post, "/api/restart") when !HasKey(request, key):
                Refuse(response, HttpStatusCode.Forbidden, "this call needs the key brain serve keeps in the data repo's cache/serve");
                return;
            case (Get, _) when Assets.TryGetValue(path, out var asset):
                WebAssets.Send(response, asset.Resource, asset.Type);
                return;
            case (Get, "/theme.css"):
                SendText(response, theme.Css, WebAssets.Sheet);
                return;
            case (Get, "/api/hello"):
                Send(response, new Hello(App, AppContext.BaseDirectory), ReviewJson.Default.Hello);
                return;
            case (Post, "/api/window"):
                Send(response, new WindowReply(OpenWindow() ? "opened" : "no-browser"), ReviewJson.Default.WindowReply);
                return;
            case (Post, "/api/restart"):
                response.StatusCode = (int)HttpStatusCode.Accepted;
                response.Close();
                Restart();
                return;
            case (Post, "/api/launch") when origin is not null:
                Launch(request, response);
                return;
        }

        var token = request.Headers[SessionHeader];
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            Refuse(response, HttpStatusCode.NotFound, "no such page");
        }
        else if (!Keys.Knows(token))
        {
            RefuseWindow(response, HttpStatusCode.Unauthorized, "this window's session ended; open brain again");
        }
        else
        {
            Api(request, response, token!, path);
        }
    }

    // navarone: 0000027
    // navarone: 0000050
    private void Api(HttpListenerRequest request, HttpListenerResponse response, string token, string path)
    {
        switch (request.HttpMethod, path)
        {
            case (Get, "/api/state"):
                Send(response, new WindowState(ReviewViews.Version(env)), ReviewJson.Default.WindowState);
                break;
            case (Get, "/api/pending"):
                Send(response, ReviewViews.Pending(env), ReviewJson.Default.IReadOnlyListReviewItem);
                break;
            case (Get, "/api/search"):
                Send(response, ReviewViews.Search(Searching, Query(request, "q")), ReviewJson.Default.SearchResult);
                break;
            case (Get, "/api/sky"):
                Send(response, Sky.Load(env).View(), ReviewJson.Default.SkyView);
                break;
            case (Get, "/api/sky/search"):
                Send(response, Sky.Load(Searching).Search(Query(request, "q")), ReviewJson.Default.SearchResult);
                break;
            case (Get, "/api/node"):
                Node(response, Query(request, "ref"));
                break;
            case (Get, "/api/nearest"):
                Nearest(response, Query(request, "id"));
                break;
            case (Get, "/api/events"):
                WindowEvents(response, token);
                break;
            case (Get, "/api/projects"):
                Send(response, ReviewViews.Focuses(env), ReviewJson.Default.IReadOnlyListFocusView);
                break;
            case (Get, "/api/questions"):
                Send(response, inboxes.List(), ReviewJson.Default.IReadOnlyListSourceView);
                break;
            case (Get, "/api/navarone/problems"):
                Send(response, navarone.List(), ReviewJson.Default.IReadOnlyListProblemView);
                break;
            case (Get, "/api/navarone/code"):
                Reading(response, () => navarone.Code(Query(request, "hash")), ReviewJson.Default.CodeView);
                break;
            case (Get, "/api/navarone/lens"):
                Reading(response, () => navarone.Lens(Query(request, "worktree"), Query(request, "id"), Query(request, "q")), ReviewJson.Default.ContextView);
                break;
            case (Post, "/api/answer") when request.Headers["Origin"] is not null:
                Answer(request, response, token);
                break;
            case (Get, "/api/answered"):
                Send(response, ReviewViews.Answered(env), ReviewJson.Default.IReadOnlyListAnsweredItem);
                break;
            case (Post, "/api/undo") when request.Headers["Origin"] is not null:
                Undo(request, response, token);
                break;
            case (Post, "/api/answer/question") when request.Headers["Origin"] is not null:
                AnswerQuestion(request, response, token);
                break;
            case (Get, "/api/conversations"):
                Send(response, conversations.List(), ReviewJson.Default.ConversationsView);
                break;
            case (Get, "/api/conversation"):
                Converse(response, HttpStatusCode.NotFound, () => conversations.Open(Query(request, "ref"), Seen(request.QueryString["since"])));
                break;
            case (Get, "/api/conversation/changes"):
                Reading(response, () => conversations.Changes(Query(request, "ref")), ReviewJson.Default.DreamChangesView);
                break;
            case (Get, "/api/dreams"):
                Send(response, [.. DreamLog.All(env).Select(DreamRow.Of)], ReviewJson.Default.IReadOnlyListDreamRow);
                break;
            case (Get, "/api/evidence"):
                Evidence(request, response);
                break;
            case (Get, "/api/dream"):
                Reading(response, () => DreamView.Of(DreamLog.Find(env, Query(request, "id"))), ReviewJson.Default.DreamView);
                break;
            case (Get, "/api/agents"):
                Send(response, AgentRegistry.Of(chats, conversations), ReviewJson.Default.AgentsView);
                break;
            case (Get, "/api/settings"):
                Settle(response, () => BrainSettings.Read(env.Home));
                break;
            case (Post, "/api/settings") when request.Headers["Origin"] is not null && !Keys.Armed(token):
                Refuse(response, TooEarly, "the window is still opening; send again in a moment");
                break;
            case (Post, "/api/settings") when request.Headers["Origin"] is not null:
                ChangeSetting(request, response);
                break;
            case (Get, EmbedderCall):
                Send(response, models.View(), ReviewJson.Default.EmbedderView);
                break;
            case (Get, EmbedderCall + "/cost"):
                Reading(response, () => models.Cost(Query(request, "model")), ReviewJson.Default.EmbedderCost);
                break;
            case (Post, EmbedderCall) when request.Headers["Origin"] is not null && !Keys.Armed(token):
                Refuse(response, TooEarly, "the window is still opening; send again in a moment");
                break;
            case (Post, EmbedderCall) when request.Headers["Origin"] is not null:
                Switch(request, response);
                break;
            case (Post, PerfCall) when request.Headers["Origin"] is not null:
                Perf(request, response);
                break;
            case (Get, PerfCall + "/gpu"):
                Send(response, GpuView.Of(gpu.Now()), ReviewJson.Default.GpuView);
                break;
            case (Get, "/api/chat"):
                Chat(response, HttpStatusCode.NotFound, () => chats.Open(Query(request, "id"), Seen(request.QueryString["since"])));
                break;
            case (Post, _) when request.Headers["Origin"] is not null && (path.StartsWith(ConversationCalls, StringComparison.Ordinal) || path.StartsWith(ChatCalls, StringComparison.Ordinal)) && !Keys.Armed(token):
                Refuse(response, TooEarly, "the window is still opening; send again in a moment");
                break;
            case (Post, _) when request.Headers["Origin"] is not null && path.StartsWith(ConversationCalls, StringComparison.Ordinal):
                Converse(response, HttpStatusCode.Conflict, () => Change(request, path[ConversationCalls.Length..]));
                break;
            case (Post, ChatCalls + "close") when request.Headers["Origin"] is not null:
                CloseChat(request, response);
                break;
            case (Post, _) when request.Headers["Origin"] is not null && path.StartsWith(ChatCalls, StringComparison.Ordinal):
                Chat(response, HttpStatusCode.Conflict, () => ChatChange(request, path[ChatCalls.Length..]));
                break;
            default:
                Refuse(response, HttpStatusCode.NotFound, "no such call");
                break;
        }
    }

    /// <summary>
    /// The window's event stream. It says <c>theme</c> when it opens and on every Omarchy theme switch, so the window swaps
    /// <c>/theme.css</c>; without Omarchy it says only that it is alive. Once the window's session was closed it says
    /// <c>close</c>, and the window closes itself; once it was handed on to the successor it says <c>moved</c> with the
    /// successor's launch URL, and the window goes there. It ends with the window's session or its connection.
    /// </summary>
    private void WindowEvents(HttpListenerResponse response, string token)
    {
        const string Switched = "event: theme\ndata: switched\n\n";
        const string Alive = ": alive\n\n";
        Interlocked.Increment(ref streams);
        try
        {
            response.ContentType = "text/event-stream; charset=utf-8";
            response.SendChunked = true;
            var seen = theme.Version;
            var said = theme.Present ? Switched : Alive;
            while (Keys.Knows(token))
            {
                Say(response, said);
                said = theme.WaitPast(seen, Heartbeat) ? Switched : Alive;
                seen = theme.Version;
            }

            if (Keys.MovedTo(token) is { } url)
            {
                Say(response, "event: moved\ndata: " + url + "\n\n");
            }
            else if (Keys.Closed(token))
            {
                Say(response, "event: close\ndata: close\n\n");
            }
        }
        finally
        {
            Interlocked.Decrement(ref streams);
        }
    }

    private void Launch(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, ReviewJson.Default.LaunchRequest) is { Code: { } code } && Keys.Exchange(code) is { } token)
        {
            files.RemoveLaunches();
            theme.Wake();
            // The windows before this one are closed; their chats stay, for this window to show.
            Send(response, new LaunchReply(token), ReviewJson.Default.LaunchReply);
            return;
        }

        RefuseWindow(response, HttpStatusCode.Forbidden, "this window's key is no longer valid; open brain again");
    }

    /// <summary>
    /// Refuses a window its launch or its session. While <see cref="WindowKeys.Alarmed"/> holds, the refusal says so and
    /// carries the alarm, so the window stays open to show it; otherwise the window closes itself.
    /// </summary>
    private void RefuseWindow(HttpListenerResponse response, HttpStatusCode status, string error)
    {
        response.StatusCode = (int)status;
        Send(response, Keys.Alarmed
            ? new Problem("another process used this window's key, so brain closed every window; open brain again", Alarm: true)
            : new Problem(error), ReviewJson.Default.Problem);
    }

    /// <summary>A batch of the window's perf rows to the perf log, each with the GPU's state of its second.</summary>
    private void Perf(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, ReviewJson.Default.PerfBatch) is not { Rows: { } rows })
        {
            Refuse(response, HttpStatusCode.BadRequest, "a batch of perf rows names its rows");
            return;
        }

        perf.Append(rows, gpu.At);
        Send(response, new PerfReply(rows.Count), ReviewJson.Default.PerfReply);
    }

    /// <summary>
    /// The evidence: every metric's section over the days the window names in <c>since</c> and <c>until</c>, all time
    /// when it names none, from the same list <c>brain stats</c> reads. A day that does not read is refused.
    /// </summary>
    private void Evidence(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!TryDay(request.QueryString["since"], out var since) || !TryDay(request.QueryString["until"], out var until))
        {
            Refuse(response, HttpStatusCode.BadRequest, $"since and until take a day as {FieldNames.DateFormat}");
            return;
        }

        Send(response, Metrics.Measure(env.Home, since, until), StatsJson.Default.StatsReport);
    }

    /// <summary>A field of the request's query, empty when it names none.</summary>
    private static string Query(HttpListenerRequest request, string name) => request.QueryString[name] ?? string.Empty;

    /// <summary>A day the window names: true with null when it names none, false when the one it names does not read.</summary>
    private static bool TryDay(string? text, out DateOnly? day)
    {
        day = string.IsNullOrEmpty(text) ? null : Figures.Day(text);
        return string.IsNullOrEmpty(text) || day is not null;
    }

    private void Node(HttpListenerResponse response, string reference) =>
        Reading(response, () => ReviewViews.Node(env, reference), ReviewJson.Default.NodeView);

    private void Nearest(HttpListenerResponse response, string id) =>
        Reading(response, () => ReviewViews.Nearest(Searching, id), ReviewJson.Default.IReadOnlyListNearestView);

    private void Answer(HttpListenerRequest request, HttpListenerResponse response, string token)
    {
        if (!Keys.Armed(token))
        {
            Refuse(response, TooEarly, "the window is still opening; answer again in a moment");
            return;
        }

        if (Read(request, ReviewJson.Default.AnswerRequest) is not { Id: { } id, Digest: { } digest, Answer: "yes" or "no" } answer)
        {
            Refuse(response, HttpStatusCode.BadRequest, "an answer names an id, a digest, and yes or no");
            return;
        }

        try
        {
            lock (answering)
            {
                if (stopped)
                {
                    Refuse(response, HttpStatusCode.ServiceUnavailable, "brain serve is stopping; answer again in the window it opens");
                    return;
                }

                var store = new PendingStore(env.Home);
                var given = store.Answer(id, digest, answer.Answer == "yes", answer.Reason);
                using var notice = WaitingNotice.FromEnvironment(store);
                notice.Settle();
                env.Err.WriteLine($"brain serve: {given.Verdict} to {id}");
                Send(response, given, ReviewJson.Default.PendingAnswer);
            }
        }
        catch (BrainException refusal)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    /// <summary>
    /// Undoes the user's answer to a write, from the window only, as an answer is: the click is the user's yes, and the
    /// undo goes through the pending store's own code under the lock answers take. An undone yes commits itself there,
    /// and the reply says what it committed or why it could not.
    /// </summary>
    private void Undo(HttpListenerRequest request, HttpListenerResponse response, string token)
    {
        if (!Keys.Armed(token))
        {
            Refuse(response, TooEarly, "the window is still opening; undo again in a moment");
            return;
        }

        if (Read(request, ReviewJson.Default.UndoRequest) is not { Id: { } id, Digest: { } digest })
        {
            Refuse(response, HttpStatusCode.BadRequest, "an undo names an id and a digest");
            return;
        }

        try
        {
            lock (answering)
            {
                if (stopped)
                {
                    Refuse(response, HttpStatusCode.ServiceUnavailable, "brain serve is stopping; undo again in the window it opens");
                    return;
                }

                var undone = new PendingStore(env.Home).Undo(id, digest, Graph.Load(env));
                env.Err.WriteLine($"brain serve: undid the {undone.Undid} to {id}");
                if (undone.NotCommitted is { } failure)
                {
                    env.Err.WriteLine($"brain serve: the undo of {id} is not committed: {failure}");
                }

                Send(response, new UndoReply(id, undone.Undid, undone.Commits, undone.NotCommitted), ReviewJson.Default.UndoReply);
            }
        }
        catch (BrainException refusal)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    /// <summary>The user's choice on a source's question, from the window only, as a write's answer is.</summary>
    private void AnswerQuestion(HttpListenerRequest request, HttpListenerResponse response, string token)
    {
        if (!Keys.Armed(token))
        {
            Refuse(response, TooEarly, "the window is still opening; answer again in a moment");
            return;
        }

        if (Read(request, ReviewJson.Default.QuestionAnswerRequest) is not { Source: { } source, Id: { } id, Choice: { } choice } answer)
        {
            Refuse(response, HttpStatusCode.BadRequest, "an answer to a question names its source, its id and a choice");
            return;
        }

        try
        {
            var given = inboxes.Answer(source, id, choice, answer.Words);
            env.Err.WriteLine($"brain serve: {choice} to {source} question {id}");
            Send(response, given, ReviewJson.Default.SourceAnswer);
        }
        catch (BrainException refusal)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    /// <summary>A change to a conversation from the window, through the code <c>brain conversation</c> runs.</summary>
    private ConversationView Change(HttpListenerRequest request, string call) =>
        call switch
        {
            "start" => Read(request, ReviewJson.Default.StartRequest) is { Scope: not null, Model: not null, Effort: not null } start
                ? conversations.Start(start)
                : throw new BrainException("a start names a scope, a model and an effort"),
            "sitting" => Read(request, ReviewJson.Default.SittingRequest) is { Ref: not null, Model: not null, Effort: not null } sitting
                ? conversations.Sit(sitting)
                : throw new BrainException("a new sitting names a conversation, a model and an effort"),
            "rename" => Read(request, ReviewJson.Default.RenameRequest) is { Ref: not null, Title: not null } rename
                ? conversations.Rename(rename)
                : throw new BrainException("a rename names a conversation and a title"),
            "commit" => Read(request, ReviewJson.Default.RefRequest) is { Ref: not null } commit
                ? conversations.Commit(commit)
                : throw new BrainException("a commit names a conversation"),
            "dream" => Read(request, ReviewJson.Default.RefRequest) is { Ref: not null } dream
                ? conversations.Dream(dream)
                : throw new BrainException("a dream names a conversation"),
            "deep-dream" => Read(request, ReviewJson.Default.RefRequest) is { Ref: not null } deep
                ? conversations.DeepDream(deep)
                : throw new BrainException("a deep dream names a conversation"),
            "say" => Read(request, ReviewJson.Default.SayRequest) is { Ref: not null } say
                ? conversations.Say(say)
                : throw new BrainException("a phase names a conversation"),
            _ => throw new BrainException("no such call on a conversation"),
        };

    /// <summary>A chat summoned or talked to from the window.</summary>
    private ChatView ChatChange(HttpListenerRequest request, string call) =>
        call switch
        {
            "start" when request.ContentLength64 > ChatRoom.MaxStartBody => throw new BrainException($"this chat's context is over {ChatRoom.MaxStartBody / 1024} KiB; open less of the page, or ask in a chat with no context"),
            "start" => chats.Start(Read(request, ReviewJson.Default.ChatStartRequest, ChatRoom.MaxStartBody) ?? throw new BrainException("a chat starts with a model and an effort, or neither")),
            "say" => chats.Say(Read(request, ReviewJson.Default.ChatSayRequest) ?? throw new BrainException("a chat's words name the chat")),
            _ => throw new BrainException("no such call on a chat"),
        };

    /// <summary>
    /// The window closes a chat: the agent stops and nothing of the chat is left. While what the chat started still runs,
    /// the close is refused with what would stop, until the window sends it again with stop, on the user's word.
    /// </summary>
    private void CloseChat(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, ReviewJson.Default.ChatCloseRequest) is not { Id: { } id } close)
        {
            Refuse(response, HttpStatusCode.BadRequest, "a close names the chat");
            return;
        }

        if (chats.Close(id, close.Stop) is { Count: > 0 } running)
        {
            response.StatusCode = (int)HttpStatusCode.Conflict;
            Send(response, new ChatRunningView("closing this chat stops what it started", running), ReviewJson.Default.ChatRunningView);
            return;
        }

        response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    /// <summary>Saves one change of the settings page, one at a time, and answers with every setting as it now is.</summary>
    private void ChangeSetting(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, ReviewJson.Default.SettingChange) is not { Key: { } changed } change)
        {
            Refuse(response, HttpStatusCode.BadRequest, "a setting's change names its key and its value");
            return;
        }

        if (changed == BrainSettings.EmbedderKey)
        {
            Refuse(response, HttpStatusCode.Conflict, "the embedding model changes only through its switch, which embeds every node with it first");
            return;
        }

        Settle(response, () => BrainSettings.Change(env.Home, changed, change.Value));
    }

    /// <summary>Starts the switch to the model the window names, and answers with the switch running.</summary>
    private void Switch(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, ReviewJson.Default.SwitchRequest) is not { Model: { } model })
        {
            Refuse(response, HttpStatusCode.BadRequest, "a switch names its model");
            return;
        }

        try
        {
            Send(response, models.Start(model), ReviewJson.Default.EmbedderView);
        }
        catch (BrainException refusal)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    private static void Settle(HttpListenerResponse response, Func<BrainSettings> settings)
    {
        try
        {
            SendText(response, settings().ToJson(), JsonType);
        }
        catch (BrainException refusal)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    private static void Chat(HttpListenerResponse response, HttpStatusCode refused, Func<ChatView> view)
    {
        try
        {
            Send(response, view(), ReviewJson.Default.ChatView);
        }
        catch (BrainException refusal)
        {
            Refuse(response, refused, refusal.Message);
        }
    }

    /// <summary>The version of a sitting a window last drew, which a long poll waits past; null when it names none.</summary>
    private static long? Seen(string? since) =>
        long.TryParse(since, NumberStyles.None, CultureInfo.InvariantCulture, out var seen) ? seen : null;

    private static void Converse(HttpListenerResponse response, HttpStatusCode refused, Func<ConversationView> view)
    {
        try
        {
            Send(response, view(), ReviewJson.Default.ConversationView);
        }
        catch (BrainException refusal)
        {
            Refuse(response, refused, refusal.Message);
        }
    }

    /// <summary>
    /// Adds every script and sheet built in from web/components and web/pages, each at its path under the window, as
    /// /components/select.js, so a component or a page that moves to a module is served without a line of its own.
    /// </summary>
    private static Dictionary<string, (string Resource, string Type)> WithModules(Dictionary<string, (string Resource, string Type)> assets)
    {
        foreach (var resource in typeof(ReviewServer).Assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith("web/components/", StringComparison.Ordinal) && !resource.StartsWith("web/pages/", StringComparison.Ordinal))
            {
                continue;
            }

            var path = "/" + resource["web/".Length..];
            if (resource.EndsWith(".js", StringComparison.Ordinal))
            {
                assets[path] = (resource, WebAssets.Script);
            }
            else if (resource.EndsWith(".css", StringComparison.Ordinal))
            {
                assets[path] = (resource, WebAssets.Sheet);
            }
        }

        return assets;
    }
}
