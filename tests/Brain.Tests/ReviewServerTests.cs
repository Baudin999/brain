using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Serving;
using Brain.Staging;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// brain serve, over real HTTP on 127.0.0.1: the window's API answers only a session that traded a launch code, the code
/// works once and a second try closes every session, a request for another host or from another origin is refused, a
/// yes from the window lands exactly the write it showed, which <c>land</c> then returns, and there is one window: a new
/// window and a stopping server each tell every older window to close, and a restart moves the window onto its
/// successor in place.
/// </summary>
[Collection(ReviewServerTests.StartsClaude)]
public sealed partial class ReviewServerTests : IDisposable
{
    /// <summary>
    /// The collection of the tests that start claude in this test process. ClaudeRuns keeps every run of the process,
    /// and a brain serve that stops stops all of them, so these tests run one at a time. A workspace's tests run in it
    /// too, so no two of them open a headless browser at once.
    /// </summary>
    internal const string StartsClaude = "claude runs";

    private readonly TempBrain brain = new();
    private readonly ManualTime time = new();
    private readonly List<string> windows = [];

    /// <summary>For each successor the server started: whether its port was still taken then.</summary>
    private readonly List<bool> successors = [];
    private CancellationTokenSource stop;
    private HttpClient client;
    private ReviewServer server;
    private ModelSwitch models;
    private Task serving;
    private int port;

    public ReviewServerTests()
    {
        brain.Ok("add", "--subjects", "test", "global", "meadow", "Ground cover", "--kind", "idea");
        brain.SaysYes = false;
        Start();
    }

    public void Dispose()
    {
        Stop();
        brain.Dispose();
    }

    /// <summary>Starts the window server on a free port, with the Omarchy theme in the test's home, if any.</summary>
    [MemberNotNull(nameof(stop), nameof(client), nameof(server), nameof(serving), nameof(models))]
    private void Start()
    {
        port = FreePort();
        stop = new CancellationTokenSource();
        brain.Claude = Claude();
        var env = brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null);
        models = new ModelSwitch(env, ModelRoot, Mirror, FakeProfile.Shipped, Embedder);
        server = new ReviewServer(env, port, Open, time, new OmarchyTheme(brain.Root, colorCommand: null), Successor, models, GpuWatch.None(time));
        serving = server.Serve(window: false, stop.Token);
        client = new HttpClient { BaseAddress = new Uri(server.Origin) };
    }

    private void Stop()
    {
        stop.Cancel();
        serving.Wait(TimeSpan.FromSeconds(5));
        stop.Dispose();
        client.Dispose();
        models.Dispose();
    }

    /// <summary>Puts an Omarchy theme in place and starts the window server again, as on a machine with Omarchy.</summary>
    private void StartWithOmarchy()
    {
        OmarchyThemeTests.WriteTheme(brain.Root, OmarchyThemeTests.Catppuccin);
        Stop();
        Start();
    }

    [Fact]
    public async Task WithoutASessionTheApiAnswersNothing()
    {
        var id = Stage();

        var pending = await Send(HttpMethod.Get, "/api/pending");
        var answer = await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: "0123", origin: server.Origin);

        Assert.Equal(HttpStatusCode.Unauthorized, pending.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "tea.md")));
    }

    [Fact]
    public async Task ALaunchCodeWorksOnceAndASecondTryClosesEverySession()
    {
        var code = Launch();

        var token = await Trade(code);
        var before = await Send(HttpMethod.Get, "/api/pending", token: token);
        var thief = await Send(HttpMethod.Post, "/api/launch", Json(new { code }), origin: server.Origin);
        var after = await Send(HttpMethod.Get, "/api/pending", token: token);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, thief.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        using var error = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.StartsWith("another process used this window's key", error.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.True(error.RootElement.GetProperty("alarm").GetBoolean());
    }

    [Fact]
    public async Task ALaunchCodeExpiresAndANewWindowRetiresTheOldOne()
    {
        var old = Launch();
        var fresh = Launch();
        var late = Launch();
        time.Advance(WindowKeys.CodeLife + TimeSpan.FromSeconds(1));

        var retired = await Send(HttpMethod.Post, "/api/launch", Json(new { code = old }), origin: server.Origin);
        var expired = await Send(HttpMethod.Post, "/api/launch", Json(new { code = late }), origin: server.Origin);

        Assert.NotEqual(fresh, old);
        Assert.Equal(HttpStatusCode.Forbidden, retired.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
    }

    [Fact]
    public async Task ACallThatFailsAnswersAnErrorWithItsMessageAndIsRecorded()
    {
        TempBrain.CommitAll(brain.Home);
        await File.AppendAllTextAsync(Path.Combine(brain.Home, ".git", "config"), "[core\n");
        var token = await Trade(Launch());

        var sky = await Send(HttpMethod.Get, "/api/sky", token: token);

        Assert.Equal(HttpStatusCode.InternalServerError, sky.StatusCode);
        using var problem = JsonDocument.Parse(await sky.Content.ReadAsStringAsync());
        var said = problem.RootElement.GetProperty("error").GetString();
        Assert.StartsWith("brain serve GET /api/sky failed: git could not ", said, StringComparison.Ordinal);
        Assert.Contains("fatal: bad config line", said, StringComparison.Ordinal);
        var error = Assert.Single(UsageLog.Read(brain.Home).Events, usage => usage.Event == UsageEvent.Error);
        Assert.Equal("brain serve GET /api/sky", error.Failed);
    }

    [Fact]
    public async Task AnotherHostOrAnotherOriginIsRefused()
    {
        var token = await Trade(Launch());
        var id = Stage();
        time.Advance(WindowKeys.Arming);

        var rebound = await Send(HttpMethod.Get, "/api/pending", token: token, host: "evil.example:" + port.ToString(CultureInfo.InvariantCulture));
        var foreign = await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: "http://evil.example");
        var noOrigin = await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token);

        Assert.True(rebound.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound, rebound.StatusCode.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noOrigin.StatusCode);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "tea.md")));
    }

    [Fact]
    public async Task AYesFromTheWindowLandsTheWriteAndLandReturnsIt()
    {
        var token = await Trade(Launch());
        var id = Stage();

        var early = await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var stale = await Send(HttpMethod.Post, "/api/answer", Json(new { id, digest = new string('0', 64), answer = "yes" }), token: token, origin: server.Origin);
        var yes = await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: server.Origin);

        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, yes.StatusCode);
        Assert.Contains("\"verdict\":\"yes\"", await yes.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal($"added global:tea\nlanded {id} on the user's yes\n", brain.Ok("land", id));
    }

    [Fact]
    public async Task ANoWithWordsReachesLandInTheUsersWords()
    {
        var token = await Trade(Launch());
        var id = Stage();
        time.Advance(WindowKeys.Arming);

        var no = await Send(HttpMethod.Post, "/api/answer", Answer(id, "no", "Call it green tea."), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.OK, no.StatusCode);
        Assert.Contains("in their own words:\nCall it green tea.\n", brain.Ok("land", id), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "tea.md")));
    }

    /// <summary>
    /// Every answer shows in the window's Answered list with the write in full, and an undo clicked there takes the yes
    /// back through the pending store, only from an armed window of this origin and only as the window showed it. A
    /// commit the undo could not make is said in the reply.
    /// </summary>
    [Fact]
    public async Task TheWindowListsEveryAnswerAndUndoesAYes()
    {
        var token = await Trade(Launch());
        var id = Stage();
        time.Advance(WindowKeys.Arming);
        await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: server.Origin);

        using var answered = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/answered", token: token)).Content.ReadAsStringAsync());
        var item = answered.RootElement.EnumerateArray().Single(one => one.GetProperty("id").GetString() == id);
        var digest = item.GetProperty("digest").GetString();
        var noOrigin = await Send(HttpMethod.Post, "/api/undo", Json(new { id, digest }), token: token);
        var wrong = await Send(HttpMethod.Post, "/api/undo", Json(new { id, digest = new string('0', 64) }), token: token, origin: server.Origin);
        var undo = await Send(HttpMethod.Post, "/api/undo", Json(new { id, digest }), token: token, origin: server.Origin);
        using var after = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/answered", token: token)).Content.ReadAsStringAsync());

        Assert.Equal("yes", item.GetProperty("verdict").GetString());
        Assert.Equal("new", item.GetProperty("changes")[0].GetProperty("state").GetString());
        Assert.Equal("The user drinks tea", item.GetProperty("changes")[0].GetProperty("node").GetProperty("title").GetString());
        Assert.Equal(0, item.GetProperty("since").GetArrayLength());
        Assert.Equal("global", item.GetProperty("graphs")[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, noOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var reply = await undo.Content.ReadAsStringAsync();
        Assert.Contains("\"undid\":\"yes\"", reply, StringComparison.Ordinal);
        Assert.Contains("\"notCommitted\":\"global:tea lies in no git repo\"", reply, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(brain.Home, "graph", "global", "tea.md")));
        Assert.NotEqual(JsonValueKind.Null, after.RootElement[0].GetProperty("undoneAt").ValueKind);
    }

    /// <summary>Longer than the window waits after a write comes into view before it takes an answer or an undo.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800);

    /// <summary>In a real window, Answered shows the write the user said yes to, and U undoes it once it was read.</summary>
    [WindowFact]
    public async Task TheAnsweredInboxShowsAYesAndUndoesIt()
    {
        var id = Stage();
        var store = new PendingStore(brain.Home);
        store.Answer(id, store.Asked().Single().Digest, yes: true, reason: null);
        var tea = Path.Combine(brain.Home, "graph", "global", "tea.md");

        await InWindow(async window =>
        {
            time.Advance(WindowKeys.Arming);
            await window.Eval("showInbox(':answered')").ConfigureAwait(false);
            await window.Until("String(document.getElementById('main').innerText.includes('You said yes'))", "true").ConfigureAwait(false);
            var main = await window.Eval("document.getElementById('main').innerText").ConfigureAwait(false);
            await Task.Delay(Settle).ConfigureAwait(false);
            await window.Press("u", "KeyU").ConfigureAwait(false);
            await window.Until("String(document.getElementById('queue').innerText.includes('yes, undone'))", "true").ConfigureAwait(false);

            Assert.Contains("The user drinks tea", main, StringComparison.Ordinal);
            Assert.Contains("added global:tea", main, StringComparison.Ordinal);
        }).ConfigureAwait(false);

        Assert.False(File.Exists(tea));
    }

    /// <summary>A node changed since the yes shows as a diff from what the yes left to now, and the undo refuses.</summary>
    [Fact]
    public async Task AnAnswerWhoseNodeChangedSinceShowsWhatChangedAndRefusesTheUndo()
    {
        var token = await Trade(Launch());
        var id = Stage();
        time.Advance(WindowKeys.Arming);
        await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: server.Origin);
        await File.AppendAllTextAsync(Path.Combine(brain.Home, "graph", "global", "tea.md"), "\n");

        using var answered = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/answered", token: token)).Content.ReadAsStringAsync());
        var item = answered.RootElement[0];
        var undo = await Send(HttpMethod.Post, "/api/undo", Json(new { id, digest = item.GetProperty("digest").GetString() }), token: token, origin: server.Origin);

        var since = item.GetProperty("since").EnumerateArray().Single();
        Assert.Equal("global:tea", since.GetProperty("ref").GetString());
        Assert.Equal(since.GetProperty("then").GetString() + "\n", since.GetProperty("now").GetString());
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);
        Assert.Contains("global:tea changed since your yes", await undo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(brain.Home, "graph", "global", "tea.md")));
    }

    [Fact]
    public async Task TheWindowSeesEveryWriteInFull()
    {
        var token = await Trade(Launch());
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "global", "noise2", "Another finding", "--kind", "idea");
        brain.SaysYes = false;
        var id = Stage("--why", "It shows in every session.");
        var edit = Id(brain.RunWithInput("---\nsubjects: [test]\n---\n# Fine ground cover\n\nGrass first.\n", "edit", "meadow").Out);
        Ask(edit);
        var forget = Id(brain.Ok("forget", "noise2"));
        Ask(forget);
        await File.AppendAllTextAsync(Path.Combine(brain.Home, "graph", "global", "meadow.md"), "\n");

        using var pending = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/pending", token: token)).Content.ReadAsStringAsync());
        var items = pending.RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!);

        var tea = items[id].GetProperty("changes")[0];
        Assert.Equal("new", tea.GetProperty("state").GetString());
        Assert.Equal("The user drinks tea", tea.GetProperty("node").GetProperty("title").GetString());
        Assert.Equal("It shows in every session.", tea.GetProperty("node").GetProperty("why").GetString());
        var meadow = items[edit].GetProperty("changes")[0];
        Assert.Equal("rewritten", meadow.GetProperty("state").GetString());
        Assert.True(meadow.GetProperty("stale").GetBoolean());
        Assert.Contains("Grass first.", meadow.GetProperty("after").GetString(), StringComparison.Ordinal);
        Assert.Contains("# Ground cover", meadow.GetProperty("before").GetString(), StringComparison.Ordinal);
        var gone = items[forget].GetProperty("changes")[0];
        Assert.Equal("forgotten", gone.GetProperty("state").GetString());
        Assert.Equal("Another finding", gone.GetProperty("node").GetProperty("title").GetString());
    }

    /// <summary>
    /// A write shows the graphs its changes land in, never the project of the session that staged it: a personal note
    /// staged in a project session is personal's, and a write over two graphs shows under both.
    /// </summary>
    [Fact]
    public async Task EveryWriteShowsTheGraphsItLandsIn()
    {
        var token = await Trade(Launch());
        var global = Stage();
        brain.MakeRepo("studio");
        brain.Init();
        var personal = Id(brain.Ok("add", "--subjects", "test", "personal", "chai", "The user drinks chai", "--kind", "idea"));
        Ask(personal);
        var studio = Id(brain.Ok("add", "--subjects", "test", "project", "cups", "Cups hold tea", "--kind", "idea"));
        Ask(studio);
        const string Both = "0123456789ab";
        var graphs = Path.Combine(brain.Home, "graph");
        await File.WriteAllTextAsync(
            Path.Combine(brain.Home, "cache", "pending", "asked", Both + ".json"),
            $$"""
            {"Id":"{{Both}}","Lines":["added personal:a and global:b"],"Changes":[
              {"Ref":"personal:a","Path":{{Json(Path.Combine(graphs, "personal", "a.md"))}},"Before":null,"After":null},
              {"Ref":"global:b","Path":{{Json(Path.Combine(graphs, "global", "b.md"))}},"Before":null,"After":null}]}
            """);

        using var pending = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/pending", token: token)).Content.ReadAsStringAsync());
        var shown = pending.RootElement.EnumerateArray().ToDictionary(
            item => item.GetProperty("id").GetString()!,
            item => item.GetProperty("graphs").EnumerateArray().Select(graph => graph.GetProperty("name").GetString()).ToList());
        var hue = pending.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetString() == studio).GetProperty("graphs")[0].GetProperty("hue");

        Assert.Equal(["global"], shown[global]);
        Assert.Equal(["personal"], shown[personal]);
        Assert.Equal(["studio"], shown[studio]);
        Assert.Equal(ReviewViews.Hue("studio"), hue.GetInt32());
        Assert.Equal(["personal", "global"], shown[Both]);
    }

    /// <summary>The window can focus on personal, global and every project in projects.tsv, one with nothing waiting too.</summary>
    [Fact]
    public async Task TheWindowCanFocusOnEveryGraph()
    {
        var token = await Trade(Launch());
        brain.MakeRepo("studio");
        brain.Init();

        using var projects = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/projects", token: token)).Content.ReadAsStringAsync());

        Assert.Equal(["personal", "global", "studio"], projects.RootElement.EnumerateArray().Select(graph => graph.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, projects.RootElement[0].GetProperty("hue").ValueKind);
        Assert.Equal(ReviewViews.Hue("studio"), projects.RootElement[2].GetProperty("hue").GetInt32());
    }

    /// <summary>The window reads the evidence from the same metrics brain stats prints, over the days it names, and refuses a day that does not read.</summary>
    [Fact]
    public async Task TheWindowReadsTheEvidenceOverTheDaysItNames()
    {
        var token = await Trade(Launch());
        Directory.CreateDirectory(Path.Combine(brain.Home, "usage"));
        File.WriteAllLines(Path.Combine(brain.Home, "usage", "2026-11.jsonl"),
        [
            """{"at":"2026-11-01T09:00:00.000+01:00","event":"refusal","session":"s-1","hook":"graph-door","tool":"Write","reason":"BLOCKED"}""",
            """{"at":"2026-11-02T09:00:00.000+01:00","event":"refusal","session":"s-1","hook":"graph-door","tool":"Edit","reason":"BLOCKED"}""",
        ]);

        using var all = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/evidence", token: token)).Content.ReadAsStringAsync());
        using var one = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/evidence?since=2026-11-02&until=", token: token)).Content.ReadAsStringAsync());
        var wrong = await Send(HttpMethod.Get, "/api/evidence?since=yesterday", token: token);

        string? Guards(JsonDocument report) =>
            report.RootElement.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("title").GetString() == "Guards").GetProperty("story").GetString();
        Assert.Equal("Without brain's guards, agents would have done 2 things they must not: 2 hand-editing the graph.", Guards(all));
        Assert.Equal("Without brain's guards, agents would have done 1 thing they must not: 1 hand-editing the graph.", Guards(one));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }

    /// <summary>A tool's question reaches only a window's session, and its answer comes only from the window, as a write's does.</summary>
    [Fact]
    public async Task AToolsQuestionIsAnsweredOnlyFromTheWindow()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dispute-source.sh");
        await File.WriteAllTextAsync(Path.Combine(brain.Home, "sources.tsv"), $"disputes\tbash '{fixture}' /repo\n");
        var token = await Trade(Launch());
        var choice = Json(new { source = "disputes", id = "0000019", choice = "agent", words = "Fine." });
        var answer = Path.Combine(brain.Home, "cache", "answers", "disputes", "0000019.json");

        var stranger = await Send(HttpMethod.Get, "/api/questions");
        var listed = await Send(HttpMethod.Get, "/api/questions", token: token);
        var early = await Send(HttpMethod.Post, "/api/answer/question", choice, token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var noOrigin = await Send(HttpMethod.Post, "/api/answer/question", choice, token: token);
        var foreign = await Send(HttpMethod.Post, "/api/answer/question", choice, token: token, origin: "http://evil.example");
        var noSession = await Send(HttpMethod.Post, "/api/answer/question", choice, origin: server.Origin);
        var exists = File.Exists(answer);
        var given = await Send(HttpMethod.Post, "/api/answer/question", choice, token: token, origin: server.Origin);
        var twice = await Send(HttpMethod.Post, "/api/answer/question", choice, token: token, origin: server.Origin);
        var after = await (await Send(HttpMethod.Get, "/api/questions", token: token)).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        using (var json = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
        {
            var source = json.RootElement[0];
            Assert.Equal("disputes", source.GetProperty("name").GetString());
            Assert.Equal("0000019", source.GetProperty("questions")[0].GetProperty("question").GetProperty("id").GetString());
            Assert.Equal(0, source.GetProperty("questions")[0].GetProperty("graphs").GetArrayLength());
            Assert.Equal("screenshot.png", source.GetProperty("questions")[1].GetProperty("question").GetProperty("blocks")[1].GetProperty("src").GetString());
            Assert.Equal(0, source.GetProperty("questions")[2].GetProperty("drift").GetArrayLength());
            var drift = source.GetProperty("questions")[3].GetProperty("drift");
            Assert.Equal(3, drift.GetArrayLength());
            Assert.Equal("[0]", drift[1].GetProperty("at").GetRawText());
            Assert.StartsWith("cyclomatic: base 1", drift[1].GetProperty("says").GetString(), StringComparison.Ordinal);
            var sum = source.GetProperty("questions")[3].GetProperty("sums")[0];
            Assert.Equal(7, sum.GetProperty("weights").GetDecimal());
            Assert.Equal(8, sum.GetProperty("total").GetDecimal());
            Assert.False(sum.GetProperty("addsUp").GetBoolean());
        }

        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        Assert.False(exists);
        Assert.Equal(HttpStatusCode.OK, given.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Contains("\"choice\":\"agent\"", await File.ReadAllTextAsync(answer), StringComparison.Ordinal);
        Assert.DoesNotContain("0000019", after, StringComparison.Ordinal);
    }

    /// <summary>
    /// A conversation is started, talked in, given a new sitting and renamed only from a window's armed session, each
    /// change through the code brain conversation runs. The open agent answers on the sitting's model and effort, its
    /// answer streams into the view, and brain serve, not the agent, writes the trail.
    /// </summary>
    [Fact]
    public async Task AConversationChangesOnlyFromTheWindowAndBrainServeWritesTheTrail()
    {
        var token = await Trade(Launch());
        var start = Json(new { scope = "personal", model = "opus", effort = "high" });

        var stranger = await Send(HttpMethod.Get, "/api/conversations");
        var early = await Send(HttpMethod.Post, "/api/conversation/start", start, token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var noOrigin = await Send(HttpMethod.Post, "/api/conversation/start", start, token: token);
        var foreign = await Send(HttpMethod.Post, "/api/conversation/start", start, token: token, origin: "http://evil.example");
        var noSession = await Send(HttpMethod.Post, "/api/conversation/start", start, origin: server.Origin);
        var none = Directory.Exists(Path.Combine(brain.Home, "conversations"));
        var nowhere = await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "nowhere", model = "opus", effort = "high" }), token: token, origin: server.Origin);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", start, token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        await Say(token, reference, "Monomorphise?");
        using var answered = await Answered(token, reference);
        await Say(token, reference, "And traits?");
        using var again = await Answered(token, reference);
        var empty = await Send(HttpMethod.Post, "/api/conversation/say", Json(new { @ref = reference, text = " " }), token: token, origin: server.Origin);
        using var sat = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/sitting", Json(new { @ref = reference, model = "sonnet", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        await Send(HttpMethod.Post, "/api/conversation/rename", Json(new { @ref = reference, title = "Generics" }), token: token, origin: server.Origin);
        using var listed = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/conversations", token: token)).Content.ReadAsStringAsync());
        var missing = await Send(HttpMethod.Get, "/api/conversation?ref=personal:2026-10-03-abcdef", token: token);

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);
        Assert.False(none);
        Assert.Equal(HttpStatusCode.Conflict, nowhere.StatusCode);
        Assert.Equal("opus", started.RootElement.GetProperty("row").GetProperty("current").GetProperty("model").GetString());
        var phases = answered.RootElement.GetProperty("phases");
        Assert.Equal(["user", "agent"], phases.EnumerateArray().Select(phase => phase.GetProperty("who").GetString()));
        Assert.Equal("Monomorphise?", phases[0].GetProperty("text").GetString());
        Assert.Equal("All done.", phases[1].GetProperty("text").GetString());
        Assert.Equal("high", phases[1].GetProperty("effort").GetString());
        Assert.Equal(4, again.RootElement.GetProperty("phases").GetArrayLength());
        Assert.Equal(
            "## phase 1 · user · 2026-10-03T12:00\n\nMonomorphise?\n\n## phase 2 · agent · 2026-10-03T12:00 · opus high\n\nAll done.\n\n"
            + "## phase 3 · user · 2026-10-03T12:00\n\nAnd traits?\n\n## phase 4 · agent · 2026-10-03T12:00 · opus high\n\nAll done.\n\n",
            await File.ReadAllTextAsync(Path.Combine(dir, "trails", "1.md")));
        var arguments = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.args"));
        Assert.Equal(["-p", "--agent", "brain", "--model", "opus", "--effort", "high"], arguments[..7]);
        Assert.Contains("--permission-mode", arguments);
        Assert.Contains("stream-json", arguments);
        Assert.DoesNotContain("--resume", arguments);
        Assert.Equal(brain.Home, (await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.cwd"))).Trim());
        var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said"));
        Assert.Equal(2, said.Length);
        Assert.Contains($"Conversation: {reference}", said[0], StringComparison.Ordinal);
        Assert.Single(Regex.Matches(said[0], "Monomorphise\\?"));
        Assert.EndsWith("The user:\\n\\nMonomorphise?\"}}", said[0], StringComparison.Ordinal);
        Assert.Equal("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"And traits?\"}}", said[1]);
        Assert.True(Directory.Exists(Path.Combine(dir, "research")));
        Assert.Equal("TEMP/\n", await File.ReadAllTextAsync(Path.Combine(dir, ".gitignore")));
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.Equal(2, sat.RootElement.GetProperty("row").GetProperty("sittings").GetInt32());
        Assert.Equal(0, sat.RootElement.GetProperty("phases").GetArrayLength());
        Assert.False(sat.RootElement.GetProperty("working").GetBoolean());
        var row = listed.RootElement.GetProperty("conversations")[0];
        Assert.Equal("Generics", row.GetProperty("title").GetString());
        Assert.Equal("personal", row.GetProperty("origin").GetProperty("kind").GetString());
        Assert.Equal(["personal", "global"], listed.RootElement.GetProperty("places").EnumerateArray().Select(place => place.GetProperty("name").GetString()));
        Assert.Contains("title: Generics", await File.ReadAllTextAsync(Path.Combine(dir, "state.md")), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>The window's commit button commits the conversation's folder in the data repo with its title and date.</summary>
    [Fact]
    public async Task TheWindowCommitsAConversationsFolderWithItsTitleAndDate()
    {
        TempBrain.CommitAll(brain.Home);
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "personal", model = "haiku", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Send(HttpMethod.Post, "/api/conversation/rename", Json(new { @ref = reference, title = "Generics" }), token: token, origin: server.Origin);

        var nameless = await Send(HttpMethod.Post, "/api/conversation/commit", Json(new { }), token: token, origin: server.Origin);
        var committed = await Send(HttpMethod.Post, "/api/conversation/commit", Json(new { @ref = reference }), token: token, origin: server.Origin);

        Assert.Contains("a commit names a conversation", await nameless.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        Assert.Equal("Generics, 2026-10-03", TempBrain.Git(brain.Home, "log", "-1", "--format=%s").Trim());
    }

    /// <summary>
    /// The window's dream button runs the code brain dream runs: it refuses while the sitting is open, and once a new
    /// sitting ended it, it settles the sitting. The window then shows what the dream changed since the last sitting,
    /// and the log lists the dream and reads it whole.
    /// </summary>
    [Fact]
    public async Task TheWindowDreamsAConversationAndShowsWhatTheDreamChanged()
    {
        TempBrain.CommitAll(brain.Home);
        brain.Models.Drafts["haiku"] = "SURE\nErase.";
        brain.Models.Drafts["sonnet"] = "SURE\nErase generics.";
        brain.Models.Comparison = "AGREE";
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "personal", model = "haiku", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Say(token, reference, "Erase generics?");
        (await Answered(token, reference)).Dispose();

        var open = await Send(HttpMethod.Post, "/api/conversation/dream", Json(new { @ref = reference }), token: token, origin: server.Origin);
        await Send(HttpMethod.Post, "/api/conversation/sitting", Json(new { @ref = reference, model = "haiku", effort = "low" }), token: token, origin: server.Origin);
        using var dreamed = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/dream", Json(new { @ref = reference }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        using var changes = JsonDocument.Parse(await (await Send(HttpMethod.Get, $"/api/conversation/changes?ref={reference}", token: token)).Content.ReadAsStringAsync());
        using var dreams = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/dreams", token: token)).Content.ReadAsStringAsync());
        var id = dreams.RootElement[0].GetProperty("id").GetString();
        using var one = JsonDocument.Parse(await (await Send(HttpMethod.Get, $"/api/dream?id={id}", token: token)).Content.ReadAsStringAsync());
        var missing = await Send(HttpMethod.Get, "/api/dream?id=2026-10-03-ffffff", token: token);

        Assert.Equal(HttpStatusCode.Conflict, open.StatusCode);
        Assert.Contains("is open, and a dream never runs while a sitting is", await open.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("Erase generics.\n", dreamed.RootElement.GetProperty("settled").GetString());
        Assert.Equal(0, dreamed.RootElement.GetProperty("unsettled").GetInt32());
        Assert.Equal(id, dreamed.RootElement.GetProperty("sittings")[0].GetProperty("dream").GetString());
        Assert.Equal(id, changes.RootElement.GetProperty("dreams")[0].GetProperty("id").GetString());
        Assert.Equal((string.Empty, "Erase generics."), (changes.RootElement.GetProperty("before").GetString(), changes.RootElement.GetProperty("after").GetString()));
        Assert.Equal("agreed", dreams.RootElement[0].GetProperty("outcome").GetString());
        Assert.Equal(["haiku", "sonnet"], dreams.RootElement[0].GetProperty("models").EnumerateArray().Select(model => model.GetString()));
        Assert.Equal("personal", dreams.RootElement[0].GetProperty("origin").GetProperty("kind").GetString());
        Assert.Equal(2, one.RootElement.GetProperty("drafts").GetArrayLength());
        Assert.True(one.RootElement.GetProperty("comparison").GetProperty("agreed").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>
    /// The window's "dream into brain" button runs the code brain dream --deep runs: a dream settles the ended sitting
    /// first, then the open agent dreams into brain. The log lists the deep dream with the agent's final words, and what
    /// dreams changed in settled.md names only the dream that changed it.
    /// </summary>
    [Fact]
    public async Task TheWindowDreamsAConversationIntoBrain()
    {
        TempBrain.CommitAll(brain.Home);
        brain.Models.Drafts["haiku"] = "SURE\nErase.";
        brain.Models.Drafts["sonnet"] = "SURE\nErase generics.";
        brain.Models.Comparison = "AGREE";
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "personal", model = "haiku", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Say(token, reference, "Erase generics?");
        (await Answered(token, reference)).Dispose();
        await Send(HttpMethod.Post, "/api/conversation/sitting", Json(new { @ref = reference, model = "haiku", effort = "low" }), token: token, origin: server.Origin);

        var deep = await Send(HttpMethod.Post, "/api/conversation/deep-dream", Json(new { @ref = reference }), token: token, origin: server.Origin);
        using var dreamed = JsonDocument.Parse(await deep.Content.ReadAsStringAsync());
        using var dreams = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/dreams", token: token)).Content.ReadAsStringAsync());
        var row = dreams.RootElement.EnumerateArray().Single(dream => dream.GetProperty("outcome").GetString() == "deep");
        using var one = JsonDocument.Parse(await (await Send(HttpMethod.Get, $"/api/dream?id={row.GetProperty("id").GetString()}", token: token)).Content.ReadAsStringAsync());
        using var changes = JsonDocument.Parse(await (await Send(HttpMethod.Get, $"/api/conversation/changes?ref={reference}", token: token)).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
        Assert.Equal("Erase generics.\n", dreamed.RootElement.GetProperty("settled").GetString());
        var settled = dreamed.RootElement.GetProperty("sittings")[0].GetProperty("dream").GetString();
        Assert.Equal(["opus"], row.GetProperty("models").EnumerateArray().Select(model => model.GetString()));
        var agent = one.RootElement.GetProperty("deep");
        Assert.Equal(("opus", "medium", settled, "All done."), (agent.GetProperty("model").GetString(), agent.GetProperty("effort").GetString(), agent.GetProperty("settled").GetString(), agent.GetProperty("words").GetString()));
        Assert.Equal([settled], changes.RootElement.GetProperty("dreams").EnumerateArray().Select(dream => dream.GetProperty("id").GetString()));
        Assert.Equal(OpenAgent.Arguments("opus", "medium"), await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.args")));
    }

    /// <summary>
    /// When brain serve stops, every sitting's agent stops with it, and so does everything the agent started, as a
    /// builder's run would be. A new trail stops the last sitting's agent the same way.
    /// </summary>
    [Fact]
    public async Task StoppingBrainServeStopsEveryAgentAndWhatItStarted()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "global", model = "haiku", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Say(token, reference, "Start a spike: start a build.");
        (await Answered(token, reference)).Dispose();
        var first = Child();
        await Send(HttpMethod.Post, "/api/conversation/sitting", Json(new { @ref = reference, model = "haiku", effort = "low" }), token: token, origin: server.Origin);
        var retired = Gone(first);
        await Say(token, reference, "Again: start a build.");
        (await Answered(token, reference)).Dispose();
        var second = Child();

        Stop();
        var stopped = Gone(second);
        Start();

        Assert.NotEqual(first, second);
        Assert.True(retired);
        Assert.True(stopped);
    }

    /// <summary>
    /// Comments sent with a phase are numbered in the order added and kept in the trail before the typed text, the view
    /// carries them, the agent receives the quote lines, and a comment on a phase the sitting lacks is refused.
    /// </summary>
    [Fact]
    public async Task CommentsGoOutWithThePhaseIntoTheTrailTheViewAndTheAgent()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "personal", model = "sonnet", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        var dir = Path.Combine(brain.Home, "conversations", "personal", reference["personal:".Length..]);
        await Say(token, reference, "Monomorphise?");
        (await Answered(token, reference)).Dispose();

        var lacking = await Send(HttpMethod.Post, "/api/conversation/say", Json(new { @ref = reference, text = "x", comments = new[] { new { target = 9, quote = "gone", text = "no" } } }), token: token, origin: server.Origin);
        var sent = await Send(
            HttpMethod.Post,
            "/api/conversation/say",
            Json(new { @ref = reference, comments = new[] { new { target = 2, quote = "All\n  done.", text = "why?\n\nand more" }, new { target = 0, quote = "settled words", text = "second" } } }),
            token: token,
            origin: server.Origin);
        using var view = await Answered(token, reference);

        Assert.Equal(HttpStatusCode.Conflict, lacking.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Contains(
            "## phase 3 · user · 2026-10-03T12:00\n\n> 1 on phase 2: \"All done.\"\nwhy?\n>\nand more\n\n> 2 on settled: \"settled words\"\nsecond\n\n## phase 4",
            await File.ReadAllTextAsync(Path.Combine(dir, "trails", "1.md")),
            StringComparison.Ordinal);
        var comments = view.RootElement.GetProperty("comments").EnumerateArray().ToList();
        Assert.Equal([1, 2], comments.Select(comment => comment.GetProperty("number").GetInt32()));
        Assert.Equal([2, 0], comments.Select(comment => comment.GetProperty("phase").GetInt32()));
        Assert.Equal("why?\n\nand more", comments[0].GetProperty("text").GetString());
        var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said"));
        Assert.Equal(2, said.Length);
        Assert.Contains("\\u003E 1 on phase 2: \\u0022All done.\\u0022\\nwhy?\\n\\u003E\\nand more\\n\\n\\u003E 2 on settled", said[1], StringComparison.Ordinal);
    }

    private async Task Say(string token, string reference, string text)
    {
        var said = await Send(HttpMethod.Post, "/api/conversation/say", Json(new { @ref = reference, text }), token: token, origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, said.StatusCode);
    }

    /// <summary>The conversation once its agent answered, followed the way the window follows it, version by version.</summary>
    private async Task<JsonDocument> Answered(string token, string reference)
    {
        var since = string.Empty;
        for (var ask = 0; ask < 50; ask++)
        {
            var reply = await Send(HttpMethod.Get, $"/api/conversation?ref={reference}{since}", token: token).ConfigureAwait(false);
            var view = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!view.RootElement.GetProperty("working").GetBoolean())
            {
                return view;
            }

            since = "&since=" + view.RootElement.GetProperty("version").GetInt64().ToString(CultureInfo.InvariantCulture);
            view.Dispose();
        }

        throw new InvalidOperationException("the agent never finished its answer");
    }

    /// <summary>The pid of the process the last stand-in for claude left running, as a builder's build would be.</summary>
    private int Child() => int.Parse(File.ReadAllText(Path.Combine(brain.Root, "claude.child")).Trim(), CultureInfo.InvariantCulture);

    /// <summary>True once the process is gone or a zombie, within a few seconds or <paramref name="within"/>.</summary>
    internal static bool Gone(int pid, TimeSpan? within = null) =>
        SpinWait.SpinUntil(
            () =>
            {
                var stat = $"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/stat";
                try
                {
                    return !File.Exists(stat) || File.ReadAllText(stat).Split(") ")[1].StartsWith('Z');
                }
                catch (IOException)
                {
                    return true;
                }
            },
            within ?? TimeSpan.FromSeconds(5));

    /// <summary>
    /// A stand-in for claude, so the gate never calls the model: it keeps its arguments, its directory and every line it
    /// is sent. Told to start a build, it sends a builder in the background and leaves a build running detached from
    /// it, in a session of its own and orphaned, holding a lock file named as a build slot's, as a builder's guarded
    /// build would. Told to send a validator, it sends one in the foreground and moves it to the background; told to ask
    /// a reviewer, it sends one in the foreground, which ends with the turn. It answers each line with a subagent's
    /// words, which are no part of the answer, then with a turn claude 2.1.316 streamed: a Bash call, its result,
    /// thinking with no text, the answer "All done." in two deltas, and the end of the turn.
    /// </summary>
    private static readonly string Turn = Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude-turn.jsonl");

    private string Claude()
    {
        var path = Path.Combine(brain.Root, "claude");
        File.WriteAllText(path, $$$$"""
            #!/bin/sh
            here={{{{brain.Root}}}}
            printf '%s\n' "$@" > "$here/claude.args"
            pwd > "$here/claude.cwd"
            while IFS= read -r line; do
              printf '%s\n' "$line" >> "$here/claude.said"
              case "$line" in
                *"start a build"*)
                  ( setsid flock "$here/cargo-guard.lock" sleep 300 </dev/null >/dev/null 2>&1 & echo $! > "$here/claude.child.new" )
                  until [ -s "$here/claude.child.new" ]; do sleep 0.01; done
                  mv "$here/claude.child.new" "$here/claude.child"
                  echo '{"type":"system","subtype":"task_started","task_id":"t1","tool_use_id":"toolu_2","description":"Build the chat","subagent_type":"builder","is_backgrounded":true}'
                  ;;
              esac
              case "$line" in
                *"send a validator"*)
                  echo '{"type":"system","subtype":"task_started","task_id":"t2","tool_use_id":"toolu_3","description":"Check the chat","subagent_type":"validator","is_backgrounded":false}'
                  echo '{"type":"system","subtype":"task_updated","task_id":"t2","patch":{"is_backgrounded":true}}'
                  ;;
              esac
              case "$line" in
                *"ask a reviewer"*)
                  echo '{"type":"system","subtype":"task_started","task_id":"t3","tool_use_id":"toolu_4","description":"Read the diff","subagent_type":"reviewer","is_backgrounded":false}'
                  ;;
              esac
              echo '{"type":"assistant","parent_tool_use_id":"toolu_1","message":{"content":[{"type":"text","text":"a builder talking"},{"type":"tool_use","name":"Bash","input":{"command":"scripts/dotnet-guard.sh build"}}]}}'
              cat "{{{{Turn}}}}"
            done
            """);
        OmarchyThemeTests.Chmod(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task ANodeShowsItsEdgesAsNeighbours()
    {
        brain.SaysYes = true;
        brain.Ok("add", "--subjects", "test", "global", "grass", "Grass grows first", "--kind", "idea", "--body", "Tall grass hides the ground.");
        brain.Ok("link", "meadow", "see", "grass");
        var token = await Trade(Launch());

        var node = await (await Send(HttpMethod.Get, "/api/node?ref=global:meadow", token: token)).Content.ReadAsStringAsync();
        var missing = await Send(HttpMethod.Get, "/api/node?ref=global:nothing", token: token);
        var search = await (await Send(HttpMethod.Get, "/api/search?q=grass", token: token)).Content.ReadAsStringAsync();

        Assert.Contains("\"edges\":[{\"rel\":\"see\",\"target\":\"grass\",\"ref\":\"global:grass\",\"title\":\"Grass grows first\"}]", node, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("\"ref\":\"global:grass\"", search, StringComparison.Ordinal);
        Assert.Contains("\"snippet\":\"Tall \\u0002grass\\u0003 hides the ground.\"", search, StringComparison.Ordinal);
    }

    /// <summary>The constellation and its search come only to a window's session, as every other read does.</summary>
    [Fact]
    public async Task TheSkyComesOnlyToAWindowsSession()
    {
        var token = await Trade(Launch());

        var stranger = await Send(HttpMethod.Get, "/api/sky");
        var sky = await (await Send(HttpMethod.Get, "/api/sky", token: token)).Content.ReadAsStringAsync();
        var search = await (await Send(HttpMethod.Get, "/api/sky/search?q=ground", token: token)).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Contains("\"ref\":\"global:meadow\",\"title\":\"Ground cover\",\"scope\":\"global\",\"kind\":\"idea\",\"bare\":true", sky, StringComparison.Ordinal);
        Assert.Contains("\"ref\":\"global:meadow\"", search, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNearestEntriesOfAWriteComeByNodeAndChangeNothing()
    {
        var token = await Trade(Launch());
        var id = Stage("--body", "Ground cover, then tea.");
        var before = new PendingStore(brain.Home).Asked().Single().Digest;

        var near = await (await Send(HttpMethod.Get, "/api/nearest?id=" + id, token: token)).Content.ReadAsStringAsync();
        var missing = await Send(HttpMethod.Get, "/api/nearest?id=000000000000", token: token);

        Assert.StartsWith("[{\"ref\":\"global:tea\",\"hits\":[{\"ref\":\"global:meadow\",\"title\":\"Ground cover\"", near, StringComparison.Ordinal);
        Assert.Contains("\"scope\":\"global\",\"hue\":null", near, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, new PendingStore(brain.Home).Asked().Single().Digest);
    }

    [Fact]
    public async Task EveryWindowCallOpensANewWindowThatClosesTheOldOne()
    {
        var first = await (await Send(HttpMethod.Post, "/api/window", key: Key)).Content.ReadAsStringAsync();
        var old = await Trade(CodeIn(windows.Single()));
        using var stream = await OpenEvents(old);
        using var events = new StreamReader(await stream.Content.ReadAsStreamAsync());
        var second = await (await Send(HttpMethod.Post, "/api/window", key: Key)).Content.ReadAsStringAsync();
        var foreign = await Send(HttpMethod.Post, "/api/window", key: Key, origin: "http://evil.example");
        var fresh = await Trade(CodeIn(windows[^1]));

        var told = await NextEvent(events);
        var oldState = await Send(HttpMethod.Get, "/api/state", token: old);
        var freshState = await Send(HttpMethod.Get, "/api/state", token: fresh);

        Assert.Contains("opened", first, StringComparison.Ordinal);
        Assert.Contains("opened", second, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(2, windows.Count);
        Assert.Equal("event: close", told);
        Assert.Equal(HttpStatusCode.Unauthorized, oldState.StatusCode);
        Assert.DoesNotContain("alarm", await oldState.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, freshState.StatusCode);
    }

    [Fact]
    public async Task TheBackdropImageIsServedFromTheBinary()
    {
        var image = await Send(HttpMethod.Get, "/backdrop.png");

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], (await image.Content.ReadAsByteArrayAsync())[..4]);
    }

    [Fact]
    public async Task ThePageComesWithAStrictPolicy()
    {
        var page = await Send(HttpMethod.Get, "/");
        var script = await Send(HttpMethod.Get, "/app.js");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("script-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("/app.js", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("X-Brain-Session", await script.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLaunchCodeNeverStandsOnTheBrowsersCommandLine()
    {
        var code = Launch();
        var url = windows.Single();
        var launch = new Uri(url).LocalPath;
        var dir = Path.GetDirectoryName(launch)!;

        Assert.StartsWith("file://", url, StringComparison.Ordinal);
        Assert.DoesNotContain(code, url, StringComparison.Ordinal);
        Assert.DoesNotContain("launch=", url, StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, Mode(launch));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, Mode(dir));
        Assert.Contains($"{server.Origin}/#launch={code}", await File.ReadAllTextAsync(launch), StringComparison.Ordinal);
        await Trade(code);
        Assert.False(File.Exists(launch));
    }

    [Fact]
    public async Task HelloAndWindowNeedTheKeyOnlyTheUsersAccountReads()
    {
        var keyFile = Path.Combine(brain.Home, "cache", "serve", "key");

        var hello = await Send(HttpMethod.Get, "/api/hello");
        var window = await Send(HttpMethod.Post, "/api/window");
        var guessed = await Send(HttpMethod.Post, "/api/window", key: new string('0', 64));
        var keyed = await Send(HttpMethod.Get, "/api/hello", key: Key);

        Assert.Equal(HttpStatusCode.Forbidden, hello.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, window.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, keyed.StatusCode);
        Assert.Empty(windows);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, Mode(keyFile));
    }

    [Fact]
    public async Task RestartNeedsTheKeyAndStartsTheSuccessorOnceThePortIsFree()
    {
        var id = Stage();
        Launch();

        var guessed = await Send(HttpMethod.Post, "/api/restart", key: new string('0', 64));
        var restart = await Send(HttpMethod.Post, "/api/restart", key: Key);
        await serving.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Forbidden, guessed.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, restart.StatusCode);
        Assert.Equal([false], successors);
        Assert.Empty(Directory.EnumerateFiles(new ServeFiles(brain.Home).Dir, "launch-*"));
        Assert.False(File.Exists(Path.Combine(new ServeFiles(brain.Home).Dir, HandedLaunch.FileName)));
        Assert.True(new PendingStore(brain.Home).IsAsked(id));
    }

    /// <summary>
    /// A restart with a window listening tells it the successor's launch URL on its own stream, answers nothing new,
    /// and starts the successor once its port is free. The successor admits the handed code in place of opening a
    /// window, so the window that moves trades it and no second window opens.
    /// </summary>
    [Fact]
    public async Task ARestartMovesTheWindowOntoTheSuccessorAndOpensNoSecondWindow()
    {
        StartWithOmarchy();
        var token = await Trade(Launch());
        using var stream = await OpenEvents(token);
        using var events = new StreamReader(await stream.Content.ReadAsStreamAsync());
        var opened = await NextEvent(events);

        var restart = await Send(HttpMethod.Post, "/api/restart", key: Key);
        var told = await NextEvent(events);
        var url = await events.ReadLineAsync();
        await serving.WaitAsync(TimeSpan.FromSeconds(5));
        var successor = new ReviewServer(brain.Env(TextReader.Null, TextWriter.Null, TextWriter.Null), port, Open, time, new OmarchyTheme(brain.Root, colorCommand: null), Successor, models, GpuWatch.None(time));
        using var stopping = new CancellationTokenSource();
        var served = successor.Serve(window: true, stopping.Token);
        var launch = "data: " + server.Origin + "/#launch=";
        Assert.NotNull(url);
        Assert.StartsWith(launch, url, StringComparison.Ordinal);
        var moved = await Send(HttpMethod.Post, "/api/launch", Json(new { code = url[launch.Length..] }), origin: server.Origin);

        Assert.Equal(HttpStatusCode.Accepted, restart.StatusCode);
        Assert.Equal("event: theme", opened);
        Assert.Equal("event: moved", told);
        Assert.Equal([false], successors);
        Assert.Single(windows);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.False(File.Exists(Path.Combine(new ServeFiles(brain.Home).Dir, HandedLaunch.FileName)));
        await stopping.CancelAsync();
        await served.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AStopWithoutARestartClosesTheWindowAndStartsNoSuccessor()
    {
        var token = await Trade(Launch());
        using var stream = await OpenEvents(token);
        using var events = new StreamReader(await stream.Content.ReadAsStreamAsync());

        var stopped = stop.CancelAsync();
        var told = await NextEvent(events);
        await stopped.WaitAsync(TimeSpan.FromSeconds(5));
        await serving.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("event: close", told);
        Assert.Empty(successors);
    }

    [Fact]
    public async Task TheWindowCarriesTheNewFileByteForByte()
    {
        var token = await Trade(Launch());
        var id = Stage("--why", "It shows in every session.", "--body", "See [the source](https://example.com/hidden).");
        time.Advance(WindowKeys.Arming);

        using var pending = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/pending", token: token)).Content.ReadAsStringAsync());
        var shown = pending.RootElement[0].GetProperty("changes")[0].GetProperty("after").GetString();
        await Send(HttpMethod.Post, "/api/answer", Answer(id, "yes"), token: token, origin: server.Origin);

        Assert.Equal(brain.NodeText("global", "tea"), shown);
        Assert.Contains("https://example.com/hidden", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutOmarchyThereIsNoThemeToFollowButTheWindowStillHasItsEvents()
    {
        var token = await Trade(Launch());

        var css = await Send(HttpMethod.Get, "/theme.css");
        using var stream = await OpenEvents(token);
        using var events = new StreamReader(await stream.Content.ReadAsStreamAsync());
        var first = await events.ReadLineAsync();
        await Trade(Launch());
        var told = await NextEvent(events);

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await css.Content.ReadAsStringAsync());
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        Assert.Equal(": alive", first);
        Assert.Equal("event: close", told);
    }

    [Fact]
    public async Task TheWindowFollowsAnOmarchyThemeSwitch()
    {
        StartWithOmarchy();
        var token = await Trade(Launch());
        var before = await (await Send(HttpMethod.Get, "/theme.css")).Content.ReadAsStringAsync();
        var refused = await Send(HttpMethod.Get, "/api/events");
        using var stream = await OpenEvents(token);
        using var events = new StreamReader(await stream.Content.ReadAsStreamAsync());

        var opened = await NextEvent(events);
        OmarchyThemeTests.WriteTheme(brain.Root, OmarchyThemeTests.Catppuccin.Replace("#1e1e2e", "#101010", StringComparison.Ordinal), "dim");
        var switched = await NextEvent(events);
        var after = await (await Send(HttpMethod.Get, "/theme.css")).Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        Assert.Equal("event: theme", opened);
        Assert.Equal("event: theme", switched);
        Assert.Contains("--paper: #1e1e2e;", before, StringComparison.Ordinal);
        Assert.Contains("--paper: #101010;", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheThemeIsAnotherHostsNothing()
    {
        StartWithOmarchy();

        var rebound = await Send(HttpMethod.Get, "/theme.css", host: "evil.example:" + port.ToString(CultureInfo.InvariantCulture));

        Assert.NotEqual(HttpStatusCode.OK, rebound.StatusCode);
    }

    [Fact]
    public async Task EveryChildListOfThePageDropsItsEmptyChildren()
    {
        var script = await (await Send(HttpMethod.Get, "/app.js")).Content.ReadAsStringAsync();

        // replaceChildren prints a null child as the text "null"; fill() drops it, as h() does, so only fill() may call it.
        Assert.Single(ReplaceChildren().Matches(script));
        Assert.Contains("el.replaceChildren(...nodes(children));", script, StringComparison.Ordinal);
    }

    /// <summary>A source sends data only: the pages that draw its questions and its context build every element with h() and never set HTML.</summary>
    [Theory]
    [InlineData("/questions.js")]
    [InlineData("/menu.js")]
    [InlineData("/highlight.js")]
    public async Task ASourcesTextNeverReachesThePageAsHtml(string path)
    {
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();
        var questions = await Send(HttpMethod.Get, path);
        var script = await questions.Content.ReadAsStringAsync();

        Assert.Contains($"<script src=\"{path}\" defer></script>", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, questions.StatusCode);
        Assert.Empty(ReplaceChildren().Matches(script));
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
    }

    /// <summary>A model's draft reaches the dream log as text: the page that draws it builds every element with h() and never sets HTML.</summary>
    [Fact]
    public async Task ADreamersDraftNeverReachesThePageAsHtml()
    {
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();
        var dreams = await Send(HttpMethod.Get, "/dreams.js");
        var script = await dreams.Content.ReadAsStringAsync();

        Assert.Contains("<script src=\"/dreams.js\" defer></script>", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, dreams.StatusCode);
        Assert.Empty(ReplaceChildren().Matches(script));
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
    }

    /// <summary>Mermaid comes from brain itself, under the page's policy, and only once a diagram asks for it.</summary>
    [Fact]
    public async Task ThePageDrawsConversationsAndMermaidFromBrainItself()
    {
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();
        var diagrams = await (await Send(HttpMethod.Get, "/diagrams.js")).Content.ReadAsStringAsync();
        var mermaid = await Send(HttpMethod.Get, "/mermaid.js");

        Assert.Contains("<script src=\"/conversations.js\" defer></script>", page, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/offer.js\" type=\"module\"></script>", page, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/conversations.css\">", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/conversations.css")).StatusCode);
        Assert.DoesNotContain("/mermaid.js", page, StringComparison.Ordinal);
        Assert.Contains("script.src = '/mermaid.js';", diagrams, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, mermaid.StatusCode);
        Assert.Contains("script-src 'self'", mermaid.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(MermaidSha256, Convert.ToHexStringLower(SHA256.HashData(await mermaid.Content.ReadAsByteArrayAsync())));
    }

    /// <summary>
    /// lit-html comes from brain itself, under the page's policy, and lit.js hands it to the classic scripts before the
    /// first of them runs.
    /// </summary>
    [Fact]
    public async Task ThePageDrawsWithLitHtmlFromBrainItself()
    {
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();
        var shim = await (await Send(HttpMethod.Get, "/lit.js")).Content.ReadAsStringAsync();
        var lit = await Send(HttpMethod.Get, "/lit-html.js");

        Assert.Contains("<script src=\"/lit.js\" type=\"module\"></script>\n<script src=\"/offer.js\" type=\"module\"></script>\n<script src=\"/questions.js\" defer></script>", page, StringComparison.Ordinal);
        Assert.Contains("from '/lit-html.js'", shim, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, lit.StatusCode);
        Assert.Contains("script-src 'self'", lit.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(LitHtmlSha256, Convert.ToHexStringLower(SHA256.HashData(await lit.Content.ReadAsByteArrayAsync())));
    }

    /// <summary>
    /// brain serve rebuilds the conversations index when it starts, so a conversation a pull brought in while the index
    /// stood still shows after a restart, though no change of the window's rebuilt it.
    /// </summary>
    [Fact]
    public async Task AConversationWrittenBehindTheIndexShowsAfterARestart()
    {
        var first = brain.Ok("conversation", "start", "personal").Split(' ')[1].TrimEnd(':');
        var token = await Trade(Launch());
        var before = await (await Send(HttpMethod.Get, "/api/conversations", token: token)).Content.ReadAsStringAsync();
        var folder = Path.Combine(brain.Home, "conversations", "personal");
        var pulled = Path.Combine(folder, "2026-10-01-0a0b0c");
        Directory.CreateDirectory(pulled);
        File.Copy(Path.Combine(folder, first["personal:".Length..], "state.md"), Path.Combine(pulled, "state.md"));

        var stale = await (await Send(HttpMethod.Get, "/api/conversations", token: token)).Content.ReadAsStringAsync();
        Stop();
        Start();
        var after = await (await Send(HttpMethod.Get, "/api/conversations", token: await Trade(Launch()))).Content.ReadAsStringAsync();

        Assert.Contains(first, before, StringComparison.Ordinal);
        Assert.DoesNotContain("personal:2026-10-01-0a0b0c", stale, StringComparison.Ordinal);
        Assert.Contains("personal:2026-10-01-0a0b0c", after, StringComparison.Ordinal);
    }

    /// <summary>
    /// The SHA-256 of the vendored mermaid.min.js, the file mermaid 11.15.0's npm tarball ships as dist/mermaid.min.js.
    /// Any change to the vendored file fails here, so a new mermaid comes in only with this hash checked again.
    /// </summary>
    private const string MermaidSha256 = "70137e77bb273bb2ef972b86e8b0400cca8be53cb25bfc45911a186dc98665de";

    /// <summary>
    /// The SHA-256 of the vendored lit-html.js, the file lit-html 3.3.3's npm tarball ships as lit-html.js. Any change to
    /// the vendored file fails here, so a new lit-html comes in only with this hash checked again.
    /// </summary>
    private const string LitHtmlSha256 = "b878e7f95dec8a9b6e9b217faca6b6a11bad82ebc44c0caa77419ed83edd81f2";

    private async Task<HttpResponseMessage> OpenEvents(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add(ReviewServer.SessionHeader, token);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    }

    /// <summary>The first line of the next event on a window's event stream, past its heartbeats; it fails after five seconds.</summary>
    private static async Task<string?> NextEvent(StreamReader events)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (await events.ReadLineAsync(limit.Token).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    private bool Open(string url)
    {
        lock (windows)
        {
            windows.Add(url);
        }

        return true;
    }

    /// <summary>Records a successor, and whether something still answered on the server's port when it started.</summary>
    private void Successor()
    {
        bool taken;
        try
        {
            using var probe = new TcpClient();
            probe.Connect(IPAddress.Loopback, port);
            taken = true;
        }
        catch (SocketException)
        {
            taken = false;
        }

        successors.Add(taken);
    }

    private string Launch()
    {
        server.OpenWindow();
        lock (windows)
        {
            return CodeIn(windows[^1]);
        }
    }

    private string Key => new ServeFiles(brain.Home).ReadKey()!;

    /// <summary>The launch code in the launch file a window was opened on.</summary>
    private static string CodeIn(string url) => LaunchCode().Match(File.ReadAllText(new Uri(url).LocalPath)).Groups[1].Value;

    private async Task<string> Trade(string code)
    {
        var reply = await Send(HttpMethod.Post, "/api/launch", Json(new { code }), origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>Stages an add of global:tea and asks it, as <c>land</c> does.</summary>
    private string Stage(params string[] more)
    {
        var id = Id(brain.Ok(["add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea", .. more]));
        Ask(id);
        return id;
    }

    private void Ask(string id) => new PendingStore(brain.Home).Ask(id);

    private string Answer(string id, string answer, string? reason = null) =>
        Json(new { id, digest = new PendingStore(brain.Home).Asked().Single(asked => asked.Write.Id == id).Digest, answer, reason });

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private async Task<HttpResponseMessage> Send(
        HttpMethod method, string path, string? body = null, string? token = null, string? origin = null, string? host = null, string? key = null)
    {

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Add(ReviewServer.SessionHeader, token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (key is not null)
        {
            request.Headers.Add(ServeFiles.KeyHeader, key);
        }

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    /// <summary>A file's mode; brain serve runs on Linux, macOS and WSL only.</summary>
    private static UnixFileMode Mode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("brain serve runs on Linux, macOS and WSL");
        }

        return File.GetUnixFileMode(path);
    }

    private static string Id(string output) => PendingId().Match(output).Groups[1].Value;

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    [GeneratedRegex("pending ([0-9a-f]{12}):")]
    private static partial Regex PendingId();

    [GeneratedRegex(@"\.replaceChildren\(")]
    private static partial Regex ReplaceChildren();

    [GeneratedRegex("#launch=([0-9a-f]{64})")]
    private static partial Regex LaunchCode();

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan span) => now += span;
    }
}
