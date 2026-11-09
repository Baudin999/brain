using System.Globalization;
using System.Text.Json;
using Brain.Children;
using Brain.Windows;
using static Brain.Children.ClaudeStream;

namespace Brain.Code;

/// <summary>
/// One session of brain code: a <c>claude -p --agent &lt;agent&gt;</c> in its folder, on the user's own logged-in claude,
/// which reads the user's words as stream-json. It starts through <see cref="ClaudeRuns"/>, the one door, so it and
/// everything it starts stop with <see cref="Stop"/> and with brain code's server. Claude Code keeps the session as it
/// would in a terminal, so its id is a Claude Code session id: a new session is given one, and a session moved in from
/// a terminal continues by its own. What the window draws is read from the session's transcript; its output only says
/// whether it is answering, which session id it runs as, that it wrote, and the text its agent is writing now, which
/// the window shows until the transcript holds it. An interrupt ends the agent's turn and leaves the session alive. Its
/// transcript and its agents' are watched through the system's file events, and every change goes to <c>changed</c>. A
/// tool call that would ask the user's permission waits in its <see cref="PermissionAsks"/> until he answers in the
/// window; once the session ends or its turn is interrupted, each is denied. Its claude runs with
/// <see cref="AskVariable"/> set to its id, so a <c>brain ask</c> it runs finds its session; what it asks stays in its
/// <see cref="SessionQuestions"/>, and once the session ends, each open ask ends with it and goes to
/// <c>unanswered</c>. A session that had ended when brain code reloaded on a new build comes back as it was shown: ended,
/// with no claude, and its conversation read from its transcript.
/// </summary>
internal sealed class CodeSession : IDisposable
{
    /// <summary>The variable that tells <c>brain ask</c> the session it runs in: the session's id.</summary>
    public const string AskVariable = "BRAIN_CODE_SESSION";

    private const int ErrorLines = 20;

    /// <summary>The most of the user's first words the window is sent: the whole of a long one would ride every list.</summary>
    private const int PromptLength = 2000;

    /// <summary>The id of each interrupt request opens with this, so its control response is known.</summary>
    private const string InterruptPrefix = "interrupt-";

    /// <summary>What a permission request and a <c>brain ask</c> the interrupted turn left open are told.</summary>
    private const string InterruptedWhy = "The user interrupted the turn in brain code's window.";
    private static readonly TimeSpan Exit = TimeSpan.FromSeconds(5);

    private readonly Lock gate = new();
    private readonly ClaudeFiles claude;
    private readonly ClaudeRun? run;
    private readonly DateTimeOffset started;
    private readonly Action<string> changed;
    private readonly Action<string, CodeQuestions> unanswered;
    private readonly Queue<string> errors = new();
    private readonly SentAgents sent = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly PermissionAsks asks = new();
    private readonly SessionQuestions questions = new();
    private readonly LiveText live = new();
    private Transcript? transcript;
    private string runsAs;
    private bool working;
    private bool exited;
    private bool stopped;
    private bool interrupting;
    private int interrupts;
    private string? problem;

    private CodeSession(string id, string agent, string cwd, ClaudeFiles claude, ClaudeRun? run, DateTimeOffset started, Action<string> changed, Action<string, CodeQuestions> unanswered)
    {
        Id = id;
        Agent = agent;
        Cwd = cwd;
        this.claude = claude;
        this.run = run;
        this.started = started;
        this.changed = changed;
        this.unanswered = unanswered;
        runsAs = id;
    }

    /// <summary>The id the window knows the session by: the Claude Code session id it started as.</summary>
    public string Id { get; }

    public string Agent { get; }

    public string Cwd { get; }

    /// <summary>
    /// The arguments of the session's command line: the agent, typed events in and out with the text as it streams, the
    /// settings that hand each permission request to the window through <paramref name="brain"/>, and
    /// <paramref name="session"/> with its id: <c>--session-id</c> for a new session, <c>--resume</c> for one it
    /// continues.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string agent, string brain, string session, string id) =>
        ["-p", "--agent", agent, .. Flags, "--include-partial-messages", "--settings", PermissionHook.Settings(brain, id), session, id];

    /// <summary>
    /// Starts a session of <paramref name="agent"/> in <paramref name="cwd"/>: a new one, or the Claude Code session
    /// <paramref name="resume"/> continued. <paramref name="program"/> is <c>claude</c>; a test names a stand-in.
    /// </summary>
    // navarone: 0000165
    public static CodeSession Start(string program, string agent, string cwd, string? resume, ClaudeFiles claude, Action<string> changed, Action<string, CodeQuestions> unanswered)
    {
        var id = Guid.NewGuid().ToString("D");
        var session = "--session-id";
        if (resume is not null)
        {
            id = resume;
            session = "--resume";
        }

        var place = new RunPlace("code", "session", id, agent);
        // The hook runs this brain: the server is brain code --serve.
        var brain = Environment.ProcessPath ?? "brain";
        var info = Info(program, cwd, Arguments(agent, brain, session, id));
        info.Environment[AskVariable] = id;
        var run = ClaudeRuns.Start(info, agent, place);
        var started = new CodeSession(id, agent, cwd, claude, run, run.Started, changed, unanswered);
        run.Process.ErrorDataReceived += (_, line) => started.Error(line.Data);
        run.Process.BeginErrorReadLine();
        started.Follow();
        new Thread(() => started.Listen(run)) { IsBackground = true, Name = "code " + id }.Start();
        return started;
    }

    /// <summary>A session that had ended when brain code reloaded, as it was shown: ended, with why, and no claude.</summary>
    public static CodeSession Ended(CodeCarried carried, ClaudeFiles claude, Action<string> changed, Action<string, CodeQuestions> unanswered)
    {
        var session = new CodeSession(carried.Id, carried.Agent, carried.Cwd, claude, null, carried.Started, changed, unanswered);
        session.exited = true;
        session.problem = carried.Problem;
        session.Follow();
        return session;
    }

    /// <summary>The session as a reload carries it: by the Claude Code session id it runs as now.</summary>
    public CodeCarried Carried()
    {
        lock (gate)
        {
            return new CodeCarried(runsAs, Agent, Cwd, !exited && !stopped, problem, started);
        }
    }

    /// <summary>The session as the window lists it.</summary>
    public CodeSessionView View()
    {
        bool alive;
        bool answering;
        string? why;
        lock (gate)
        {
            alive = !exited && !stopped;
            answering = working;
            why = problem;
        }

        IReadOnlyList<RunningJob> running = [];
        if (alive && run is not null)
        {
            running = run.Running();
        }

        var agents = 0;
        string? name = null;
        string? prompt = null;
        if (Follow() is { } read)
        {
            agents = sent.Running(read);
            name = read.Name();
            prompt = Prompted(read);
        }

        var title = name;
        if (title is null && prompt is not null)
        {
            title = Transcript.FirstWords(prompt);
        }

        return new CodeSessionView(Id, Agent, Cwd, alive, answering, why, started, running, asks.All(), questions.All(), agents, name, title, prompt);
    }

    /// <summary>The name the user gave the session with <c>/rename</c>, or null when it has none.</summary>
    public string? Name() => Follow()?.Name();

    /// <summary>The user's first words of the transcript <paramref name="read"/>, cut when long, or null when he said none.</summary>
    private static string? Prompted(Transcript read)
    {
        var prompt = read.FirstPrompt();
        if (prompt is null || prompt.Length <= PromptLength)
        {
            return prompt;
        }

        return prompt[..PromptLength] + "...";
    }

    /// <summary>
    /// A permission request of claude's, which waits for the user until he answers or the session ends. A subagent's
    /// names the Agent call that sent it, once its meta is written.
    /// </summary>
    public PermissionWait Ask(CodePermission asked)
    {
        string? call = null;
        if (asked.AgentId is { } agent)
        {
            call = Follow()?.SentBy(agent);
        }

        var wait = asks.Add(asked, call);
        changed(Id);
        return wait;
    }

    /// <summary>
    /// Asks the user the questions of <paramref name="document"/>, after the events the session's transcript holds now,
    /// where the window draws them. They stay open until he answers or the session ends.
    /// </summary>
    public QuestionsWait AskQuestions(AskDocument document, DateTimeOffset asked)
    {
        var at = Follow()?.Count() ?? 0;
        var wait = questions.Add(document, at, asked);
        changed(Id);
        return wait;
    }

    /// <summary>Answers the request <paramref name="ask"/> with <paramref name="decision"/>; false when none of that id waits.</summary>
    public bool Answer(string? ask, CodeDecision decision)
    {
        if (!asks.Answer(ask, decision))
        {
            return false;
        }

        changed(Id);
        return true;
    }

    /// <summary>
    /// Every agent the session sent, in the order it sent them, each builder with its work's Navarone signals from
    /// <paramref name="signals"/>; none until claude wrote its transcript.
    /// </summary>
    public CodeSentList Agents(WorkSignals signals, Records records)
    {
        if (Follow() is not { } read)
        {
            return new CodeSentList([]);
        }

        return new CodeSentList(sent.All(read, signals, new Landings(Cwd, records)));
    }

    /// <summary>
    /// The user's answers to the open ask <paramref name="ask"/>: an agent that waits has them at once, and one that went
    /// on hears them as the user's next message. Refused when they do not answer it or the session stopped.
    /// </summary>
    public CodeQuestions Reply(string? ask, IReadOnlyList<AskReply>? replies)
    {
        var (answered, text) = questions.Answer(ask, replies);
        return Settled(answered, text);
    }

    /// <summary>
    /// The user's word that he closes the open ask <paramref name="ask"/> unanswered: an agent that waits has it at
    /// once, and one that went on hears it as the user's next message. Refused when no such ask is open.
    /// </summary>
    public CodeQuestions Dismiss(string? ask)
    {
        var (closed, text) = questions.Dismiss(ask);
        return Settled(closed, text);
    }

    /// <summary>
    /// An ask the user settled: an agent that went on hears <paramref name="text"/> as his next message.
    /// </summary>
    private CodeQuestions Settled(CodeQuestions settled, string text)
    {
        if (!settled.Wait)
        {
            Say(text, []);
        }

        changed(Id);
        return settled;
    }

    /// <summary>
    /// The agent the session's call <paramref name="toolUseId"/> sent, as its own view draws it, with the trail of the
    /// work it is part of; refused when the session sent no agent by that call.
    /// </summary>
    public CodeAgentView Sent(string toolUseId, WorkSignals signals, Records records)
    {
        var none = $"this session sent no agent by the call {toolUseId}";
        if (Follow() is not { } read)
        {
            throw new BrainException(none);
        }

        if (sent.View(read, toolUseId, signals, new Landings(Cwd, records)) is not { } view)
        {
            throw new BrainException(none);
        }

        return view;
    }

    /// <summary>
    /// The session's conversation from the event numbered <paramref name="from"/> of the reading <paramref name="seen"/>,
    /// with the text its agent is writing now; not found until claude wrote its transcript.
    /// </summary>
    public CodeConversation Conversation(int from, string? seen)
    {
        var read = Follow();
        if (read is null)
        {
            return new CodeConversation(0, [], Found: false);
        }

        return read.From(from, seen, live.Now());
    }

    /// <summary>
    /// The conversation of the agent the session's call <paramref name="toolUseId"/> sent, read only, from the event
    /// numbered <paramref name="from"/> of the reading <paramref name="seen"/>.
    /// The agents it sent in turn keep their transcripts beside the session's too.
    /// </summary>
    public CodeConversation Subagent(string toolUseId, int from, string? seen)
    {
        var read = Follow();
        if (read is null)
        {
            return new CodeConversation(0, [], Found: false);
        }

        if (sent.Of(read, toolUseId) is not { } agent)
        {
            return new CodeConversation(0, [], Found: false);
        }

        return agent.From(from, seen);
    }

    /// <summary>The user's words and the images he attached to them, sent to claude; refused once it stopped.</summary>
    public void Say(string words, IReadOnlyList<ImageBlock> images)
    {
        lock (gate)
        {
            Send(UserMessage(words, images));
            working = true;
            problem = null;
        }

        changed(Id);
    }

    /// <summary>
    /// Ends the agent's turn, as Ctrl+C does in a terminal: claude is asked over its input, and reads the next words once the
    /// turn ended. Claude Code stops the agents the session sent in the background with it, so every permission request
    /// that waits is denied, and every open <c>brain ask</c> ends unanswered while a later one is asked as any other.
    /// False when no turn runs, so nothing is interrupted, since claude would stop those agents even then; refused once
    /// the session stopped.
    /// </summary>
    public bool Interrupt()
    {
        lock (gate)
        {
            BrainException.ThrowIf(Gone());
            if (!working)
            {
                return false;
            }

            interrupts++;
            Send(InterruptRequest(InterruptPrefix + interrupts.ToString(CultureInfo.InvariantCulture)));
            interrupting = true;
            problem = null;
        }

        asks.Deny(InterruptedWhy);
        Unanswered(questions.EndOpen(InterruptedWhy));
        changed(Id);
        return true;
    }

    /// <summary>
    /// Ends the process <paramref name="pid"/> the session's claude started, with everything below it; the session and
    /// its turn go on. False when it runs no more, as every process of a session that stopped; refused for a process the
    /// session did not start.
    /// </summary>
    public bool End(int pid)
    {
        if (run is null)
        {
            return false;
        }

        lock (gate)
        {
            if (exited || stopped)
            {
                return false;
            }
        }

        var ended = run.End(pid);
        changed(Id);
        return ended;
    }

    /// <summary>Writes one line to claude's input, under the gate; refused once the session stopped.</summary>
    private void Send(string line)
    {
        BrainException.ThrowIf(Gone());
        if (run is null)
        {
            throw new BrainException("this session had ended before brain code reloaded; start it again with brain code --resume " + runsAs);
        }

        try
        {
            run.Process.StandardInput.WriteLine(line);
            run.Process.StandardInput.Flush();
        }
        catch (IOException error)
        {
            throw new BrainException($"the session stopped: {error.Message}");
        }
    }

    /// <summary>Why nothing can be sent to claude any more, under the gate, or null while it runs.</summary>
    private string? Gone()
    {
        if (exited || stopped)
        {
            return "this session stopped; start it again with brain code --resume " + runsAs;
        }

        return null;
    }

    /// <summary>Stops claude and everything it started. The transcript stays, so the session can be continued.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            working = false;
        }

        asks.End("The session was closed in brain code's window before the user answered.");
        EndQuestions("The session was closed in brain code's window before the user answered.");
        run?.Stop();
        changed(Id);
    }

    public void Dispose()
    {
        Stop();
        lock (gate)
        {
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }

            watchers.Clear();
        }

        run?.Dispose();
    }

    /// <summary>
    /// The session's transcript once claude wrote it, watched from then on, and the folder of the agents it sent once
    /// that is there: a write of an agent's transcript or of its meta, as it finishes, says the session changed. Null
    /// before claude wrote the transcript.
    /// </summary>
    private Transcript? Follow()
    {
        var read = Found();
        if (read is not null && Directory.Exists(read.Subagents))
        {
            Watch(read.Subagents, "*");
        }

        return read;
    }

    /// <summary>The transcript of the session id claude runs as, found anew when that id changed; null before claude wrote it.</summary>
    private Transcript? Found()
    {
        string id;
        lock (gate)
        {
            if (transcript is not null && transcript.Location.EndsWith(runsAs + ".jsonl", StringComparison.Ordinal))
            {
                return transcript;
            }

            id = runsAs;
        }

        if (claude.Session(id) is not { } path)
        {
            return null;
        }

        var found = new Transcript(path);
        lock (gate)
        {
            transcript = found;
        }

        Watch(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        return found;
    }

    /// <summary>Watches <paramref name="filter"/> in <paramref name="folder"/>: each write says the session changed.</summary>
    private void Watch(string folder, string filter)
    {
        lock (gate)
        {
            if (watchers.Any(watcher => watcher.Path == folder && watcher.Filter == filter))
            {
                return;
            }

            var watcher = new FileSystemWatcher(folder, filter) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            watcher.Changed += (_, _) => changed(Id);
            watcher.Created += (_, _) => changed(Id);
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    /// <summary>Reads claude's events, one JSON object a line, until it ends.</summary>
    private void Listen(ClaudeRun listened)
    {
        try
        {
            while (listened.Process.StandardOutput.ReadLine() is { } line)
            {
                Event(line);
            }

            listened.Process.WaitForExit(Exit);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The pipe broke as the process went.
        }

        var wrote = Found() is not null;
        lock (gate)
        {
            exited = true;
            working = false;
            if (!stopped)
            {
                var code = "unknown";
                if (listened.Process.HasExited)
                {
                    code = listened.Process.ExitCode.ToString(CultureInfo.InvariantCulture);
                }

                problem = $"the session stopped, exit {code}";
                if (!wrote)
                {
                    problem += ", and claude wrote no transcript";
                }

                if (errors.Count > 0)
                {
                    problem += ": " + string.Join('\n', errors);
                }
            }
        }

        asks.End("The session ended before the user answered.");
        EndQuestions("The session ended before the user answered.");

        changed(Id);
    }

    /// <summary>
    /// One of claude's events: init names the session id it runs as, a partial event of its own agent is text it writes
    /// now, and a result ends its turn; the result of a turn the user interrupted is no failure. Any event may follow a
    /// write to the transcript, so it looks for one not found yet.
    /// </summary>
    private void Event(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var type = Text(root, "type");
            var subtype = Text(root, "subtype");
            var top = !root.TryGetProperty("parent_tool_use_id", out var parent) || parent.ValueKind == JsonValueKind.Null;
            if (type == "stream_event" && top)
            {
                live.Streamed(root);
            }

            lock (gate)
            {
                if (type == "system" && subtype == "init")
                {
                    Began(root);
                }

                if (type == "control_response")
                {
                    Answered(root);
                }

                if (type == "result" && top)
                {
                    Ended(root);
                }
            }
        }

        Follow();
        changed(Id);
    }

    /// <summary>Ends every open ask, and every later one, with <paramref name="why"/>: the session ended.</summary>
    private void EndQuestions(string why) => Unanswered(questions.End(why));

    /// <summary>Each ask that ended before the user answered it goes to <c>unanswered</c>.</summary>
    private void Unanswered(IReadOnlyList<CodeQuestions> ended)
    {
        foreach (var asked in ended)
        {
            unanswered(Id, asked);
        }
    }

    /// <summary>
    /// A turn begins, under the gate: claude says init at the start of each, a turn of words it queued while the last
    /// ran among them, with the session id it runs as.
    /// </summary>
    private void Began(JsonElement init)
    {
        working = true;
        var id = Text(init, "session_id");
        if (ClaudeFiles.IsSessionId(id))
        {
            runsAs = id;
        }
    }

    /// <summary>
    /// claude's answer to a control request, under the gate: an interrupt it refused leaves the turn running, says why,
    /// and the user can stop it again.
    /// </summary>
    private void Answered(JsonElement answer)
    {
        if (!answer.TryGetProperty("response", out var response))
        {
            return;
        }

        if (!Text(response, "request_id").StartsWith(InterruptPrefix, StringComparison.Ordinal))
        {
            return;
        }

        if (Text(response, "subtype") != "error")
        {
            return;
        }

        interrupting = false;
        problem = "the interrupt was refused: " + Text(response, "error");
    }

    /// <summary>
    /// A result ends the turn, under the gate, and the text it was writing with it. One that is an error says why the
    /// turn failed, unless it is the abort of the turn the user interrupted.
    /// </summary>
    private void Ended(JsonElement result)
    {
        working = false;
        live.End();
        var interrupted = interrupting;
        interrupting = false;
        if (!result.TryGetProperty("is_error", out var failed) || failed.ValueKind != JsonValueKind.True)
        {
            return;
        }

        if (interrupted && Text(result, "terminal_reason").StartsWith("aborted", StringComparison.Ordinal))
        {
            return;
        }

        problem = "the turn failed: " + Text(result, "result");
    }

    private void Error(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (gate)
        {
            errors.Enqueue(line);
            while (errors.Count > ErrorLines)
            {
                errors.Dequeue();
            }
        }
    }
}
