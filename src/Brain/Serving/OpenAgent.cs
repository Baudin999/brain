using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Brain.Children;
using Brain.Conversations;
using static Brain.Children.ClaudeStream;

namespace Brain.Serving;

/// <summary>What the user and the open agent said, as the window draws it: the phases, the agent's answer so far among them, and what the agent is doing.</summary>
internal sealed record AgentTalk(IReadOnlyList<Phase> Phases, bool Working, string? Activity, string? Problem, long Version);

/// <summary>
/// Where an open agent starts: what it runs for in the window, its model and effort, the phases already said, what it
/// reads before the user's first words, and where each new phase is kept. A sitting keeps its phases in its trail; a
/// chat keeps them nowhere.
/// </summary>
/// <param name="Seed">Puts what the agent reads first before the user's first words, as one message.</param>
/// <param name="Keep">Keeps a phase as it is said: the user's when it is sent, the agent's when its turn ends.</param>
/// <param name="Held">What the agent holds while it lives, such as a sitting's lock; let go when its process ends or it is stopped.</param>
internal sealed record AgentStart(RunPlace Place, string Model, string Effort, IReadOnlyList<Phase> Phases, Func<string, string> Seed, Action<Phase> Keep, IDisposable? Held = null);

/// <summary>
/// The open agent the window talks with: one <c>claude -p --agent brain</c> process, the same agent a terminal runs,
/// which brain serve feeds the user's words as stream-json and reads typed events from. brain serve, not the agent,
/// keeps what is said, through <see cref="AgentStart.Keep"/>: the user's phase when it is sent, the agent's when its
/// turn ends, its text only, never its thinking or its tool calls. It starts through <see cref="ClaudeRuns"/>, the one
/// door, so the process and everything it started, the builders' runs among them, stop with <see cref="Stop"/> and
/// with brain serve.
/// </summary>
internal sealed class OpenAgent
{
    /// <summary>The open agent's own tools, and Edit and Write for the builders it sends, which run under its permissions.</summary>
    public static readonly IReadOnlyList<string> Tools = ["Bash", "Read", "Grep", "Glob", "Agent", "Skill", "SendMessage", "WebSearch", "WebFetch", "Edit", "Write"];

    private const string PermissionMode = "dontAsk";
    private const int ErrorLines = 20;
    private const int ActivityLength = 160;
    private const string Paragraph = "\n\n";
    private static readonly TimeSpan Exit = TimeSpan.FromSeconds(5);
    private static long changes;

    /// <summary>The inputs that say what a tool call works on, the first a call has.</summary>
    private static readonly string[] ToolSubjects = ["description", "command", "file_path", "pattern", "query", "url", "skill"];

    private readonly Lock gate = new();
    private readonly AgentStart start;
    private readonly ClaudeRun run;
    private readonly Process process;
    private readonly TimeProvider time;
    private readonly List<Phase> phases;
    private readonly List<string> said = [];
    private readonly StringBuilder saying = new();
    private readonly Queue<string> errors = new();

    /// <summary>The agents it sent that still run, by task: what each does and whether it runs past the turn that sent it.</summary>
    private readonly Dictionary<string, (string What, bool Background)> sent = new(StringComparer.Ordinal);
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool seeded;
    private bool working;
    private bool stopped;
    private bool exited;
    private string? activity;
    private string? problem;
    private DateTimeOffset answering;
    private long version = Interlocked.Increment(ref changes);

    private OpenAgent(AgentStart start, ClaudeRun run, TimeProvider time)
    {
        this.start = start;
        this.run = run;
        process = run.Process;
        this.time = time;
        phases = [.. start.Phases];
    }

    public int Pid => process.Id;

    /// <summary>When the agent's process started.</summary>
    public DateTimeOffset Started => run.Started;

    public string Model => start.Model;

    public string Effort => start.Effort;

    /// <summary>False once the process ended, by itself or by <see cref="Stop"/>.</summary>
    public bool Alive
    {
        get
        {
            lock (gate)
            {
                return !exited && !stopped;
            }
        }
    }

    /// <summary>The arguments of the agent's command line: the open agent on the model and effort picked, typed events in and out.</summary>
    public static IReadOnlyList<string> Arguments(string model, string effort) =>
    [
        "-p", "--agent", "brain", "--model", model, "--effort", effort,
        .. Flags, "--include-partial-messages",
        "--no-session-persistence", "--permission-mode", PermissionMode, "--allowedTools", string.Join(',', Tools),
    ];

    /// <summary>
    /// Starts the open agent in <paramref name="cwd"/>. <paramref name="program"/> is <c>claude</c>; a test names a
    /// stand-in for it.
    /// </summary>
    // navarone: 0000108
    public static OpenAgent Start(AgentStart start, string program, string cwd, TimeProvider time)
    {
        var info = Info(program, cwd, Arguments(start.Model, start.Effort));
        var run = ClaudeRuns.Start(info, start.Model, start.Place);
        var process = run.Process;
        var agent = new OpenAgent(start, run, time);
        process.ErrorDataReceived += (_, line) => agent.Error(line.Data);
        process.BeginErrorReadLine();
        new Thread(agent.Listen) { IsBackground = true, Name = "agent " + start.Place.Name }.Start();
        return agent;
    }

    /// <summary>
    /// The user's words: kept as the user's phase, then sent to the agent, the first time with the seed before them.
    /// Refused while the agent still answers the last phase.
    /// </summary>
    public void Say(string words, IReadOnlyList<PhaseComment> comments)
    {
        lock (gate)
        {
            BrainException.ThrowIf(working ? "the agent is still answering; send again once it is done" : !Alive ? "the agent stopped" : null);
            // The seed reads what was kept before these words, so they reach the agent once, after the seed.
            var now = time.GetUtcNow();
            var phase = new Phase(phases.Count + 1, Trail.User, now, Model: null, Effort: null, words, comments.Count > 0 ? comments : null);
            var message = seeded ? Trail.Body(phase) : start.Seed(Trail.Body(phase));
            Kept(phase);
            seeded = true;
            try
            {
                process.StandardInput.WriteLine(UserMessage(message));
                process.StandardInput.Flush();
            }
            catch (IOException error)
            {
                throw new BrainException($"the agent stopped: {error.Message}");
            }

            working = true;
            answering = now;
            problem = null;
            activity = null;
            Changed();
        }
    }

    /// <summary>What was said, and while the agent answers, its answer so far as the last phase.</summary>
    public AgentTalk Talk()
    {
        lock (gate)
        {
            var now = phases.ToList();
            if (working)
            {
                now.Add(new Phase(now.Count + 1, Trail.Agent, answering, start.Model, start.Effort, Answer()));
            }

            return new AgentTalk(now, working, activity, problem, version);
        }
    }

    /// <summary>Waits until what was said changed past <paramref name="seen"/>, or until <paramref name="limit"/> passed.</summary>
    public void WaitPast(long seen, TimeSpan limit)
    {
        Task next;
        lock (gate)
        {
            if (version != seen)
            {
                return;
            }

            next = changed.Task;
        }

        next.Wait(limit);
    }

    /// <summary>
    /// What runs under the agent now: the agents it sent that have not ended, then each process it started that still
    /// runs, the agent's own child or an orphan that came to its keeper, with whether anything below it holds a build slot.
    /// </summary>
    public IReadOnlyList<AgentWork> Running()
    {
        List<AgentWork> running;
        lock (gate)
        {
            if (stopped || exited)
            {
                return [];
            }

            running = [.. sent.Values.Select(task => new AgentWork(AgentWork.AnAgent, task.What, Slot: false))];
        }

        running.AddRange(run.Processes());
        return running;
    }

    /// <summary>
    /// Stops the agent and every process it started: its keeper ends all of them and then itself, and what is still
    /// there after a while is killed by its tree. What the agent had not said in full is gone.
    /// </summary>
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
            Changed();
        }

        run.Stop();
        start.Held?.Dispose();
    }

    /// <summary>Reads the agent's events, one JSON object a line, until it ends.</summary>
    private void Listen()
    {
        try
        {
            while (process.StandardOutput.ReadLine() is { } line)
            {
                Event(line);
            }

            process.WaitForExit(Exit);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // The pipe broke as the process went.
        }

        lock (gate)
        {
            exited = true;
            if (!stopped)
            {
                var code = process.HasExited ? process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
                problem = $"the agent stopped, exit {code}" + (errors.Count > 0 ? ": " + string.Join('\n', errors) : string.Empty);
                working = false;
            }

            Changed();
        }

        start.Held?.Dispose();
    }

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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var top = !root.TryGetProperty("parent_tool_use_id", out var parent) || parent.ValueKind == JsonValueKind.Null;
            lock (gate)
            {
                switch (Text(root, "type"))
                {
                    case "stream_event" when top:
                        Streamed(root.GetProperty("event"));
                        break;
                    case "assistant":
                        Said(root.GetProperty("message"), top);
                        break;
                    case "result":
                        Done(root);
                        break;
                    case "system":
                        Sent(root);
                        break;
                    default:
                        return;
                }

                Changed();
            }
        }
    }

    /// <summary>A piece of the answer as it streams: a text block starts, or text comes in.</summary>
    private void Streamed(JsonElement streamed)
    {
        switch (Text(streamed, "type"))
        {
            case "content_block_start" when streamed.TryGetProperty("content_block", out var block):
                if (Text(block, "type") == "text")
                {
                    saying.Clear();
                }

                break;
            case "content_block_delta" when streamed.TryGetProperty("delta", out var delta) && Text(delta, "type") == "text_delta":
                saying.Append(Text(delta, "text"));
                break;
        }
    }

    /// <summary>A whole block the agent said: text of the open agent itself joins its answer, and a tool call says what it does.</summary>
    private void Said(JsonElement message, bool top)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var block in content.EnumerateArray())
        {
            switch (Text(block, "type"))
            {
                case "text" when top:
                    said.Add(Text(block, "text"));
                    saying.Clear();
                    break;
                case "tool_use":
                    activity = Describe(block, top);
                    break;
            }
        }
    }

    /// <summary>
    /// An agent the open agent or one it sent started, moved to the background, or ended. Claude Code says a later move to
    /// the background as <c>task_updated</c> with <c>patch.is_backgrounded</c>. A background shell is a process, not an agent.
    /// </summary>
    private void Sent(JsonElement system)
    {
        var task = Text(system, "task_id");
        switch (Text(system, "subtype"))
        {
            case "task_started" when task.Length > 0 && Text(system, "subagent_type") is { Length: > 0 } type:
                var what = Text(system, "description");
                sent[task] = (what.Length > 0 ? type + ": " + what : type, Backgrounded(system));
                break;
            case "task_updated" when sent.TryGetValue(task, out var known) && system.TryGetProperty("patch", out var patch) && Backgrounded(patch):
                // An agent sent in the foreground and moved to the background later runs past the turn that sent it.
                sent[task] = known with { Background = true };
                break;
            case "task_notification":
                sent.Remove(task);
                break;
        }
    }

    private static bool Backgrounded(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("is_backgrounded", out var backgrounded) && backgrounded.ValueKind == JsonValueKind.True;

    /// <summary>The turn ended: the answer is kept as the agent's phase, an agent it sent and waited for has ended, and a failure is shown.</summary>
    private void Done(JsonElement result)
    {
        var answer = Answer();
        if (answer.Length > 0)
        {
            Kept(new Phase(phases.Count + 1, Trail.Agent, answering, start.Model, start.Effort, answer));
        }

        if (result.TryGetProperty("is_error", out var failed) && failed.ValueKind == JsonValueKind.True)
        {
            problem = "the agent failed: " + (Text(result, "result") is { Length: > 0 } why ? why : Text(result, "subtype"));
        }

        foreach (var task in sent.Where(task => !task.Value.Background).Select(task => task.Key).ToList())
        {
            sent.Remove(task);
        }

        said.Clear();
        saying.Clear();
        working = false;
        activity = null;
    }

    private string Answer()
    {
        var parts = said.Where(part => part.Trim().Length > 0).Select(part => part.Trim()).ToList();
        if (saying.ToString().Trim() is { Length: > 0 } streaming)
        {
            parts.Add(streaming);
        }

        return string.Join(Paragraph, parts);
    }

    /// <summary>One quiet line for a tool call: its name and what it works on, a builder's marked as the agent's.</summary>
    private static string Describe(JsonElement block, bool top)
    {
        var name = Text(block, "name");
        var on = block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
            ? ToolSubjects.Select(key => Text(input, key)).FirstOrDefault(value => value.Length > 0) ?? string.Empty
            : string.Empty;
        var line = (top ? string.Empty : "a subagent: ") + name + (on.Length > 0 ? ": " + on.Split('\n')[0] : string.Empty);
        return line.Length > ActivityLength ? line[..ActivityLength] + "..." : line;
    }

    /// <summary>A phase said: the agent's own list holds it, and the place it started from keeps it. Called under the gate.</summary>
    private void Kept(Phase phase)
    {
        phases.Add(phase);
        start.Keep(phase);
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

    /// <summary>Wakes every window waiting on this agent. Called under the gate.</summary>
    private void Changed()
    {
        version = Interlocked.Increment(ref changes);
        var woken = changed;
        changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        woken.TrySetResult();
    }
}
