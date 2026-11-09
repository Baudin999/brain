using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Code;
using Brain.Serving;
using Brain.Windows;

namespace Brain.Tests;

/// <summary>
/// brain code's server, over real HTTP on 127.0.0.1, with a stand-in for claude and a Claude Code config folder of its
/// own: <c>brain code</c> starts a session with the key, of the agent it names, which runs <c>claude -p --agent</c> with
/// a session id of its own; the user's words reach claude, and what claude wrote to its transcript comes back as brain's
/// own events; a session moved in by its id runs in the folder it ran in and shows its history before a word is said;
/// an agent a session sent reads from its own transcript by the id of the call that sent it; and closing a session
/// stops its claude.
/// </summary>
[Collection(ReviewServerTests.StartsClaude)]
public sealed partial class CodeServerTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly TempBrain brain = new();
    private readonly List<string> windows = [];

    /// <summary>The clock every server of a test runs on: the system's, until a test has it leap.</summary>
    private readonly LeapingClock clock = new();
    private readonly CancellationTokenSource stop = new();
    private readonly string config;
    private readonly string project;
    private readonly string publish;
    private readonly CodeServer server;
    private readonly Task serving;
    private readonly HttpClient client;
    private int successors;

    /// <summary>What a successor's start does past being counted, such as start a successor server; nothing unless a test sets it.</summary>
    private Action succeeding = () => { };

    public CodeServerTests()
    {
        config = Path.Combine(brain.Root, "claude-config");
        project = Path.Combine(brain.Root, "lens");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(Path.Combine(config, "agents"));
        File.WriteAllText(Path.Combine(config, "agents", "brain.md"), "---\nname: brain\n---\n");
        File.WriteAllText(Path.Combine(config, "agents", "immediate.md"), "---\nname: immediate\n---\n");
        publish = Path.Combine(brain.Root, "publish");
        Directory.CreateDirectory(publish);
        File.WriteAllText(Path.Combine(publish, CodeBuild.StampFile), "first\n");
        server = Server();
        serving = server.Serve(stop.Token);
        client = new HttpClient { BaseAddress = new Uri(server.Origin) };
    }

    public void Dispose()
    {
        stop.Cancel();
        serving.Wait(Wait);
        stop.Dispose();
        client.Dispose();
        server.Dispose();
        brain.Dispose();
    }

    [Fact]
    public async Task ACommandStartsASessionOfItsAgentAndTheUsersWordsComeBackAsEvents()
    {
        var token = await Trade(Code(windows[0]));
        using var events = await OpenEvents(token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());

        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "immediate", cwd = project }), key: Key()));
        var id = started.RootElement.GetProperty("id").GetString()!;
        var announced = await NextEvent(reader, "started");
        var said = await Send(HttpMethod.Post, "/api/say", Json(new { session = id, words = "hello" }), token: token, origin: server.Origin);
        var conversation = await Conversation(token, id, 2);

        Assert.Equal(id, announced);
        Assert.Equal("immediate", started.RootElement.GetProperty("agent").GetString());
        Assert.Equal(project, started.RootElement.GetProperty("cwd").GetString());
        Assert.Equal(HttpStatusCode.NoContent, said.StatusCode);
        var args = Args();
        Assert.Equal(["-p", "--agent", "immediate"], args[..3]);
        Assert.Contains("stream-json", args);
        Assert.Equal(["--session-id", id], args[^2..]);
        Assert.Contains("\"content\":\"hello\"", await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")), StringComparison.Ordinal);
        Assert.Equal(["user:hello", "text:I heard you"], conversation);
    }

    /// <summary>
    /// The text the agent writes comes with the conversation as claude streams it, and once the transcript holds its
    /// message it is that message's event only, so it shows once.
    /// </summary>
    [Fact]
    public async Task TheTextTheAgentWritesComesLiveUntilTheTranscriptHoldsIt()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();

        await Say(token, id, "stream it");
        var writing = await ConversationWhen(token, id, (read) => read.TryGetProperty("live", out _));
        await Say(token, id, "go on");
        var written = await ConversationWhen(token, id, (read) => !read.TryGetProperty("live", out _) && read.GetProperty("events").GetArrayLength() == 2);

        Assert.Equal("I hear you", writing.RootElement.GetProperty("live").GetString());
        Assert.Equal(["user:hello"], Kinds(writing));
        Assert.Equal(["user:hello", "text:I hear you"], Kinds(written));
        Assert.Contains("--include-partial-messages", Args());
    }

    /// <summary>
    /// An interrupt ends the agent's turn over claude's input, as Esc does in a terminal: the turn ends with no problem
    /// shown, the conversation says it was interrupted, and the session stays for the next words. With no turn running,
    /// claude is sent nothing. Each interrupt is a record.
    /// </summary>
    [Fact]
    public async Task AnInterruptEndsTheTurnAndTheSessionStaysForTheNextWords()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "stream it");
        await ConversationWhen(token, id, (read) => read.TryGetProperty("live", out _));

        var interrupted = await Send(HttpMethod.Post, "/api/interrupt", Json(new { session = id }), token: token, origin: server.Origin);
        var after = await ConversationWhen(token, id, (read) => Kinds(read).Contains("interrupted:") && !read.TryGetProperty("live", out _));
        var stopped = await SessionWhen(token, id, (session) => !session.GetProperty("working").GetBoolean());
        await Say(token, id, "hello again");
        var next = await ConversationWhen(token, id, (read) => Kinds(read).Contains("text:I heard you"));
        await SessionWhen(token, id, (session) => !session.GetProperty("working").GetBoolean());
        var idle = await Send(HttpMethod.Post, "/api/interrupt", Json(new { session = id }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.NoContent, interrupted.StatusCode);
        Assert.Equal(["user:hello", "interrupted:"], Kinds(after));
        Assert.True(stopped.GetProperty("alive").GetBoolean());
        Assert.False(stopped.TryGetProperty("problem", out _));
        Assert.Contains("text:I heard you", Kinds(next));
        Assert.Equal(HttpStatusCode.NoContent, idle.StatusCode);
        var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said"));
        var control = Assert.Single(said, (line) => line.Contains("control_request", StringComparison.Ordinal));
        Assert.Contains("\"subtype\":\"interrupt\"", control, StringComparison.Ordinal);
        var records = string.Concat(Directory.EnumerateFiles(Path.Combine(brain.Home, "perf", CodeServer.Folder)).Select(File.ReadAllText));
        Assert.Contains("\"name\":\"interrupt\"", records, StringComparison.Ordinal);
    }

    /// <summary>
    /// Words said after an interrupt and before its turn ended are queued, as claude queues them: the interrupted turn
    /// ends with no problem shown, and the words run as a turn of their own, during which the session works.
    /// </summary>
    [Fact]
    public async Task WordsSaidBeforeTheInterruptedTurnEndedRunAsTheNextTurnWithNoProblem()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "stream slow");
        await ConversationWhen(token, id, (read) => read.TryGetProperty("live", out _));

        await Send(HttpMethod.Post, "/api/interrupt", Json(new { session = id }), token: token, origin: server.Origin);
        await Say(token, id, "and then this");
        var next = await ConversationWhen(token, id, (read) => Kinds(read).Contains("text:I heard you"));
        var ended = await SessionWhen(token, id, (session) => !session.GetProperty("working").GetBoolean());

        Assert.Equal(["user:hello", "interrupted:", "user:hello", "text:I heard you"], Kinds(next));
        Assert.False(ended.TryGetProperty("problem", out _));
        Assert.Contains("and then this", await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")), StringComparison.Ordinal);
    }

    /// <summary>
    /// An interrupt claude refuses leaves the turn running and says why, so the user can stop it again; the turn then
    /// ends as any other.
    /// </summary>
    [Fact]
    public async Task AnInterruptClaudeRefusesSaysWhyAndTheTurnGoesOn()
    {
        var token = await Trade(Code(windows[0]));
        var id = await StartSession();
        await Say(token, id, "stream refuse");
        await ConversationWhen(token, id, (read) => read.TryGetProperty("live", out _));

        await Send(HttpMethod.Post, "/api/interrupt", Json(new { session = id }), token: token, origin: server.Origin);
        var refused = await SessionWhen(token, id, (session) => session.TryGetProperty("problem", out _));
        await Say(token, id, "go on");
        var written = await ConversationWhen(token, id, (read) => Kinds(read).Contains("text:I hear you"));
        var ended = await SessionWhen(token, id, (session) => !session.GetProperty("working").GetBoolean());

        Assert.Equal("the interrupt was refused: not now", refused.GetProperty("problem").GetString());
        Assert.True(refused.GetProperty("working").GetBoolean());
        Assert.DoesNotContain("interrupted:", Kinds(written));
        Assert.False(ended.TryGetProperty("problem", out _));
    }

    /// <summary>
    /// An agent the user stopped has ended: Claude Code marks its meta <c>stoppedByUser</c> and does not always write
    /// <c>finished</c>. It lists as stopped, and the session's row counts it no longer running.
    /// </summary>
    [Fact]
    public async Task AnAgentTheUserStoppedHasEndedThoughItsMetaSaysNoFinished()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""", """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_5","name":"Agent","input":{"subagent_type":"builder","description":"Build","run_in_background":true}}]}}""");
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        await File.WriteAllTextAsync(Path.Combine(agents, "agent-c1.meta.json"), """{"agentType":"builder","toolUseId":"toolu_5","stoppedByUser":true}""");
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var listed = await Json(await Send(HttpMethod.Get, $"/api/sent?session={id}", token: token));
        var sessions = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        var stopped = listed.RootElement.GetProperty("agents").EnumerateArray().Single();
        Assert.Equal(("toolu_5", "stopped"), (stopped.GetProperty("tool").GetString(), stopped.GetProperty("state").GetString()));
        Assert.Equal(0, sessions.RootElement.GetProperty("sessions")[0].GetProperty("agents").GetInt32());
    }

    [Fact]
    public async Task ASessionMovedInRunsWhereItRanAndShowsItsHistoryAtOnce()
    {
        var id = Guid.NewGuid().ToString("D");
        var ran = Path.Combine(brain.Root, "elsewhere");
        Directory.CreateDirectory(ran);
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{ran}}}","message":{"role":"user","content":"what is here?"}}""", """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"hm"},{"type":"text","text":"A lens."}]}}""");
        var token = await Trade(Code(windows[0]));

        var started = await Json(await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", resume = id }), token: token, origin: server.Origin));
        var conversation = await Conversation(token, id, 2);
        var again = await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", resume = id }), token: token, origin: server.Origin);

        Assert.Equal(id, started.RootElement.GetProperty("id").GetString());
        Assert.Equal(ran, started.RootElement.GetProperty("cwd").GetString());
        Assert.Equal(["user:what is here?", "text:A lens."], conversation);
        var args = Args();
        Assert.Equal(["--resume", id], args[^2..]);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AnAgentASessionSentReadsFromItsOwnTranscript()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""", """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_7","name":"Agent","input":{"subagent_type":"builder","description":"Build the lens","prompt":"..."}}]}}""");
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        await File.WriteAllTextAsync(Path.Combine(agents, "agent-a1.meta.json"), """{"agentType":"builder","toolUseId":"toolu_7"}""");
        await File.WriteAllTextAsync(Path.Combine(agents, "agent-a1.jsonl"), """{"type":"user","isSidechain":true,"message":{"role":"user","content":"Build the lens."}}""" + "\n" + """{"type":"assistant","message":{"content":[{"type":"text","text":"Built."}]}}""" + "\n");
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var session = await Conversation(token, id, 2);
        var sent = await Json(await Send(HttpMethod.Get, $"/api/subagent?session={id}&tool=toolu_7&from=0", token: token));
        var none = await Json(await Send(HttpMethod.Get, $"/api/subagent?session={id}&tool=toolu_8&from=0", token: token));

        Assert.Equal(["user:build it", "agent:builder"], session);
        Assert.Equal(["user:Build the lens.", "text:Built."], Kinds(sent));
        Assert.False(none.RootElement.GetProperty("found").GetBoolean());
    }

    /// <summary>
    /// An agent sent in the background gives back its call at once, as Claude Code's open agent sends every builder and
    /// validator; it runs until its meta says it finished, whatever its call's result says.
    /// </summary>
    [Fact]
    public async Task AnAgentSentInTheBackgroundRunsUntilItsMetaSaysItFinished()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""", """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_9","name":"Agent","input":{"subagent_type":"builder","description":"Build","run_in_background":true}}]}}""", """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_9","content":"Async agent launched successfully."}]}}""");
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        var meta = Path.Combine(agents, "agent-b1.meta.json");
        await File.WriteAllTextAsync(meta, """{"agentType":"builder","toolUseId":"toolu_9","requestShape":"background"}""");
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var conversation = await Json(await Send(HttpMethod.Get, $"/api/conversation?session={id}&from=0", token: token));
        var running = await Json(await Send(HttpMethod.Get, $"/api/sent?session={id}", token: token));
        await File.WriteAllTextAsync(meta, """{"agentType":"builder","toolUseId":"toolu_9","requestShape":"background","finished":true}""");
        var finished = await Json(await Send(HttpMethod.Get, $"/api/sent?session={id}", token: token));

        Assert.Contains("agent:builder", Kinds(conversation));
        Assert.Contains("result:Async agent launched successfully.", Kinds(conversation));
        Assert.Equal("running", running.RootElement.GetProperty("agents")[0].GetProperty("state").GetString());
        Assert.Equal("finished", finished.RootElement.GetProperty("agents")[0].GetProperty("state").GetString());
    }

    /// <summary>
    /// A session's agents list in the order it sent them, each with its kind, its state and how long it ran; the validator
    /// joins the work of the builder its brief names, a readiness check says its verdict, and the session's row counts
    /// the agents that still run.
    /// </summary>
    [Fact]
    public async Task ASessionsAgentsListInTheOrderSentWithTheValidatorInItsBuildersWork()
    {
        var id = await SentThree();
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var listed = await Json(await Send(HttpMethod.Get, $"/api/sent?session={id}", token: token));
        var sessions = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        var agents = listed.RootElement.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(["ready", "builder", "validator"], agents.Select((one) => one.GetProperty("kind").GetString()!));
        Assert.Equal(["finished", "finished", "running"], agents.Select((one) => one.GetProperty("state").GetString()!));
        Assert.Equal("READY", agents[0].GetProperty("verdict").GetString());
        Assert.Equal(["b1", "b1"], agents[1..].Select((one) => one.GetProperty("work").GetString()!));
        Assert.Equal("Build the lens", agents[1].GetProperty("description").GetString());
        Assert.True(agents[1].TryGetProperty("ended", out _));
        Assert.False(agents[2].TryGetProperty("ended", out _));
        Assert.Equal(1, sessions.RootElement.GetProperty("sessions")[0].GetProperty("agents").GetInt32());
    }

    /// <summary>A validator's view carries its brief and the trail of the work: the builder's report, the round of findings and the builder's answer with its commit.</summary>
    [Fact]
    public async Task AValidatorsViewTellsTheTrailOfTheWork()
    {
        var id = await SentThree();
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var view = await Json(await Send(HttpMethod.Get, $"/api/sent-agent?session={id}&tool=toolu_v", token: token));
        var none = await Send(HttpMethod.Get, $"/api/sent-agent?session={id}&tool=toolu_x", token: token);
        var ready = await Json(await Send(HttpMethod.Get, $"/api/sent-agent?session={id}&tool=toolu_r", token: token));

        Assert.Equal("READY", ready.RootElement.GetProperty("sent").GetProperty("verdict").GetString());
        Assert.Equal("One note: the brief names no repo.", ready.RootElement.GetProperty("questions").GetString());
        var root = view.RootElement;
        Assert.StartsWith("Validate the lens. Builder agent ID: b1.", root.GetProperty("brief").GetString(), StringComparison.Ordinal);
        Assert.Equal("SendMessage b1", root.GetProperty("sent").GetProperty("now").GetString());
        var steps = root.GetProperty("trail").GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["report", "findings", "answer"], steps.Select((one) => one.GetProperty("kind").GetString()!));
        Assert.Equal(["Built the lens.", "1. The lens has no test.", "Tested in 1234abc."], steps.Select((one) => one.GetProperty("text").GetString()!));
        Assert.Equal("1234abc", steps[2].GetProperty("commits")[0].GetProperty("hash").GetString());
        Assert.Equal([0, 1, 1], steps.Select((one) => one.GetProperty("round").GetInt32()));
        Assert.False(root.GetProperty("trail").GetProperty("signals").GetProperty("read").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
    }

    /// <summary>
    /// The window in a real browser: the agents a session sent show beside its conversation, and a validator opens on the
    /// trail of its work, its round's findings drawn as markdown, with its conversation one click away.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsASessionsAgentsAndAValidatorsTrail()
    {
        var id = await SentThree().ConfigureAwait(false);
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.code-agents .agent-row').length", "3").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session-agents')?.textContent", "1 agent running").ConfigureAwait(false);

            await window.Eval("document.querySelector('.code-agents .agent-row[data-kind=\"validator\"]').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.trail-step').length", "3").ConfigureAwait(false);
            await window.Until("document.querySelector('.trail-step[data-kind=\"findings\"] .prose li')?.textContent.trim()", "The lens has no test.").ConfigureAwait(false);
            await window.Eval("document.querySelectorAll('.code-head .segmented button')[1].click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.agent-message[data-way=\"sent\"] .prose')?.textContent.trim()", "The lens has no test.").ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a session that sent three agents, a readiness check that answered READY, a builder that reported and
    /// answered a round with a commit, and its validator, still running, whose brief names the builder; its id.
    /// </summary>
    private async Task<string> SentThree()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""", Call("toolu_r", "brain-ready", "Check the brief"), Call("toolu_b", "builder", "Build the lens"), Call("toolu_v", "validator", "Validate the lens"));
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        await Agent(agents, "r1", """{"agentType":"brain-ready","toolUseId":"toolu_r","description":"Check the brief","finished":true}""", Brief(0, "Is this ready?"), Said(1, "READY. One note: the brief names no repo.")).ConfigureAwait(false);
        await Agent(agents, "b1", """{"agentType":"builder","toolUseId":"toolu_b","description":"Build the lens","worktreePath":"/nowhere/b1","finished":true}""", Brief(2, "Build the lens."), Handback(4, "Built the lens."), Peer(7, "v1", "1. The lens has no test."), Bash(8, "c1"), BashResult(9, "c1", "[worktree-agent-b1 1234abc] Test the lens"), Handback(10, "Tested in 1234abc.")).ConfigureAwait(false);
        await Agent(agents, "v1", """{"agentType":"validator","toolUseId":"toolu_v","description":"Validate the lens"}""", Brief(5, "Validate the lens. Builder agent ID: b1."), Message(6, "b1", "1. The lens has no test.")).ConfigureAwait(false);
        return id;
    }

    private static async Task Agent(string folder, string agent, string meta, params string[] lines)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, $"agent-{agent}.meta.json"), meta).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(folder, $"agent-{agent}.jsonl"), string.Join('\n', lines) + "\n").ConfigureAwait(false);
    }

    private static string Call(string tool, string type, string description) =>
        $$$"""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"{{{tool}}}","name":"Agent","input":{"subagent_type":"{{{type}}}","description":"{{{description}}}","run_in_background":true}}]}}""";

    private static string Stamp(int second) => $"2026-11-06T19:00:{second:00}Z";

    private static string Brief(int second, string text) =>
        $$$"""{"type":"user","timestamp":"{{{Stamp(second)}}}","message":{"role":"user","content":"{{{text}}}"}}""";

    private static string Said(int second, string text) =>
        $$$"""{"type":"assistant","timestamp":"{{{Stamp(second)}}}","message":{"content":[{"type":"text","text":"{{{text}}}"}]}}""";

    private static string Handback(int second, string text) =>
        $$$"""{"type":"assistant","timestamp":"{{{Stamp(second)}}}","message":{"content":[{"type":"tool_use","id":"h{{{second}}}","name":"SubagentHandback","input":{"message":"{{{text}}}"}}]}}""";

    private static string Message(int second, string to, string text) =>
        $$$"""{"type":"assistant","timestamp":"{{{Stamp(second)}}}","message":{"content":[{"type":"tool_use","id":"m{{{second}}}","name":"SendMessage","input":{"to":"{{{to}}}","message":"{{{text}}}"}}]}}""";

    private static string Peer(int second, string from, string text) =>
        $$$"""{"type":"user","isMeta":true,"timestamp":"{{{Stamp(second)}}}","origin":{"kind":"peer","from":"{{{from}}}","name":"validator","body":"{{{text}}}"},"message":{"role":"user","content":"Another Claude session sent a message"}}""";

    private static string Bash(int second, string tool) =>
        $$$"""{"type":"assistant","timestamp":"{{{Stamp(second)}}}","message":{"content":[{"type":"tool_use","id":"{{{tool}}}","name":"Bash","input":{"command":"git commit -m x","description":"Commit"}}]}}""";

    private static string BashResult(int second, string tool, string text) =>
        $$$"""{"type":"user","timestamp":"{{{Stamp(second)}}}","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"{{{tool}}}","content":"{{{text}}}"}]}}""";

    [Fact]
    public async Task ClosingASessionStopsItsClaudeAndTakesItFromTheList()
    {
        var token = await Trade(Code(windows[0]));
        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()));
        var id = started.RootElement.GetProperty("id").GetString()!;
        var before = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        var closed = await Send(HttpMethod.Post, "/api/close", Json(new { session = id }), token: token, origin: server.Origin);
        var after = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        Assert.True(before.RootElement.GetProperty("sessions")[0].GetProperty("alive").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.Equal(0, after.RootElement.GetProperty("sessions").GetArrayLength());
        Assert.DoesNotContain(Brain.Children.ClaudeRuns.Now(), (run) => run.Place?.Id == id);
    }

    [Theory]
    [InlineData("../brain", null)]
    [InlineData("brain", "not-a-session")]
    public async Task AStartThatNamesNoAgentOrNoSessionIsRefused(string agent, string? resume)
    {
        var refused = await Send(HttpMethod.Post, "/api/key/start", Json(new { agent, cwd = project, resume }), key: Key());
        var keyless = await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, keyless.StatusCode);
    }

    [Fact]
    public async Task AFolderThatIsNotThereOrDiffersFromAMovedSessionsIsRefusedAndATildeIsHome()
    {
        var id = Guid.NewGuid().ToString("D");
        var ran = Path.Combine(brain.Root, "elsewhere");
        Directory.CreateDirectory(ran);
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{ran}}}","message":{"role":"user","content":"hi"}}""");

        var missing = await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = Path.Combine(brain.Root, "nowhere") }), key: Key());
        var differs = await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key());
        var home = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = "~/lens" }), key: Key()));

        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Contains("nowhere", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, differs.StatusCode);
        Assert.Equal(project, home.RootElement.GetProperty("cwd").GetString());
    }

    [Fact]
    public async Task ASessionWhoseClaudeExitsWithoutATranscriptIsEndedAndSaysWhy()
    {
        var path = Path.Combine(brain.Root, "failing");
        await File.WriteAllTextAsync(path, "#!/bin/sh\necho 'no such agent' >&2\nexit 3\n");
        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var sessions = new CodeSessions(path, new ClaudeFiles(config), (_) => { }, (_, _) => { });

        var session = sessions.Start("brain", project, null);
        CodeSessionView view;
        var until = DateTime.UtcNow + Wait;
        do
        {
            await Task.Delay(50);
            view = session.View();
        }
        while (view.Alive && DateTime.UtcNow < until);

        Assert.False(view.Alive);
        Assert.Contains("exit 3", view.Problem, StringComparison.Ordinal);
        Assert.Contains("no such agent", view.Problem, StringComparison.Ordinal);
        Assert.Contains("wrote no transcript", view.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWindowIsOfferedTheAgentFilesAndItsPageComesFromBrain()
    {
        var token = await Trade(Code(windows[0]));

        var agents = await Json(await Send(HttpMethod.Get, "/api/agents", token: token));
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();

        Assert.Equal(["brain", "immediate"], agents.RootElement.GetProperty("agents").EnumerateArray().Select((one) => one.GetString()!).ToList());
        Assert.Contains("<script src=\"/app.js\" type=\"module\"></script>", page, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/components.css\">", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The window in a real browser: a session brain code starts shows in the list and in view, the words written in
    /// its composer reach claude with Enter, and the answer is drawn as markdown under them, with no error on the page.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsASessionTheCommandStartedAndDrawsTheAnswerToTheUsersWords()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "immediate", cwd = project }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session-agent')?.textContent", "immediate").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);

            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'hello'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-said .prose')?.textContent.trim()", "I heard you").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-user .prose')?.textContent.trim()", "hello").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-words').value", string.Empty).ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The window in a real browser: text selected in the conversation shows a Quote button and a plain selection leaves
    /// the composer alone. A click on the button, or Alt+Q, puts the selected lines in at the cursor behind "> " and
    /// leaves the cursor on a fresh line below, in the focused box; a second quote lands under the answer to the first,
    /// and text selected in the information panel quotes the same way. With the file explorer open, Alt+Q quotes nothing.
    /// </summary>
    [WindowFact]
    public async Task TheWindowQuotesSelectedTextIntoTheComposer()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript(
            "q",
            id,
            $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"look around"}}""",
            Use("c1", "Bash", """{"command":"ls /x","description":"List x"}"""),
            Result("c1", "a.cs"),
            Said(1, "I looked."));
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelector('.code-said .prose')?.textContent.trim()", "I looked.").ConfigureAwait(false);
            const string Alt = "document.dispatchEvent(new KeyboardEvent('keydown', { key: 'q', code: 'KeyQ', altKey: true, bubbles: true, cancelable: true }))";
            const string Words = "document.querySelector('.code-words').value";

            await window.Eval("getSelection().selectAllChildren(document.querySelector('.code-said .prose'))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-quote') !== null", "true").ConfigureAwait(false);
            Assert.Equal(string.Empty, await window.Eval(Words).ConfigureAwait(false));

            await window.Eval("{ const box = document.querySelector('.code-words'); box.value = 'before'; box.dispatchEvent(new Event('input')); document.querySelector('.code-quote').click() }").ConfigureAwait(false);
            await window.Until(Words, "before\n> I looked.\n").ConfigureAwait(false);
            await window.Until("document.activeElement === document.querySelector('.code-words')", "true").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-words').selectionStart", "19").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-quote') === null", "true").ConfigureAwait(false);

            await window.Eval("{ const box = document.querySelector('.code-words'); box.setRangeText('my answer\\n', box.selectionStart, box.selectionEnd, 'end'); box.dispatchEvent(new Event('input')) }").ConfigureAwait(false);
            await window.Eval("document.querySelector('.tool-call').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.info-panel-body .tool-detail-code')?.textContent", "$ ls /x").ConfigureAwait(false);
            await window.Eval("document.activeElement.blur(); getSelection().selectAllChildren(document.querySelector('.info-panel-body .tool-detail-code'))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-quote') !== null", "true").ConfigureAwait(false);
            await window.Eval(Alt).ConfigureAwait(false);
            await window.Until(Words, "before\n> I looked.\nmy answer\n> $ ls /x\n").ConfigureAwait(false);

            await window.Eval("document.querySelector('.code-head-actions .button.no').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.palette .stop-confirm') !== null", "true").ConfigureAwait(false);
            await window.Eval("getSelection().selectAllChildren(document.querySelector('.code-said .prose'))").ConfigureAwait(false);
            await window.Eval(Alt).ConfigureAwait(false);
            Assert.Equal("before\n> I looked.\nmy answer\n> $ ls /x\n", await window.Eval(Words).ConfigureAwait(false));
            await window.Eval(PressEsc).ConfigureAwait(false);
            await window.Until("document.querySelector('.palette') === null", "true").ConfigureAwait(false);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'π', code: 'KeyP', altKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.file-explorer') !== null", "true").ConfigureAwait(false);
            await window.Eval("getSelection().selectAllChildren(document.querySelector('.code-said .prose'))").ConfigureAwait(false);
            await window.Eval(Alt).ConfigureAwait(false);
            Assert.Equal("before\n> I looked.\nmy answer\n> $ ls /x\n", await window.Eval(Words).ConfigureAwait(false));
            Assert.Equal("true", await window.Eval("String(document.querySelector('.code-quote') === null)").ConfigureAwait(false));

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The window in a real browser: the text the agent writes shows as it streams, with the stop beside the composer,
    /// and Ctrl+C interrupts the turn: the conversation says so, the text the turn abandoned goes with it, and the stop goes.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsTheTextAsItStreamsAndCtrlCInterruptsTheTurn()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);

            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'stream it'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-said .prose')?.textContent.trim()", "I hear you").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-composer .button.no kbd')?.textContent", "Ctrl+C").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-words').dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-interrupted')?.textContent", "Interrupted").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-composer .button.no') === null", "true").ConfigureAwait(false);

            await window.Until("String(document.querySelectorAll('.code-said').length)", "0").ConfigureAwait(false);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The window in a real browser: while an agent the session sent still runs, Ctrl+C names it and asks first, since the
    /// interrupt would stop it too. Esc keeps it running and sends claude nothing; only the confirm's stop
    /// interrupts the turn.
    /// </summary>
    [WindowFact]
    public async Task WhileAnAgentTheSessionSentRunsTheWindowNamesItAndAsksBeforeItInterrupts()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'stream with an agent'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-said .prose')?.textContent.trim()", "I hear you").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-events .agent-row-state')?.textContent", "running").ConfigureAwait(false);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'c', ctrlKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm .stop-confirm-kind')?.textContent", "builder").ConfigureAwait(false);
            var named = await window.Eval("document.querySelector('.stop-confirm .stop-confirm-what').textContent").ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm') === null", "true").ConfigureAwait(false);
            var kept = await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.said")).ConfigureAwait(false);

            await window.Eval("document.querySelector('.code-composer .button.no').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm') !== null", "true").ConfigureAwait(false);
            await window.Eval("document.querySelector('.stop-confirm .button.no').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-interrupted')?.textContent", "Interrupted").ConfigureAwait(false);
            await window.Until("document.querySelector('.stop-confirm') === null", "true").ConfigureAwait(false);

            Assert.Equal("Build the lens", named);
            Assert.DoesNotContain("control_request", kept, StringComparison.Ordinal);
            var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said")).ConfigureAwait(false);
            Assert.Single(said, (line) => line.Contains("control_request", StringComparison.Ordinal));
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Fact]
    public void ATranscriptLineReadsAsBrainsOwnEventsWithoutThinkingOrTheHarnessNotes()
    {
        var call = """{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"x"},{"type":"text","text":"Reading."},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"ls -la\nmore","description":"List the files"}}]}}""";
        var result = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","is_error":true,"content":[{"type":"text","text":"no such file"}]}]}}""";
        var meta = """{"type":"user","isMeta":true,"message":{"role":"user","content":"Caveat"}}""";
        var command = """{"type":"user","message":{"role":"user","content":"<command-name>/clear</command-name>"}}""";

        var said = Transcript.Events(call);
        var gave = Transcript.Events(result);

        Assert.Equal([CodeEvent.Said, CodeEvent.Tool], said.Select((one) => one.Kind));
        Assert.Equal(("Bash", "List the files", "t1"), (said[1].Name, said[1].Subject, said[1].Id));
        Assert.Equal(new CodeEvent(CodeEvent.Result, Text: "no such file", Id: "t1", Failed: true), gave.Single());
        Assert.Equal("ls -la\nmore", said[1].Input!["command"]);
        Assert.Empty(Transcript.Events(meta));
        Assert.Empty(Transcript.Events(command));
        Assert.Empty(Transcript.Events("{\"type\":\"mode\"}"));
    }

    [Fact]
    public void ATranscriptReadsOnlyWhatWasAddedAndALineOnceItsEndIsWritten()
    {
        var path = Path.Combine(brain.Root, "t.jsonl");
        File.WriteAllText(path, """{"type":"user","message":{"role":"user","content":"one"}}""" + "\n" + """{"type":"assistant","message":{"content":[{"type":"te""");
        var transcript = new Transcript(path);

        var first = transcript.From(0, null);
        File.AppendAllText(path, "xt\",\"text\":\"two\"}]}}\n");
        var second = transcript.From(first.Events.Count, first.Generation);

        Assert.Equal(["one"], first.Events.Select((one) => one.Text));
        Assert.Equal(1, second.From);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(["two"], second.Events.Select((one) => one.Text));
    }

    [Fact]
    public void ATranscriptWrittenAnewIsANewReadingThatStartsFromItsFirstEvent()
    {
        var path = Path.Combine(brain.Root, "t.jsonl");
        File.WriteAllText(path, """{"type":"user","message":{"role":"user","content":"one, and a long line"}}""" + "\n" + """{"type":"user","message":{"role":"user","content":"two"}}""" + "\n");
        var transcript = new Transcript(path);
        var first = transcript.From(0, null);

        File.WriteAllText(path, """{"type":"user","message":{"role":"user","content":"anew"}}""" + "\n");
        var anew = transcript.From(first.Events.Count, first.Generation);
        var other = new Transcript(path).From(1, anew.Generation);

        Assert.Equal(2, first.Events.Count);
        Assert.Equal(0, anew.From);
        Assert.NotEqual(first.Generation, anew.Generation);
        Assert.Equal(["anew"], anew.Events.Select((one) => one.Text));
        Assert.Equal(0, other.From);
        Assert.NotEqual(anew.Generation, other.Generation);
    }

    /// <summary>The command reaches the running server with its body whole: the agent it names, in its folder.</summary>
    [Fact]
    public void TheCommandStartsTheAgentItNamesInTheFolderItNamesInTheRunningServer()
    {
        brain.Cwd = brain.Root;

        var (code, output, errors) = brain.Run("code", "immediate", project);

        Assert.True(code == 0, errors);
        Assert.StartsWith($"brain code: started immediate in {project}", output, StringComparison.Ordinal);
        Assert.Equal(["-p", "--agent", "immediate"], Args()[..3]);
        Assert.Equal(project, File.ReadAllText(Path.Combine(brain.Root, "claude.cwd")).TrimEnd('\n'));
    }

    /// <summary>
    /// Each session carries the PermissionRequest hook in its own <c>--settings</c>, so it applies to brain code's
    /// sessions only: the server's own binary, <c>code --permission</c> with the session's id, waiting a week.
    /// </summary>
    [Fact]
    public async Task ASessionCarriesThePermissionHookInItsOwnSettings()
    {
        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()));
        var id = started.RootElement.GetProperty("id").GetString()!;

        var args = Args();
        var settings = args[Array.IndexOf(args, "--settings") + 1];
        using var hooks = JsonDocument.Parse(settings);
        var hook = hooks.RootElement.GetProperty("hooks").GetProperty("PermissionRequest")[0].GetProperty("hooks")[0];

        Assert.Equal("command", hook.GetProperty("type").GetString());
        Assert.Equal($"'{Environment.ProcessPath}' code --permission {id}", hook.GetProperty("command").GetString());
        Assert.Equal(7 * 24 * 60 * 60, hook.GetProperty("timeout").GetInt32());
    }

    /// <summary>
    /// The hook hands a request to the server and waits: the session lists it, with its tool and input, until the user
    /// denies it in the window, and his words go back to the agent in the hook's decision. Each is a record.
    /// </summary>
    [Fact]
    public async Task APermissionRequestWaitsInItsSessionUntilTheUserAnswersInTheWindow()
    {
        var token = await Trade(Code(windows[0]));
        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()));
        var id = started.RootElement.GetProperty("id").GetString()!;

        var hook = Task.Run(() => brain.RunWithInput(HookInput("Bash", new { command = "git push", description = "Push" }), "code", "--permission", id));
        var asked = await WaitingAsk(token, id, hook);
        var answered = await Send(HttpMethod.Post, "/api/answer", Json(new { session = id, ask = asked.GetProperty("id").GetString(), allow = false, words = "not yet" }), token: token, origin: server.Origin);
        var (code, output, _) = await hook.WaitAsync(Wait);
        var after = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token));

        Assert.Equal("Bash", asked.GetProperty("tool").GetString());
        Assert.Equal("git push", asked.GetProperty("input").GetProperty("command").GetString());
        Assert.False(asked.TryGetProperty("agent", out _));
        Assert.Equal(HttpStatusCode.NoContent, answered.StatusCode);
        Assert.Equal(0, code);
        var decision = Decision(output);
        Assert.Equal("deny", decision.GetProperty("behavior").GetString());
        Assert.EndsWith(": not yet", decision.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, after.RootElement.GetProperty("sessions")[0].GetProperty("asks").GetArrayLength());
        var records = string.Concat(Directory.EnumerateFiles(Path.Combine(brain.Home, "perf", CodeServer.Folder)).Select(File.ReadAllText));
        Assert.Contains("\"name\":\"ask\"", records, StringComparison.Ordinal);
        Assert.Contains("Bash deny", records, StringComparison.Ordinal);
    }

    /// <summary>A subagent's request says which subagent asked and names the Agent call that sent it; an allow lets it run.</summary>
    [Fact]
    public async Task ASubagentsRequestNamesTheCallThatSentItAndAnAllowLetsItRun()
    {
        var id = Guid.NewGuid().ToString("D");
        WriteTranscript("q", id, $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""");
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        await File.WriteAllTextAsync(Path.Combine(agents, "agent-a7.meta.json"), """{"agentType":"builder","toolUseId":"toolu_7"}""");
        var token = await Trade(Code(windows[0]));
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin);

        var hook = Task.Run(() => brain.RunWithInput(HookInput("Write", new { file_path = "/tmp/x", content = "hi" }, agentId: "a7", agentType: "builder"), "code", "--permission", id));
        var asked = await WaitingAsk(token, id, hook);
        await Send(HttpMethod.Post, "/api/answer", Json(new { session = id, ask = asked.GetProperty("id").GetString(), allow = true }), token: token, origin: server.Origin);
        var (_, output, _) = await hook.WaitAsync(Wait);

        Assert.Equal("builder", asked.GetProperty("agent").GetString());
        Assert.Equal("a7", asked.GetProperty("agentId").GetString());
        Assert.Equal("toolu_7", asked.GetProperty("call").GetString());
        var decision = Decision(output);
        Assert.Equal("allow", decision.GetProperty("behavior").GetString());
        Assert.False(decision.TryGetProperty("message", out _));
    }

    /// <summary>Closing a session answers the request that waits in it deny, so its hook never hangs.</summary>
    [Fact]
    public async Task ClosingASessionDeniesTheRequestThatWaits()
    {
        var token = await Trade(Code(windows[0]));
        var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()));
        var id = started.RootElement.GetProperty("id").GetString()!;

        var hook = Task.Run(() => brain.RunWithInput(HookInput("Bash", new { command = "ls" }), "code", "--permission", id));
        await WaitingAsk(token, id, hook);
        await Send(HttpMethod.Post, "/api/close", Json(new { session = id }), token: token, origin: server.Origin);
        var (_, output, _) = await hook.WaitAsync(Wait);
        var late = await Send(HttpMethod.Post, "/api/answer", Json(new { session = id, ask = "ask-1", allow = true }), token: token, origin: server.Origin);

        var decision = Decision(output);
        Assert.Equal("deny", decision.GetProperty("behavior").GetString());
        Assert.Contains("closed", decision.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    /// <summary>With no brain code window, or a request it cannot read, the hook denies at once.</summary>
    [Fact]
    public void WithNoWindowOrNoRequestTheHookDeniesAtOnce()
    {
        using var lone = new TempBrain();

        var (code, output, _) = lone.RunWithInput(HookInput("Bash", new { command = "ls" }), "code", "--permission", Guid.NewGuid().ToString("D"));
        var (_, unread, _) = lone.RunWithInput("not json", "code", "--permission", Guid.NewGuid().ToString("D"));

        Assert.Equal(0, code);
        Assert.Contains("not open", Decision(output).GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("deny", Decision(unread).GetProperty("behavior").GetString());
    }

    /// <summary>
    /// The window in a real browser: a request shows in its session's conversation as the permission request, its
    /// command as code, the session's row says it asks, and Allow answers the hook.
    /// </summary>
    [WindowFact]
    public async Task TheWindowShowsAPermissionRequestAndAllowAnswersTheHook()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "immediate", cwd = project }), key: Key()).ConfigureAwait(false)).ConfigureAwait(false);
            var id = started.RootElement.GetProperty("id").GetString()!;
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);

            var hook = Task.Run(() => brain.RunWithInput(HookInput("Bash", new { command = "git push origin lens" }), "code", "--permission", id));
            await window.Until("document.querySelector('.permission-request .permission-request-code')?.textContent", "git push origin lens").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session-state')?.textContent", "asks you").ConfigureAwait(false);
            await window.Eval("document.querySelector('.permission-request .button.yes').click()").ConfigureAwait(false);
            var (_, output, _) = await hook.WaitAsync(Wait).ConfigureAwait(false);
            await window.Until("document.querySelector('.permission-request') === null", "true").ConfigureAwait(false);

            Assert.Equal("allow", Decision(output).GetProperty("behavior").GetString());
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The JSON a PermissionRequest hook reads on stdin, as the real claude writes it.</summary>
    private string HookInput(string tool, object input, string? agentId = null, string? agentType = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["session_id"] = Guid.NewGuid().ToString("D"),
            ["cwd"] = project,
            ["permission_mode"] = "default",
            ["hook_event_name"] = "PermissionRequest",
            ["tool_name"] = tool,
            ["tool_input"] = input,
            ["agent_type"] = agentType ?? "brain",
        };
        if (agentId is not null)
        {
            fields["agent_id"] = agentId;
        }

        return JsonSerializer.Serialize(fields);
    }

    /// <summary>The decision of the hook's output.</summary>
    private static JsonElement Decision(string output)
    {
        using var json = JsonDocument.Parse(output);
        var specific = json.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("PermissionRequest", specific.GetProperty("hookEventName").GetString());
        return specific.GetProperty("decision").Clone();
    }

    /// <summary>
    /// The first permission request that waits in session <paramref name="id"/>, once there is one; it fails after a
    /// wait, or with what the hook said when it ended before its request came.
    /// </summary>
    private async Task<JsonElement> WaitingAsk(string token, string id, Task<(int Code, string Out, string Err)> hook)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (hook.IsCompleted)
            {
                var (code, output, errors) = await hook.ConfigureAwait(false);
                throw new InvalidOperationException($"the hook ended at once, exit {code}: {output}{errors}");
            }

            using var listed = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            foreach (var session in listed.RootElement.GetProperty("sessions").EnumerateArray())
            {
                if (session.GetProperty("id").GetString() == id && session.GetProperty("asks").GetArrayLength() > 0)
                {
                    return session.GetProperty("asks")[0].Clone();
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new InvalidOperationException("no permission request came to session " + id);
    }

    /// <summary>The arguments the stand-in for claude was started with, once it wrote them; it fails after a wait.</summary>
    private string[] Args()
    {
        var path = Path.Combine(brain.Root, "claude.args");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllText(path).EndsWith('\n'), Wait), "the stand-in for claude never started");
        return File.ReadAllLines(path);
    }

    /// <summary>Writes the transcript of session <paramref name="id"/> in the project folder <paramref name="folder"/> of the config.</summary>
    private void WriteTranscript(string folder, string id, params string[] lines)
    {
        var dir = Path.Combine(config, "projects", folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".jsonl"), string.Join('\n', lines) + "\n");
    }

    /// <summary>The conversation of a session as kind:text, once it holds <paramref name="count"/> events; it fails after a wait.</summary>
    private async Task<List<string>> Conversation(string token, string id, int count)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var read = await Json(await Send(HttpMethod.Get, $"/api/conversation?session={id}&from=0", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            var kinds = Kinds(read);
            if (kinds.Count >= count || DateTime.UtcNow > deadline)
            {
                return kinds;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    private static List<string> Kinds(JsonDocument read) => Kinds(read.RootElement);

    private static List<string> Kinds(JsonElement read) =>
        [.. read.GetProperty("events").EnumerateArray().Select((one) => one.GetProperty("kind").GetString() + ":" + Shown(one))];

    private static string? Shown(JsonElement one)
    {
        if (one.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        if (one.TryGetProperty("name", out var name))
        {
            return name.GetString();
        }

        return string.Empty;
    }

    private async Task Say(string token, string id, string words)
    {
        var said = await Send(HttpMethod.Post, "/api/say", Json(new { session = id, words }), token: token, origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NoContent, said.StatusCode);
    }

    /// <summary>The whole conversation of a session once <paramref name="until"/> holds of it; it fails after a wait.</summary>
    private async Task<JsonDocument> ConversationWhen(string token, string id, Func<JsonElement, bool> until)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            var read = await Json(await Send(HttpMethod.Get, $"/api/conversation?session={id}&from=0", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            if (until(read.RootElement))
            {
                return read;
            }

            read.Dispose();
            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new InvalidOperationException("the conversation of session " + id + " never came to what was waited for");
    }

    /// <summary>The view of a session in the list once <paramref name="until"/> holds of it; it fails after a wait.</summary>
    private async Task<JsonElement> SessionWhen(string token, string id, Func<JsonElement, bool> until)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            using var listed = await Json(await Send(HttpMethod.Get, "/api/sessions", token: token).ConfigureAwait(false)).ConfigureAwait(false);
            foreach (var session in listed.RootElement.GetProperty("sessions").EnumerateArray())
            {
                if (session.GetProperty("id").GetString() == id && until(session))
                {
                    return session.Clone();
                }
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new InvalidOperationException("session " + id + " never came to what was waited for");
    }

    /// <summary>
    /// A stand-in for claude: it keeps the folder it runs in, the session brain ask would find, its arguments, each start
    /// with its folder and arguments, and each line it is sent; it exits at once on words with "quitnow" in them, and for
    /// each other line writes the user's words and an answer to the transcript of the session id it
    /// was given, as claude does, then says the turn ended. Words with "stream" in them are answered as the text streams,
    /// after an agent it sent in the background when the words name an agent: the partial events of a message, and then,
    /// once the next line came, the whole message in the transcript. When that line is an interrupt, the turn ends as
    /// the real claude ends it: the message abandoned, Claude Code's note, and an error result; with "slow" in the words
    /// that result waits for the next words, which then run as a turn of their own, and with "refuse" claude refuses the
    /// interrupt and the turn goes on to its end once the next line came. Words with "background" in them start a job
    /// that outlives the turn, a shell with a sleep under it, and keep its pid. A control request with no turn running is
    /// read only. A message that carries images goes to the transcript as it was sent, as claude keeps an image a user
    /// sent.
    /// </summary>
    private string StandIn()
    {
        var path = Path.Combine(brain.Root, "claude");
        File.WriteAllText(path, $$$"""
            #!/bin/sh
            here={{{brain.Root}}}
            printf '%s\n' "$PWD" > "$here/claude.cwd"
            printf '%s\n' "$BRAIN_CODE_SESSION" > "$here/claude.session"
            printf '%s\n' "$@" > "$here/claude.args"
            printf '%s %s\n' "$PWD" "$*" >> "$here/claude.runs"
            id=""; prev=""
            for a in "$@"; do
              if [ "$prev" = "--session-id" ] || [ "$prev" = "--resume" ]; then id="$a"; fi
              prev="$a"
            done
            dir="{{{config}}}/projects/p"
            mkdir -p "$dir"
            while IFS= read -r line; do
              printf '%s\n' "$line" >> "$here/claude.said"
              case "$line" in *'"control_request"'*) continue ;; esac
              case "$line" in *quitnow*) exit 3 ;; esac
              printf '{"type":"system","subtype":"init","session_id":"%s"}\n' "$id"
              case "$line" in
                *'"type":"image"'*) printf '%s\n' "$line" >> "$dir/$id.jsonl" ;;
                *) printf '{"type":"user","cwd":"%s","message":{"role":"user","content":"hello"}}\n' "$PWD" >> "$dir/$id.jsonl" ;;
              esac
              case "$line" in
                *stream*)
                  case "$line" in *agent*)
                    printf '{"type":"assistant","message":{"id":"msg_0","content":[{"type":"tool_use","id":"toolu_1","name":"Agent","input":{"subagent_type":"builder","description":"Build the lens","run_in_background":true}}]}}\n' >> "$dir/$id.jsonl"
                    ;;
                  esac
                  echo '{"type":"stream_event","event":{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","content":[]}},"parent_tool_use_id":null}'
                  echo '{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}},"parent_tool_use_id":null}'
                  echo '{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"I hear"}},"parent_tool_use_id":null}'
                  echo '{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" you"}},"parent_tool_use_id":null}'
                  IFS= read -r next
                  printf '%s\n' "$next" >> "$here/claude.said"
                  case "$next" in
                    *'"interrupt"'*)
                      case "$line" in
                        *refuse*)
                          echo '{"type":"control_response","response":{"subtype":"error","request_id":"interrupt-1","error":"not now"}}'
                          IFS= read -r next
                          printf '%s\n' "$next" >> "$here/claude.said"
                          ;;
                        *)
                          echo '{"type":"control_response","response":{"subtype":"success","request_id":"interrupt-1","response":{"still_queued":[]} } }'
                          case "$line" in *slow*)
                            IFS= read -r queued
                            printf '%s\n' "$queued" >> "$here/claude.said"
                            ;;
                          esac
                          printf '{"type":"user","message":{"role":"user","content":[{"type":"text","text":"[Request interrupted by user]"}]}}\n' >> "$dir/$id.jsonl"
                          echo '{"type":"result","subtype":"error_during_execution","is_error":true,"terminal_reason":"aborted_streaming"}'
                          case "$line" in *slow*)
                            printf '{"type":"system","subtype":"init","session_id":"%s"}\n' "$id"
                            printf '{"type":"user","cwd":"%s","message":{"role":"user","content":"hello"}}\n' "$PWD" >> "$dir/$id.jsonl"
                            printf '{"type":"assistant","message":{"content":[{"type":"text","text":"I heard you"}]}}\n' >> "$dir/$id.jsonl"
                            echo '{"type":"result","subtype":"success"}'
                            ;;
                          esac
                          continue
                          ;;
                      esac
                      ;;
                  esac
                  printf '{"type":"assistant","message":{"id":"msg_1","content":[{"type":"text","text":"I hear you"}]}}\n' >> "$dir/$id.jsonl"
                  ;;
                *background*)
                  sh -c 'sleep 300 & wait' </dev/null >/dev/null 2>&1 &
                  printf '%s\n' "$!" > "$here/claude.job"
                  printf '{"type":"assistant","message":{"content":[{"type":"text","text":"I heard you"}]}}\n' >> "$dir/$id.jsonl"
                  ;;
                *)
                  printf '{"type":"assistant","message":{"content":[{"type":"text","text":"I heard you"}]}}\n' >> "$dir/$id.jsonl"
                  ;;
              esac
              echo '{"type":"result","subtype":"success"}'
            done
            """);
        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>A server of this brain on the stand-in, whose build runs from the publish folder; its successor is counted, then <see cref="succeeding"/> runs.</summary>
    private CodeServer Server() =>
        new(brain.Home, brain.Root, StandIn(), new ClaudeFiles(config), TextWriter.Null, Open, (_) => false, clock, new OmarchyTheme(brain.Root, colorCommand: null), new CodeBuild(publish), Succeed);

    /// <summary>The system's clock until <see cref="Leaps"/>; from then on each read is ten seconds past the one before.</summary>
    private sealed class LeapingClock : TimeProvider
    {
        private static readonly TimeSpan Leap = TimeSpan.FromSeconds(10);
        private TimeSpan ahead;

        public bool Leaps { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (Leaps)
            {
                ahead += Leap;
            }

            return DateTimeOffset.UtcNow + ahead;
        }
    }

    private void Succeed()
    {
        Interlocked.Increment(ref successors);
        succeeding();
    }

    private bool Open(string url)
    {
        lock (windows)
        {
            windows.Add(url);
        }

        return true;
    }

    private string Key() => new ServeFiles(brain.Home, CodeServer.Folder).ReadKey()!;

    /// <summary>The launch code in the launch file a window was opened on.</summary>
    private static string Code(string url) => LaunchCode().Match(File.ReadAllText(new Uri(url).LocalPath)).Groups[1].Value;

    private async Task<string> Trade(string code)
    {
        var reply = await Send(HttpMethod.Post, "/api/launch", Json(new { code }), origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> OpenEvents(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add(WindowServer.SessionHeader, token);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    }

    /// <summary>The data of the next event named <paramref name="name"/> on a window's event stream; it fails after a wait.</summary>
    private static async Task<string> NextEvent(StreamReader events, string name)
    {
        using var limit = new CancellationTokenSource(Wait);
        var named = false;
        while (await events.ReadLineAsync(limit.Token).ConfigureAwait(false) is { } line)
        {
            if (line == "event: " + name)
            {
                named = true;
            }
            else if (named && line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line["data: ".Length..];
            }
        }

        throw new InvalidOperationException("the stream ended before " + name);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string? token = null, string? origin = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Add(WindowServer.SessionHeader, token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (key is not null)
        {
            request.Headers.Add(ServeFiles.KeyHeader, key);
        }

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    [GeneratedRegex("#launch=([0-9a-f]+)")]
    private static partial Regex LaunchCode();
}
