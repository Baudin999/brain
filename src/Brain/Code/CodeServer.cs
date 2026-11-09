using System.Globalization;
using System.Net;
using System.Text.Json.Serialization.Metadata;
using Brain.Serving;
using Brain.Windows;
using Brain.Workspace;
using static Brain.Serving.WebCalls;
using static Brain.Windows.WindowServer;

namespace Brain.Code;

/// <summary>
/// brain code's own server: a <see cref="WindowServer"/>, one on the machine, apart from brain serve's and every
/// workspace's, that runs the window's sessions in its <see cref="CodeSessions"/> and ends, with every session and what
/// runs under each, once its window is gone. <c>brain code</c> starts a session through the keyed
/// <c>/api/key/start</c>; the window starts one through <c>/api/start</c>, says the user's words, interrupts a turn,
/// ends a process a session started, reads a session's conversation, the agents it sent and one agent's view with the
/// trail of its work and its conversation, answers a permission request and a session's questions, and closes a
/// session. The user's words carry the images he attached. Its file explorer lists a folder's folders and images
/// through <see cref="ImageFolders"/>, reads an image file, and says which folder images were attached from; the server
/// reads nothing else of the disk for it. Each session's folder has its workspace, which <see cref="CodeWorkspaces"/>
/// starts and the window shows in a frame; brain code reads nothing of it. A session's permission hook,
/// <c>brain code --permission</c>, hands each request in through the keyed <c>/api/key/permission</c>, which answers
/// once the user did, or deny once the session ended or a week went by. A session's <c>brain ask</c> hands its
/// questions in through the keyed <c>/api/key/ask</c>, which answers at once for an agent that goes on, and once the
/// user answered, or the session ended, for one that waits. The window hears on its event stream which session changed,
/// at most once each 50 ms, and which one a <c>brain code</c> started. Every call, each request with its answer, and
/// each question with its answer, is a record in the <see cref="WindowLog"/> of <c>perf/code</c>.
/// <para>
/// Once a newer build is published beside it, its window hears so and its session list says whether a reload waits for
/// a session that runs. The user's reload, refused while one runs, hands every session on in <see cref="Handover"/>, and
/// the server stops them and starts its successor itself, from its own environment, so no session owns the new server.
/// The window moves onto the successor in place, through <see cref="WindowServer.MoveTo"/>, and no second window opens.
/// A server that starts takes the handover a reload left: it resumes each session by its id and its window shows the
/// one that was in view.
/// </para>
/// </summary>
internal sealed class CodeServer : IDisposable
{
    public const string App = "brain-code";
    public const string Command = "brain code";
    public const string Folder = "code";
    private const string Title = "brain code";

    /// <summary>A conversation read past this many milliseconds is over its budget.</summary>
    private const double ReadBudgetMs = 100;

    /// <summary>The most of a question's first line its records keep.</summary>
    private const int HeadlineLength = 80;

    /// <summary>Changes of one session within this long reach the window as one.</summary>
    private static readonly TimeSpan Gather = TimeSpan.FromMilliseconds(50);

    /// <summary>The largest permission request read: a Write of a large file, whose input carries all of it.</summary>
    private const int AskBody = 4 * 1024 * 1024;

    /// <summary>The largest words read: room for a few images at their largest, in base64.</summary>
    private const int SayBody = 48 * 1024 * 1024;

    /// <summary>A folder listed past this many milliseconds is over its budget, as a workspace's tree is.</summary>
    private const double FolderBudgetMs = 200;

    private static readonly Dictionary<string, (string Resource, string Type)> Assets = WebAssets.With(
        ("/", "code/index.html", WebAssets.Page),
        ("/code.css", "code/code.css", WebAssets.Sheet),
        ("/app.js", "code/app.js", WebAssets.Script),
        ("/session-state.js", "code/session-state.js", WebAssets.Script),
        ("/attach.js", "code/attach.js", WebAssets.Script),
        ("/pages/session.js", "code/pages/session.js", WebAssets.Script),
        ("/pages/agent.js", "code/pages/agent.js", WebAssets.Script),
        ("/components/session-list.js", "code/components/session-list.js", WebAssets.Script),
        ("/components/new-session.js", "code/components/new-session.js", WebAssets.Script),
        ("/components/conversation.js", "code/components/conversation.js", WebAssets.Script),
        ("/components/composer.js", "code/components/composer.js", WebAssets.Script),
        ("/components/quote.js", "code/components/quote.js", WebAssets.Script),
        ("/components/asks.js", "code/components/asks.js", WebAssets.Script),
        ("/pages/questions.js", "code/pages/questions.js", WebAssets.Script),
        ("/components/session-question.js", "code/components/session-question.js", WebAssets.Script),
        ("/components/open-questions.js", "code/components/open-questions.js", WebAssets.Script),
        ("/components/questions-row.js", "code/components/questions-row.js", WebAssets.Script),
        ("/components/agent-list.js", "code/components/agent-list.js", WebAssets.Script),
        ("/components/trail.js", "code/components/trail.js", WebAssets.Script),
        ("/components/signals.js", "code/components/signals.js", WebAssets.Script),
        ("/pages/settings.js", "code/pages/settings.js", WebAssets.Script),
        ("/components/settings-cog.js", "code/components/settings-cog.js", WebAssets.Script),
        ("/components/history.js", "code/components/history.js", WebAssets.Script),
        ("/components/reload.js", "code/components/reload.js", WebAssets.Script),
        ("/components/tool-calls.js", "code/components/tool-calls.js", WebAssets.Script),
        ("/components/view-strip.js", "code/components/view-strip.js", WebAssets.Script),
        ("/components/workspace-pane.js", "code/components/workspace-pane.js", WebAssets.Script));

    private readonly Lock gate = new();
    private readonly HashSet<string> pending = new(StringComparer.Ordinal);
    private readonly string userHome;
    private readonly ClaudeFiles claude;
    private readonly PastSessions past;
    private readonly WindowLog log;
    private readonly Records records;
    private readonly CodeSessions sessions;
    private readonly WorkSignals signals;
    private readonly CodeSettings settings;
    private readonly ImageFolders explorer;
    private readonly WindowServer window;
    private readonly ITimer gathering;
    private readonly TimeProvider time;
    private readonly CodeBuild build;
    private readonly Action successor;
    private readonly ServeFiles files;
    private readonly CodeWorkspaces workspaces;
    private readonly Func<string, bool> raise;
    private readonly CancellationTokenSource reloaded = new();
    private string? opened;
    private bool reloading;
    private bool moved;

    /// <param name="program">The claude each session runs: <c>claude</c>, or a test's stand-in.</param>
    /// <param name="build">The build the server runs, which says when a newer one is published.</param>
    /// <param name="raise">Brings the window titled as it says forward through the compositor; false when it could not.</param>
    /// <param name="successor">Starts the server's successor on the build now published, once a reload stopped it.</param>
    /// <param name="startWorkspace">Starts the server of a folder's workspace, and gives the log it writes.</param>
    public CodeServer(string home, string userHome, string program, ClaudeFiles claude, TextWriter err, Func<string, bool> openWindow, Func<string, bool> raise, TimeProvider time, OmarchyTheme theme, CodeBuild build, Action successor, Func<string, string> startWorkspace)
    {
        this.raise = raise;
        this.userHome = userHome;
        this.claude = claude;
        past = new PastSessions(claude);
        this.time = time;
        this.build = build;
        this.successor = successor;
        files = new ServeFiles(home, Folder);
        log = new WindowLog(home, Folder, Folder, time);
        records = new Records(log, time);
        sessions = new CodeSessions(program, claude, Changed, Unanswered);
        signals = new WorkSignals(home, records);
        settings = new CodeSettings(home);
        explorer = new ImageFolders(userHome, files);
        workspaces = new CodeWorkspaces(home, startWorkspace);
        gathering = time.CreateTimer(_ => Tell(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var kind = new WindowKind(App, Command, Folder, userHome, Title, Title, Assets, WebAssets.Framing);
        window = new WindowServer(home, kind, err, openWindow, time, theme, records, Keyed, Api);
    }

    public string Origin => window.Origin;

    /// <summary>
    /// Resumes the sessions a reload handed on, opens its window and listens until <paramref name="stop"/>, until the
    /// window is gone, or until the user's reload; then it stops every session. After a reload whose window did not move
    /// onto the successor, as when no window listened, it starts the successor now, which opens its own window.
    /// </summary>
    public async Task Serve(CancellationToken stop)
    {
        log.Prune();
        if (Handover.Take(files) is { } handover)
        {
            opened = handover.Opened;
            sessions.Resume(handover);
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop, reloaded.Token);
        using var watching = build.Watch(() => window.Publish("event: build\ndata: published\n\n"));
        try
        {
            await window.Serve(stopping.Token).ConfigureAwait(false);
        }
        finally
        {
            sessions.Dispose();
        }

        if (reloading && !moved)
        {
            successor();
        }
    }

    /// <summary>Answers one request.</summary>
    public void Handle(HttpListenerContext context) => window.Handle(context);

    public void Dispose()
    {
        gathering.Dispose();
        sessions.Dispose();
        reloaded.Dispose();
    }

    /// <summary>A session changed: the window hears of it once the changes of the next 50 ms are gathered.</summary>
    private void Changed(string id)
    {
        // A reload stops every session as it hands them on: the window, about to move, hears none of it.
        if (Volatile.Read(ref reloading))
        {
            return;
        }

        lock (gate)
        {
            if (pending.Add(id) && pending.Count == 1)
            {
                gathering.Change(Gather, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Tell()
    {
        List<string> changed;
        lock (gate)
        {
            changed = [.. pending];
            pending.Clear();
        }

        foreach (var id in changed)
        {
            window.Publish("event: session\ndata: " + id + "\n\n");
        }
    }

    /// <summary>
    /// The calls <c>brain code</c> makes with the key: read a session, start one, which the window then shows, bring
    /// the window forward, and hand in a permission request, which waits for the user's answer; and the call
    /// <c>brain ask</c> makes, which hands in a session's questions.
    /// </summary>
    // navarone: 0000181
    private void Keyed(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        switch (request.HttpMethod, path)
        {
            case (Get, "/api/key/session"):
                Answering(response, () => sessions.Of(request.QueryString["id"]).View(), CodeJson.Default.CodeSessionView);
                break;
            case (Post, "/api/key/start"):
                Announced(request, response);
                break;
            case (Post, "/api/key/raise"):
                Send(response, new WindowRaised(Raise()), WindowJson.Default.WindowRaised);
                break;
            case (Post, "/api/key/permission"):
                Send(response, Decided(Read(request, CodeJson.Default.CodePermission, AskBody)), CodeJson.Default.CodeDecision);
                break;
            case (Post, "/api/key/ask"):
                Answering(response, () => Asked(Read(request, CodeJson.Default.CodeQuestionsIn)), CodeJson.Default.CodeQuestionsOut);
                break;
            default:
                Refuse(response, HttpStatusCode.NotFound, "no such call");
                break;
        }
    }

    /// <summary>Brings the window forward: the compositor raises it, or a fresh window takes its place.</summary>
    private string Raise()
    {
        if (raise(Title))
        {
            return "raised";
        }

        if (window.OpenWindow())
        {
            return "opened";
        }

        return "no-browser";
    }

    /// <summary>Starts the session <c>brain code</c> asked for, and tells the window, which then shows it.</summary>
    private void Announced(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Started(request, response) is { } session)
        {
            window.Publish("event: started\ndata: " + session.Id + "\n\n");
        }
    }

    /// <summary>The window's calls: a GET reads, and a POST, which must come from the window's origin, acts.</summary>
    private void Api(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (request.HttpMethod == Get)
        {
            Reads(request, response, path);
            return;
        }

        if (request.HttpMethod == Post && request.Headers["Origin"] is not null)
        {
            Acts(request, response, path);
            return;
        }

        Refuse(response, HttpStatusCode.NotFound, "no such call");
    }

    // navarone: 0000175
    // navarone: 0000208
    private void Reads(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        switch (path)
        {
            case "/api/sessions":
                Send(response, records.Time("sessions", string.Empty, null, List), CodeJson.Default.CodeSessionList);
                break;
            case "/api/settings":
                Send(response, settings.Read(), CodeJson.Default.CodeSettingsView);
                break;
            case "/api/agents":
                Send(response, new CodeAgents(claude.AgentNames()), CodeJson.Default.CodeAgents);
                break;
            case "/api/history":
                Answering(response, () => History(request), CodeJson.Default.CodePastList);
                break;
            case "/api/history-session":
                Answering(response, () => PastSession(request), CodeJson.Default.CodeConversation);
                break;
            case "/api/conversation":
                Reading(response, () => Conversation(request), CodeJson.Default.CodeConversation);
                break;
            case "/api/subagent":
                Reading(response, () => Subagent(request), CodeJson.Default.CodeConversation);
                break;
            case "/api/sent":
                Reading(response, () => Sent(request), CodeJson.Default.CodeSentList);
                break;
            case "/api/sent-agent":
                Reading(response, () => SentAgent(request), CodeJson.Default.CodeAgentView);
                break;
            case "/api/places":
                Send(response, records.Time("places", string.Empty, null, explorer.Places), CodeJson.Default.CodePlaces);
                break;
            case "/api/folder":
                Answering(response, () => Listed(request.QueryString["path"]), CodeJson.Default.CodeFolder);
                break;
            case "/api/image":
                Answering(response, () => ImageFile(request.QueryString["path"]), CodeJson.Default.CodeImage);
                break;
            default:
                Refuse(response, HttpStatusCode.NotFound, "no such call");
                break;
        }
    }

    // navarone: 0000176
    // navarone: 0000211
    private void Acts(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        switch (path)
        {
            case "/api/settings":
                Answering(response, () => SaveSettings(request), CodeJson.Default.CodeSettingsView);
                break;
            case "/api/start":
                Started(request, response);
                break;
            case "/api/say":
                Acting(response, () => Say(request));
                break;
            case "/api/interrupt":
                Acting(response, () => Interrupt(request));
                break;
            case "/api/end-process":
                Answering(response, () => EndProcess(request), CodeJson.Default.CodeProcessEnd);
                break;
            case "/api/answer":
                Acting(response, () => Answer(request));
                break;
            case "/api/reply":
                Acting(response, () => Reply(request));
                break;
            case "/api/dismiss":
                Acting(response, () => Dismiss(request));
                break;
            case "/api/attached-from":
                Acting(response, () => AttachedFrom(request));
                break;
            case "/api/close":
                Acting(response, () => records.Time("close", string.Empty, null, () => Close(request)));
                break;
            case "/api/reload":
                Reload(request, response);
                break;
            case "/api/workspace":
                Answering(response, () => Workspace(request), WorkspaceJson.Default.WorkspaceLaunch);
                break;
            default:
                Refuse(response, HttpStatusCode.NotFound, "no such call");
                break;
        }
    }

    /// <summary>
    /// A launch of the workspace of a session's folder, for a frame of the window, its server started when none runs;
    /// refused for a folder no session runs in.
    /// </summary>
    private WorkspaceLaunch Workspace(HttpListenerRequest request)
    {
        var folder = Read(request, CodeJson.Default.CodeWorkspaceRef)?.Folder ?? string.Empty;
        if (!sessions.All().Any(session => session.Cwd == folder))
        {
            throw new BrainException($"no session runs in {folder}, so it has no workspace here");
        }

        var url = records.Time("workspace", folder, null, () => workspaces.Launch(folder, window.Origin));
        return new WorkspaceLaunch(url);
    }

    private CodeSessionList List()
    {
        var views = sessions.All().Select(session => session.View()).ToList();
        return new CodeSessionList(views, userHome, Waits(views), opened);
    }

    /// <summary>What a reload waits for: nothing until a newer build is published, then the sessions while one runs.</summary>
    private string? Waits(IReadOnlyList<CodeSessionView> views)
    {
        if (!build.Newer())
        {
            return null;
        }

        if (views.Any(view => view.Runs()))
        {
            return CodeReload.Running;
        }

        return CodeReload.Ready;
    }

    /// <summary>
    /// The user's reload onto the newer build: every session is handed on, with the one the window shows, every session
    /// stops, the successor starts and the window moves onto it, and the server stops. Refused, with why, while no newer
    /// build is published or a session runs.
    /// </summary>
    private void Reload(HttpListenerRequest request, HttpListenerResponse response)
    {
        var open = Read(request, CodeJson.Default.CodeSessionRef)?.Session;
        Acting(response, () => records.Time("reload", open ?? string.Empty, null, () => Hand(open)));
        if (!reloading)
        {
            return;
        }

        // The answer goes before the server stops, since closing its listener drops every connection still open. Every
        // session stops before the successor starts, which resumes each by its id.
        response.Close();
        sessions.Dispose();
        moved = window.MoveTo(successor);
        reloaded.Cancel();
    }

    private bool Hand(string? open)
    {
        BrainException.ThrowIf(!build.Newer() ? "no newer build of brain code is published" : null);
        sessions.Hand(open, (handover) => Handover.Write(files, handover));
        reloading = true;
        return true;
    }

    private CodePastList History(HttpListenerRequest request)
    {
        var folder = Required(request, "folder");
        return records.Time("history", folder, null, () => past.List(folder));
    }

    private CodeConversation PastSession(HttpListenerRequest request)
    {
        var folder = Required(request, "folder");
        var id = Required(request, "id");
        return records.Time("history-session", id, null, () => past.Read(folder, id));
    }

    private static string Required(HttpListenerRequest request, string name)
    {
        var value = request.QueryString[name];
        if (string.IsNullOrEmpty(value))
        {
            throw new BrainException($"{name} is missing");
        }

        return value;
    }

    private CodeConversation Conversation(HttpListenerRequest request)
    {
        var session = sessions.Of(request.QueryString["session"]);
        var from = From(request);
        var seen = request.QueryString["generation"];
        return records.Time("conversation", session.Id, ReadBudgetMs, () => session.Conversation(from, seen));
    }

    private CodeConversation Subagent(HttpListenerRequest request)
    {
        var session = sessions.Of(request.QueryString["session"]);
        var tool = request.QueryString["tool"] ?? string.Empty;
        var from = From(request);
        var seen = request.QueryString["generation"];
        return records.Time("subagent", tool, ReadBudgetMs, () => session.Subagent(tool, from, seen));
    }

    private CodeFolder Listed(string? path) => records.Time("folder", path ?? string.Empty, FolderBudgetMs, () => ImageFolders.List(path));

    private CodeImage ImageFile(string? path) => records.Time("image", path ?? string.Empty, null, () => ImageFolders.Image(path));

    /// <summary>The folder the user attached images from in the file explorer, which it then offers first.</summary>
    private void AttachedFrom(HttpListenerRequest request)
    {
        var folder = Read(request, CodeJson.Default.CodeAttached)?.Folder;
        records.Time("attached-from", folder ?? string.Empty, null, () =>
        {
            explorer.Attached(folder);
            return true;
        });
    }

    private CodeSentList Sent(HttpListenerRequest request)
    {
        var session = sessions.Of(request.QueryString["session"]);
        return records.Time("sent", session.Id, ReadBudgetMs, () => session.Agents(signals, records));
    }

    private CodeAgentView SentAgent(HttpListenerRequest request)
    {
        var session = sessions.Of(request.QueryString["session"]);
        var tool = request.QueryString["tool"] ?? string.Empty;
        return records.Time("sent-agent", tool, ReadBudgetMs, () => session.Sent(tool, signals, records));
    }

    /// <summary>Starts the session the request names and sends its view, or 409 with why it was refused; the session, or null.</summary>
    private CodeSession? Started(HttpListenerRequest request, HttpListenerResponse response)
    {
        var start = Read(request, CodeJson.Default.CodeStart);
        try
        {
            var cwd = FolderOf(start);
            var session = records.Time("start", start?.Agent ?? string.Empty, null, () => sessions.Start(start?.Agent, cwd, start?.Resume));
            Send(response, session.View(), CodeJson.Default.CodeSessionView);
            return session;
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
            return null;
        }
    }

    /// <summary>The folder a start names, in full; the user's home for a new session that names none, and null for a moved one, which runs where its transcript says.</summary>
    private string? FolderOf(CodeStart? start)
    {
        if (string.IsNullOrWhiteSpace(start?.Cwd))
        {
            return start?.Resume is null ? userHome : null;
        }

        return Folders.Resolve(start.Cwd, userHome, userHome);
    }

    /// <summary>Keeps the choice the window sent; a body that is no choice is refused and nothing is saved.</summary>
    private CodeSettingsView SaveSettings(HttpListenerRequest request)
    {
        var choice = Read(request, CodeJson.Default.CodeSettingsView);
        if (choice is null)
        {
            throw new BrainException("settings need a choice of tool calls and brain calls");
        }

        if (choice.Agents is not (CodeSettingsView.Flow or CodeSettingsView.List))
        {
            throw new BrainException($"the agents panel draws as {CodeSettingsView.Flow} or {CodeSettingsView.List}, not {choice.Agents}");
        }

        return settings.Save(choice);
    }

    /// <summary>The user's words and the images he attached, to a session; refused when there are neither, or an image is no good.</summary>
    private void Say(HttpListenerRequest request)
    {
        var said = Read(request, CodeJson.Default.CodeSay, SayBody);
        var words = said?.Words?.Trim() ?? string.Empty;
        var images = AttachedImages.Checked(said?.Images);
        if (words.Length == 0 && images.Count == 0)
        {
            throw new BrainException("say something first");
        }

        var session = sessions.Of(said?.Session);
        records.Time("say", $"{session.Id} {images.Count}", null, () =>
        {
            session.Say(words, images);
            return true;
        });
    }

    /// <summary>The user's interrupt of a session's turn, a record of its own.</summary>
    private void Interrupt(HttpListenerRequest request)
    {
        var session = sessions.Of(Read(request, CodeJson.Default.CodeSessionRef)?.Session);
        records.Time("interrupt", session.Id, null, session.Interrupt);
    }

    /// <summary>
    /// The user's stop of one process a session started, with everything below it, a record of its own; refused when it
    /// names no process.
    /// </summary>
    private CodeProcessEnd EndProcess(HttpListenerRequest request)
    {
        var asked = Read(request, CodeJson.Default.CodeProcessRef) ?? throw new BrainException("a stop names a session and a process");
        var session = sessions.Of(asked.Session);
        if (asked.Pid < 1)
        {
            throw new BrainException("a stop names a process");
        }

        var pid = asked.Pid;
        var ended = records.Time("end-process", $"{session.Id} {pid}", null, () => session.End(pid));
        return new CodeProcessEnd(!ended);
    }

    /// <summary>
    /// The decision on a permission request: it waits in its session until the user answers, the session ends, or
    /// <see cref="PermissionHook.Longest"/> went by. A request that names no tool or no session of the window is denied
    /// at once. The request and its answer are a record each.
    /// </summary>
    private CodeDecision Decided(CodePermission? asked)
    {
        if (asked?.Tool is null)
        {
            return CodeDecision.Deny("brain code could not read the permission request.");
        }

        var session = sessions.Find(asked.Session);
        if (session is null)
        {
            return CodeDecision.Deny("brain code's window has no such session.");
        }

        var started = time.GetTimestamp();
        var wait = session.Ask(asked);
        records.Add("ask", $"{session.Id} {wait.Ask.Id} {asked.Tool}", 0, WindowRecord.Ok, null);
        if (!wait.Answer.Wait(PermissionHook.Longest))
        {
            session.Answer(wait.Ask.Id, CodeDecision.Deny("Nobody answered in brain code's window for a week."));
        }

        var decision = wait.Answer.Result;
        var behavior = decision.Allow ? "allow" : "deny";
        records.Add("answer", $"{session.Id} {wait.Ask.Id} {asked.Tool} {behavior}", time.GetElapsedTime(started).TotalMilliseconds, WindowRecord.Ok, null);
        return decision;
    }

    /// <summary>
    /// The user's answer to a permission request: allow, or deny with his words, or a plain deny without any; refused
    /// when the request waits no more.
    /// </summary>
    private void Answer(HttpListenerRequest request)
    {
        var answer = Read(request, CodeJson.Default.CodeAnswer);
        var session = sessions.Of(answer?.Session);
        var decision = CodeDecision.Allowed;
        if (answer is not { Allow: true })
        {
            decision = CodeDecision.DeniedByUser(answer?.Words);
        }

        var answered = session.Answer(answer?.Ask, decision);
        if (!answered)
        {
            throw new BrainException($"no request {answer?.Ask} waits in this session");
        }
    }

    /// <summary>
    /// Asks the user the questions a session's <c>brain ask</c> handed in, each a record: an agent that goes on hears at
    /// once that they wait, and one that waits hears once the user answered. Refused for an ask that is no good, a
    /// session the window does not have, and a session that ended before he answered.
    /// </summary>
    private CodeQuestionsOut Asked(CodeQuestionsIn? asked)
    {
        var document = AskContract.Checked(asked?.Ask);
        var session = sessions.Of(asked?.Session);
        var wait = session.AskQuestions(document, time.GetUtcNow());
        var questions = wait.Asked;
        for (var at = 0; at < questions.Questions.Count; at++)
        {
            records.Add("question", Headline(session.Id, questions, at), 0, WindowRecord.Ok, null);
        }

        if (questions.State == CodeQuestions.Ended)
        {
            Unanswered(session.Id, questions);
        }

        if (!questions.Wait)
        {
            return new CodeQuestionsOut(questions.Id, null);
        }

        return new CodeQuestionsOut(questions.Id, wait.Answers.GetAwaiter().GetResult());
    }

    /// <summary>The user's answers to a session's open questions, each a record with how long its question waited for it.</summary>
    private void Reply(HttpListenerRequest request)
    {
        var reply = Read(request, CodeJson.Default.CodeReply);
        var session = sessions.Of(reply?.Session);
        Waited("reply", session.Id, session.Reply(reply?.Ask, reply?.Replies));
    }

    /// <summary>
    /// The user's word that he closes a session's open questions unanswered, each a record with how long its question
    /// waited.
    /// </summary>
    private void Dismiss(HttpListenerRequest request)
    {
        var dismissal = Read(request, CodeJson.Default.CodeDismiss);
        var session = sessions.Of(dismissal?.Session);
        Waited("closed", session.Id, session.Dismiss(dismissal?.Ask));
    }

    /// <summary>The questions of a session that ended before the user answered, each a record with how long it waited.</summary>
    private void Unanswered(string session, CodeQuestions questions) => Waited("unanswered", session, questions);

    /// <summary>
    /// Each question of <paramref name="questions"/> as a record of <paramref name="kind"/>, with how long it waited.
    /// </summary>
    private void Waited(string kind, string session, CodeQuestions questions)
    {
        var waited = (time.GetUtcNow() - questions.Asked).TotalMilliseconds;
        for (var at = 0; at < questions.Questions.Count; at++)
        {
            records.Add(kind, Headline(session, questions, at), waited, WindowRecord.Ok, null);
        }
    }

    /// <summary>A question as its record names it: its session, its ask, its number and the start of its text.</summary>
    private static string Headline(string session, CodeQuestions questions, int at)
    {
        var text = questions.Questions[at].Text ?? string.Empty;
        var line = text.Split('\n')[0];
        if (line.Length > HeadlineLength)
        {
            line = line[..HeadlineLength];
        }

        return $"{session} {questions.Id} {at + 1} {line}";
    }

    private bool Close(HttpListenerRequest request)
    {
        sessions.Close(Read(request, CodeJson.Default.CodeSessionRef)?.Session);
        return true;
    }

    /// <summary>Does what the window asked, or answers 409 with why it was refused, which the window shows where the user acted.</summary>
    private static void Acting(HttpListenerResponse response, Action act)
    {
        try
        {
            act();
            response.StatusCode = (int)HttpStatusCode.NoContent;
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    /// <summary>Sends what <paramref name="answer"/> gives, or answers 409 with why it was refused.</summary>
    private static void Answering<T>(HttpListenerResponse response, Func<T> answer, JsonTypeInfo<T> type)
    {
        try
        {
            Send(response, answer(), type);
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    private static int From(HttpListenerRequest request) =>
        int.TryParse(request.QueryString["from"], NumberStyles.None, CultureInfo.InvariantCulture, out var from) ? from : 0;
}
