namespace Brain.Code;

/// <summary>
/// The agents one session sent, read from the metas and transcripts beside the session's own: each in the order the
/// session sent it, with its kind of view, its state and how long it ran, and for one agent its brief, what it does
/// now, its report and the trail of the work it is part of. A validator is part of the work of the builder its brief
/// names by agent id. Each agent's transcript is kept once found, so a read costs what was written since.
/// </summary>
internal sealed class SentAgents
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Transcript> transcripts = new(StringComparer.Ordinal);

    /// <summary>The transcript of the agent the session's call <paramref name="toolUseId"/> sent, or null when there is none yet.</summary>
    public Transcript? Of(Transcript session, string toolUseId)
    {
        var meta = session.Sent().FirstOrDefault(one => one.ToolUseId == toolUseId);
        if (meta is null)
        {
            return null;
        }

        return Opened(meta);
    }

    /// <summary>Every agent the session sent, in the order it sent them.</summary>
    public IReadOnlyList<CodeSent> All(Transcript session) => Listed(Read(session));

    /// <summary>How many of the agents the session sent still run, by the same state the list shows.</summary>
    public int Running(Transcript session) => All(session).Count(sent => sent.State == CodeSent.Running);

    /// <summary>
    /// The agent the session's call <paramref name="toolUseId"/> sent, as its view draws it, or null when the session
    /// sent none by that call. The commits a landing put on the default branch come from <paramref name="landings"/>,
    /// and the work's Navarone signals from <paramref name="signals"/>.
    /// </summary>
    // navarone: 0000190
    public CodeAgentView? View(Transcript session, string toolUseId, WorkSignals signals, Landings landings)
    {
        var agents = Read(session);
        var agent = agents.Find(one => one.Meta.ToolUseId == toolUseId);
        if (agent is null)
        {
            return null;
        }

        var trail = TrailOf(agent, agents, signals, landings);
        var sent = Listed(agents)[agents.IndexOf(agent)];
        return new CodeAgentView(sent, agent.Brief, agent.Report, trail, agent.Questions);
    }

    /// <summary>Each agent the session sent, with its events, the earliest sent first: by when its transcript starts.</summary>
    // navarone: 0000191
    private List<Agent> Read(Transcript session)
    {
        var calls = session.All();
        var failed = FailedCalls(calls);
        var metas = WithUnwritten(calls, session.Sent());
        var builders = metas.Where(meta => meta.Type == Agent.BuilderType && meta.Agent.Length > 0).Select(meta => meta.Agent).ToList();
        var agents = new List<Agent>();
        foreach (var meta in metas)
        {
            var events = Opened(meta)?.All() ?? [];
            agents.Add(new Agent(meta, events, failed, builders));
        }

        return [.. agents.OrderBy(agent => agent.Sent.Started ?? DateTimeOffset.MaxValue)];
    }

    /// <summary>
    /// The agents as the list shows them, in the same order: each readiness check with the agent it checked, and each
    /// builder with the outline of its work. A check belongs to the builder or validator sent with exactly its brief;
    /// one whose brief matches none belongs to the next builder or validator sent after it; one with none after it
    /// belongs to none.
    /// </summary>
    // navarone: 0000214
    private static List<CodeSent> Listed(List<Agent> agents)
    {
        var listed = new List<CodeSent>();
        foreach (var agent in agents)
        {
            listed.Add(agent.Sent with { Attached = CheckedBy(agent, agents), Steps = OutlineOf(agent, agents) });
        }

        return listed;
    }

    /// <summary>The call id of the agent the check <paramref name="check"/> checked; null for any other agent, and for a check with no agent sent after it.</summary>
    private static string? CheckedBy(Agent check, List<Agent> agents)
    {
        if (check.Sent.Kind != CodeSent.Ready)
        {
            return null;
        }

        var works = agents.Where(IsWork).ToList();
        var sentAfter = agents.Skip(agents.IndexOf(check) + 1).Where(IsWork).ToList();
        if (check.Brief is { } brief)
        {
            var same = sentAfter.Find(one => one.Brief == brief) ?? works.Find(one => one.Brief == brief);
            if (same is not null)
            {
                return same.Meta.ToolUseId;
            }
        }

        return sentAfter.FirstOrDefault()?.Meta.ToolUseId;
    }

    /// <summary>A builder's work: the validators sent for it, and its steps.</summary>
    private sealed record Piece(List<Agent> Validators, IReadOnlyList<CodeStep> Steps);

    private static Piece PieceOf(Agent builder, List<Agent> agents)
    {
        var validators = agents.Where(one => one.Sent.Kind == CodeSent.Validator && one.Sent.Work == builder.Meta.Agent).ToList();
        return new Piece(validators, Trail.Of(builder.Worker, [.. validators.Select(one => one.Worker)]));
    }

    /// <summary>True for a builder and a validator: the agents a readiness check checks.</summary>
    private static bool IsWork(Agent agent) => agent.Sent.Kind == CodeSent.Builder || agent.Sent.Kind == CodeSent.Validator;

    /// <summary>The short steps of the work a builder heads, the earliest first; null for any other agent.</summary>
    private static List<CodeOutline>? OutlineOf(Agent builder, List<Agent> agents)
    {
        if (builder.Sent.Kind != CodeSent.Builder)
        {
            return null;
        }

        var piece = PieceOf(builder, agents);
        var tools = new Dictionary<string, string>(StringComparer.Ordinal) { [builder.Meta.Agent] = builder.Meta.ToolUseId };
        foreach (var validator in piece.Validators)
        {
            tools[validator.Meta.Agent] = validator.Meta.ToolUseId;
        }

        var outline = new List<CodeOutline>();
        foreach (var step in piece.Steps)
        {
            outline.Add(new CodeOutline(step.Kind, tools[step.Agent], step.At, step.Round, step.Commits.Count, step.Landed));
        }

        return outline;
    }

    /// <summary>The ids of the session's calls whose result was an error.</summary>
    private static HashSet<string> FailedCalls(IReadOnlyList<CodeEvent> calls)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var one in calls)
        {
            if (one is { Kind: CodeEvent.Result, Failed: true, Id: { } call })
            {
                failed.Add(call);
            }
        }

        return failed;
    }

    /// <summary>
    /// The metas of the session's agents, and for each Agent call whose meta Claude Code has not written yet, one as the
    /// call names it, with no id and no transcript: the agent runs from its call on, so the list, the session's count and
    /// the stop confirm know it at once.
    /// </summary>
    private static List<SentMeta> WithUnwritten(IReadOnlyList<CodeEvent> calls, IReadOnlyList<SentMeta> metas)
    {
        var all = new List<SentMeta>(metas);
        var written = metas.Select(meta => meta.ToolUseId).ToHashSet(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            if (call.Kind != CodeEvent.Agent || call.Id is null || written.Contains(call.Id))
            {
                continue;
            }

            all.Add(new SentMeta(string.Empty, call.Id, call.Name ?? string.Empty, call.Subject ?? string.Empty, null, Finished: false, Stopped: false, string.Empty));
        }

        return all;
    }

    /// <summary>The transcript of an agent by its meta, kept once found; null before the agent wrote one.</summary>
    private Transcript? Opened(SentMeta meta)
    {
        lock (gate)
        {
            if (transcripts.TryGetValue(meta.ToolUseId, out var known))
            {
                return known;
            }
        }

        if (!File.Exists(meta.Transcript))
        {
            return null;
        }

        return Kept(meta.ToolUseId, meta.Transcript);
    }

    private Transcript Kept(string toolUseId, string path)
    {
        lock (gate)
        {
            transcripts.TryAdd(toolUseId, new Transcript(path));
            return transcripts[toolUseId];
        }
    }

    /// <summary>The trail of the work the agent is part of, with the work's signals; null for an agent that is part of no builder's work.</summary>
    private static CodeTrail? TrailOf(Agent agent, List<Agent> agents, WorkSignals signals, Landings landings)
    {
        var work = agent.Sent.Work;
        var builder = agents.Find(one => one.Sent.Kind == CodeSent.Builder && one.Meta.Agent == work);
        if (builder is null)
        {
            return null;
        }

        var piece = PieceOf(builder, agents);
        var validators = piece.Validators;
        var told = piece.Steps.Select(landings.Told).ToList();
        var found = new CodeSignals(Read: false, []);
        if (builder.Meta.Worktree is { } worktree)
        {
            var mark = builder.NavaroneRuns + validators.Sum(one => one.NavaroneRuns);
            found = signals.Of(worktree, mark);
        }

        return new CodeTrail(builder.Meta.Agent, told, found);
    }

    /// <summary>
    /// An agent the session sent: its meta and its events so far, and what they say of it. <c>failed</c> holds the
    /// calls whose result was an error, and <c>builders</c> the ids of the session's builders, one of which a
    /// validator's brief names.
    /// </summary>
    private sealed class Agent
    {
        public const string BuilderType = "builder";
        private const string ValidatorType = "validator";
        private const string ReadyType = "brain-ready";
        private const string Handback = "SubagentHandback";
        private const string Ready = "READY";
        private const string NotReady = "NOT READY";
        private const string BashTool = "Bash";
        private const string Navarone = "navarone";

        /// <summary>What may follow a verdict before its questions: a colon, a stop, a dash, a line's end.</summary>
        private static readonly char[] VerdictEnd = [':', '.', '-', ' ', '\r', '\n'];

        public Agent(SentMeta meta, IReadOnlyList<CodeEvent> events, IReadOnlySet<string> failed, IReadOnlyList<string> builders)
        {
            Meta = meta;
            Events = events;
            var state = StateOf(meta, failed);
            var started = events.FirstOrDefault(one => one.At is not null)?.At;
            DateTimeOffset? ended = null;
            if (state != CodeSent.Running)
            {
                ended = events.LastOrDefault(one => one.At is not null)?.At;
            }

            string? now = null;
            if (state == CodeSent.Running)
            {
                now = NowOf();
            }

            Sent = new CodeSent(meta.Agent, meta.ToolUseId, meta.Type, Kind, meta.Description, state, started, ended, WorkOf(builders), Verdict(), meta.Worktree, Now: now);
        }

        public SentMeta Meta { get; }

        public IReadOnlyList<CodeEvent> Events { get; }

        public CodeSent Sent { get; }

        /// <summary>The kind of view the agent opens in, from its type.</summary>
        public string Kind => KindOf(Meta.Type);

        /// <summary>The agent as a part of a piece of work.</summary>
        public Worker Worker => new(Meta.Agent, Events, Meta.Ended);

        /// <summary>What the agent was sent to do: the first words it was given.</summary>
        public string? Brief => Events.FirstOrDefault(one => one.Kind == CodeEvent.User)?.Text;

        /// <summary>The agent's report: the last it handed back, or, once it finished, its last words; null before it gave one.</summary>
        public string? Report
        {
            get
            {
                var handed = Events.LastOrDefault(one => one.Kind == CodeEvent.Tool && one.Name == Handback && one.Text is not null);
                if (handed is not null)
                {
                    return handed.Text;
                }

                if (!Meta.Ended)
                {
                    return null;
                }

                return Events.LastOrDefault(one => one.Kind == CodeEvent.Said)?.Text;
            }
        }

        /// <summary>What an agent does now, in one line: its last tool call with what it works on, or the first line of its last words; null before it did either.</summary>
        private string? NowOf()
        {
            var last = Events.LastOrDefault(one => one.Kind is CodeEvent.Tool or CodeEvent.Said);
            if (last is null)
            {
                return null;
            }

            if (last.Kind == CodeEvent.Tool)
            {
                return $"{last.Name} {last.Subject}".Trim();
            }

            return last.Text!.Split('\n')[0];
        }

        /// <summary>A readiness check's questions: its report after its verdict; null for any other agent, and for a verdict that asks none.</summary>
        public string? Questions
        {
            get
            {
                if (Sent.Verdict is not { } verdict || Report is not { } report)
                {
                    return null;
                }

                var asks = report.TrimStart()[verdict.Length..].TrimStart(VerdictEnd).Trim();
                if (asks.Length == 0)
                {
                    return null;
                }

                return asks;
            }
        }

        /// <summary>How many of the agent's Bash calls that run Navarone gave back: the one way Navarone's log changes for its work.</summary>
        public int NavaroneRuns
        {
            get
            {
                var calls = new HashSet<string>(StringComparer.Ordinal);
                var runs = 0;
                foreach (var one in Events)
                {
                    if (RunsNavarone(one))
                    {
                        calls.Add(one.Id!);
                    }

                    if (one.Kind == CodeEvent.Result && calls.Contains(one.Id ?? string.Empty))
                    {
                        runs++;
                    }
                }

                return runs;
            }
        }

        /// <summary>True for a Bash call whose command runs Navarone.</summary>
        private static bool RunsNavarone(CodeEvent one)
        {
            if (one.Kind != CodeEvent.Tool || one.Name != BashTool || one.Id is null)
            {
                return false;
            }

            return one.Input?.GetValueOrDefault("command") is { } command && command.Contains(Navarone, StringComparison.Ordinal);
        }

        private static string KindOf(string type) => type switch
        {
            BuilderType => CodeSent.Builder,
            ValidatorType => CodeSent.Validator,
            ReadyType => CodeSent.Ready,
            _ => CodeSent.Other,
        };

        /// <summary>
        /// Running until its meta says it finished, or that the user stopped it; failed when the call that sent it gave
        /// back an error.
        /// </summary>
        private static string StateOf(SentMeta meta, IReadOnlySet<string> failed)
        {
            if (failed.Contains(meta.ToolUseId))
            {
                return CodeSent.Failed;
            }

            if (meta.Stopped && !meta.Finished)
            {
                return CodeSent.Stopped;
            }

            if (meta.Finished)
            {
                return CodeSent.Finished;
            }

            return CodeSent.Running;
        }

        /// <summary>The builder whose work the agent is part of: a builder's own, the one a validator's brief names by its id, or none.</summary>
        private string? WorkOf(IReadOnlyList<string> builders)
        {
            if (Kind == CodeSent.Builder)
            {
                return Meta.Agent;
            }

            if (Kind != CodeSent.Validator || Brief is not { } brief)
            {
                return null;
            }

            return builders.FirstOrDefault(builder => brief.Contains(builder, StringComparison.Ordinal));
        }

        /// <summary>A readiness check's verdict, by the words its report opens with; null for any other agent, and for a report that opens with neither.</summary>
        private string? Verdict()
        {
            if (Kind != CodeSent.Ready)
            {
                return null;
            }

            var opens = Report?.TrimStart() ?? string.Empty;
            if (opens.StartsWith(NotReady, StringComparison.Ordinal))
            {
                return NotReady;
            }

            if (opens.StartsWith(Ready, StringComparison.Ordinal))
            {
                return Ready;
            }

            return null;
        }
    }
}
