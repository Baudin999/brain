using System.Globalization;
using System.Net;
using System.Text.Json;
using Brain.Children;
using Brain.Models;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The chats of brain's window: the open agent on the chat setting, Sonnet at medium effort, unless picked, summoned only from the window,
/// one per tab. It keeps nothing anywhere, and it is gone, with its agent and what that started, once closed. The
/// registry, in brain serve's memory, says which tabs hold a chat and what runs under each.
/// </summary>
public sealed partial class ReviewServerTests
{
    /// <summary>A chat's seed carries the context the window sent, fenced and unchanged, and says when there is none.</summary>
    [Fact]
    public void AChatSeedCarriesTheContextItWasSummonedWith()
    {
        var none = ChatRoom.Seed("Home", string.Empty, "Hello");
        var with = ChatRoom.Seed("Node review", "A write.\n\n+ a new line", "Change it");

        Assert.Contains("it starts with no context.", none, StringComparison.Ordinal);
        Assert.DoesNotContain("<context>", none, StringComparison.Ordinal);
        Assert.Contains("<context>\nA write.\n\n+ a new line\n</context>", with, StringComparison.Ordinal);
        Assert.Contains("It does not change when they select something else", with, StringComparison.Ordinal);
        Assert.EndsWith("The user:\n\nChange it", with, StringComparison.Ordinal);
    }

    /// <summary>A context sent with the summons reaches the agent in its first words, even past the size other calls may send; one over the chat's limit is refused with its own reason.</summary>
    [Fact]
    public async Task AChatSummonedWithContextHandsItToTheAgentUntilItsLimit()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var context = "The write.\n" + new string('x', 100 * 1024);

        var id = await StartChat(token, Json(new { tab = ":node review", place = "Node review", context }));
        await SayToChat(token, id, "Shorten it.");
        using var answered = await ChatAnswered(token, id);
        var over = await Send(HttpMethod.Post, "/api/chat/start", Json(new { tab = ":home", place = "Home", context = new string('y', ChatRoom.MaxStartBody) }), token: token, origin: server.Origin);

        var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said"));
        Assert.Contains("\\u003Ccontext\\u003E\\nThe write.\\nxxx", said[0], StringComparison.Ordinal);
        Assert.Contains("xxx\\n\\u003C/context\\u003E", said[0], StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Contains("context is over 1024 KiB", await over.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChatIsTheOpenAgentAndKeepsNothingOnceClosed()
    {
        var token = await Trade(Launch());
        var before = Files(brain.Home);

        var stranger = await Send(HttpMethod.Get, "/api/chat?id=x");
        var early = await Send(HttpMethod.Post, "/api/chat/start", Home, token: token, origin: server.Origin);
        time.Advance(WindowKeys.Arming);
        var foreign = await Send(HttpMethod.Post, "/api/chat/start", Home, token: token, origin: "http://evil.example");
        var badModel = await Send(HttpMethod.Post, "/api/chat/start", Json(new { tab = ":home", model = "gpt", effort = "low" }), token: token, origin: server.Origin);
        var noTab = await Send(HttpMethod.Post, "/api/chat/start", "{}", token: token, origin: server.Origin);
        var id = await StartChat(token, Home);
        var empty = await Send(HttpMethod.Post, "/api/chat/say", Json(new { id, text = " " }), token: token, origin: server.Origin);
        await SayToChat(token, id, "What does the graph say about meadows?");
        using var answered = await ChatAnswered(token, id);
        await SayToChat(token, id, "And grass?");
        using var again = await ChatAnswered(token, id);
        var closed = await Send(HttpMethod.Post, "/api/chat/close", Json(new { id }), token: token, origin: server.Origin);
        var gone = await Send(HttpMethod.Get, $"/api/chat?id={id}", token: token);
        var late = await Send(HttpMethod.Post, "/api/chat/say", Json(new { id, text = "Still there?" }), token: token, origin: server.Origin);

        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.Equal((HttpStatusCode)425, early.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, badModel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, noTab.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        var phases = answered.RootElement.GetProperty("phases");
        Assert.Equal(["user", "agent"], phases.EnumerateArray().Select(phase => phase.GetProperty("who").GetString()));
        Assert.Equal("What does the graph say about meadows?", phases[0].GetProperty("text").GetString());
        Assert.Equal("All done.", phases[1].GetProperty("text").GetString());
        Assert.Equal("medium", phases[1].GetProperty("effort").GetString());
        Assert.Equal(4, again.RootElement.GetProperty("phases").GetArrayLength());
        var arguments = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.args"));
        Assert.Equal(OpenAgent.Arguments("sonnet", "medium"), arguments);
        Assert.Equal(["-p", "--agent", "brain", "--model", "sonnet", "--effort", "medium"], arguments[..7]);
        Assert.Equal(brain.Home, (await File.ReadAllTextAsync(Path.Combine(brain.Root, "claude.cwd"))).Trim());
        var said = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.said"));
        Assert.Equal(2, said.Length);
        Assert.Contains("summoned this chat on the Home tab of the window, and it starts with no context", said[0], StringComparison.Ordinal);
        Assert.EndsWith("The user:\\n\\nWhat does the graph say about meadows?\"}}", said[0], StringComparison.Ordinal);
        Assert.Equal("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"And grass?\"}}", said[1]);
        Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(before, Files(brain.Home));
    }

    /// <summary>
    /// Each tab holds its own chat, on the model and effort the user picks, and several live at once: a second chat on
    /// one tab is refused, and a new window ends none. The registry names each chat's tab, model, effort and start, and
    /// what runs under it: the builder its agent sent and the build that holds a build slot.
    /// </summary>
    [Fact]
    public async Task EachTabHoldsItsOwnChatAndTheRegistryNamesWhatRunsUnderIt()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var home = await StartChat(token, Json(new { tab = ":home", place = "Home", model = "opus", effort = "high" }));
        await SayToChat(token, home, "Clean up three nodes.");
        (await ChatAnswered(token, home)).Dispose();
        var picked = await File.ReadAllLinesAsync(Path.Combine(brain.Root, "claude.args"));
        var review = await StartChat(token, Json(new { tab = "node review", place = "Node review" }));
        var twice = await Send(HttpMethod.Post, "/api/chat/start", Json(new { tab = ":home" }), token: token, origin: server.Origin);
        await SayToChat(token, review, "Please start a build.");
        (await ChatAnswered(token, review)).Dispose();
        var build = Child();

        using var registry = await Chats(token);
        var next = await Trade(Launch());
        using var kept = await Chats(next);

        Assert.Equal(["-p", "--agent", "brain", "--model", "opus", "--effort", "high"], picked[..7]);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        var chats = registry.RootElement.GetProperty("agents");
        Assert.Equal([":home", "node review"], chats.EnumerateArray().Select(chat => chat.GetProperty("tab").GetString()));
        Assert.Equal(["Home", "Node review"], chats.EnumerateArray().Select(chat => chat.GetProperty("name").GetString()));
        Assert.Equal(["opus", "sonnet"], chats.EnumerateArray().Select(chat => chat.GetProperty("model").GetString()));
        Assert.Equal(["high", "medium"], chats.EnumerateArray().Select(chat => chat.GetProperty("effort").GetString()));
        Assert.Equal(time.GetUtcNow(), chats[0].GetProperty("started").GetDateTimeOffset());
        Assert.Empty(chats[0].GetProperty("running").EnumerateArray());
        var running = chats[1].GetProperty("running").EnumerateArray().Select(work => (work.GetProperty("kind").GetString(), work.GetProperty("what").GetString(), work.GetProperty("slot").GetBoolean())).ToList();
        Assert.Equal([("agent", "builder: Build the chat", false), ("process", $"flock {brain.Root}/cargo-guard.lock sleep 300", true)], running);
        Assert.False(Gone(build, TimeSpan.FromMilliseconds(300)));
        Assert.Equal([home, review], kept.RootElement.GetProperty("agents").EnumerateArray().Select(chat => chat.GetProperty("id").GetString()));
    }

    /// <summary>
    /// The registry holds every agent brain serve runs, not only chats: a conversation's live sitting on the
    /// conversations tab, and each model a dream asks on the dreams tab while it answers, both named as the window's
    /// scripts name those tabs.
    /// </summary>
    [Fact]
    public async Task TheRegistryHoldsEverySittingAndEveryDreamToo()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "global", model = "haiku", effort = "low" }), token: token, origin: server.Origin)).Content.ReadAsStringAsync());
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Say(token, reference, "Monomorphise?");
        (await Answered(token, reference)).Dispose();
        var asker = Path.Combine(brain.Root, "asker");
        await File.WriteAllTextAsync(asker, "#!/bin/sh\ncat >/dev/null\nsleep 300\n");
        OmarchyThemeTests.Chmod(asker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var place = new RunPlace(WindowTabs.Dreams, AgentEntry.Dream, reference, "dream of " + reference);
        var dreaming = Task.Run(() =>
        {
            using (ClaudeRuns.For(place))
            {
                return Record.Exception(() => new ClaudeModels(asker).Ask("haiku", "medium", "system", "prompt", TimeSpan.FromSeconds(30)));
            }
        });
        Assert.True(SpinWait.SpinUntil(() => ClaudeRuns.Now().Any(run => run.Place == place), TimeSpan.FromSeconds(5)));

        using var registry = await Chats(token);
        foreach (var run in ClaudeRuns.Now().Where(run => run.Place == place))
        {
            run.Stop();
        }

        var failed = await dreaming.ConfigureAwait(true);
        var page = await (await Send(HttpMethod.Get, "/conversations.js")).Content.ReadAsStringAsync();
        var dreams = await (await Send(HttpMethod.Get, "/dreams.js")).Content.ReadAsStringAsync();

        var agents = registry.RootElement.GetProperty("agents").EnumerateArray().Select(agent => (agent.GetProperty("tab").GetString(), agent.GetProperty("kind").GetString(), agent.GetProperty("id").GetString(), agent.GetProperty("model").GetString())).ToList();
        Assert.Equal([(WindowTabs.Conversations, "sitting", reference, "haiku"), (WindowTabs.Dreams, "dream", reference, "haiku")], agents);
        Assert.IsType<BrainException>(failed);
        Assert.Contains($"const CONVERSATIONS = '{WindowTabs.Conversations}';", page, StringComparison.Ordinal);
        Assert.Contains($"const DREAMS = '{WindowTabs.Dreams}';", dreams, StringComparison.Ordinal);
    }

    /// <summary>
    /// A live sitting agent holds the sitting lock, so brain conversation remove, a process of its own, refuses while
    /// the agent lives and goes through once it ends, stopped by the window or exited by itself.
    /// </summary>
    [Fact]
    public async Task RemoveRefusesWhileASittingAgentLivesAndGoesThroughOnceItStoppedOrExited()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var stopped = await StartAndSay(token, "Monomorphise?");
        var exited = await StartAndSay(token, "And traits?");

        var live = brain.Run("conversation", "remove", stopped);
        await Send(HttpMethod.Post, "/api/conversation/sitting", Json(new { @ref = stopped, model = "sonnet", effort = "low" }), token: token, origin: server.Origin);
        var afterStop = brain.Run("conversation", "remove", stopped);
        foreach (var run in ClaudeRuns.Now().Where(run => run.Place?.Id == exited))
        {
            run.Stop();
        }

        Assert.True(SpinWait.SpinUntil(() => brain.Run("conversation", "remove", exited).Code == 0, TimeSpan.FromSeconds(5)));
        Assert.NotEqual(0, live.Code);
        Assert.Contains("live sitting agent", live.Err, StringComparison.Ordinal);
        Assert.Equal(0, afterStop.Code);
        Assert.False(Directory.Exists(Path.Combine(brain.Home, "conversations", "global", stopped["global:".Length..])));
    }

    private async Task<string> StartAndSay(string token, string words)
    {
        using var started = JsonDocument.Parse(await (await Send(HttpMethod.Post, "/api/conversation/start", Json(new { scope = "global", model = "haiku", effort = "low" }), token: token, origin: server.Origin).ConfigureAwait(false)).Content.ReadAsStringAsync().ConfigureAwait(false));
        var reference = started.RootElement.GetProperty("row").GetProperty("ref").GetString()!;
        await Say(token, reference, words).ConfigureAwait(false);
        (await Answered(token, reference).ConfigureAwait(false)).Dispose();
        return reference;
    }

    /// <summary>
    /// A chat with nothing running under it closes at once. One whose builder or build still runs closes only on the
    /// user's word: the close is refused and names what would stop, and the close with stop ends all of it.
    /// </summary>
    [Fact]
    public async Task ClosingAChatWhoseWorkRunsNamesWhatStopsAndWaitsForTheUsersWord()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var id = await StartChat(token, Json(new { tab = ":dreams", place = "Dreams" }));
        await SayToChat(token, id, "Please start a build.");
        (await ChatAnswered(token, id)).Dispose();
        var build = Child();

        var asked = await Send(HttpMethod.Post, "/api/chat/close", Json(new { id }), token: token, origin: server.Origin);
        using var named = JsonDocument.Parse(await asked.Content.ReadAsStringAsync());
        var still = Gone(build, TimeSpan.FromMilliseconds(300));
        var stopped = await Send(HttpMethod.Post, "/api/chat/close", Json(new { id, stop = true }), token: token, origin: server.Origin);
        var ended = Gone(build);
        using var registry = await Chats(token);

        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.Equal(["agent", "process"], named.RootElement.GetProperty("running").EnumerateArray().Select(work => work.GetProperty("kind").GetString()));
        Assert.False(still);
        Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
        Assert.True(ended);
        Assert.Empty(registry.RootElement.GetProperty("agents").EnumerateArray());
    }

    /// <summary>
    /// An agent sent in the foreground ends with the turn that sent it, unless it moved to the background, as Claude
    /// Code says with task_updated: then it still runs under the chat, and closing the chat names it and asks.
    /// </summary>
    [Fact]
    public async Task AnAgentMovedToTheBackgroundStillRunsUnderTheChat()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var id = await StartChat(token, Home);
        await SayToChat(token, id, "Please send a validator and ask a reviewer.");
        (await ChatAnswered(token, id)).Dispose();

        var asked = await Send(HttpMethod.Post, "/api/chat/close", Json(new { id }), token: token, origin: server.Origin);
        using var named = JsonDocument.Parse(await asked.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.Equal(["validator: Check the chat"], named.RootElement.GetProperty("running").EnumerateArray().Select(work => work.GetProperty("what").GetString()));
    }

    /// <summary>When brain serve stops, every tab's chat stops with it, the build its agent left running detached included.</summary>
    [Fact]
    public async Task StoppingBrainServeStopsEveryChatAndWhatItStarted()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var id = await StartChat(token, Json(new { tab = ":evidence", place = "Evidence" }));
        await SayToChat(token, id, "Please start a build.");
        (await ChatAnswered(token, id)).Dispose();
        var build = Child();
        await StartChat(token, Home);

        Stop();
        var stopped = Gone(build);
        Start();

        Assert.True(stopped);
    }

    /// <summary>
    /// The restart <c>./install.sh</c> asks for ends every chat, with the build its agent left running, before the
    /// successor starts; the restart neither refuses nor waits for the chats.
    /// </summary>
    [Fact]
    public async Task ARestartEndsEveryChatBeforeTheSuccessorStarts()
    {
        var token = await Trade(Launch());
        time.Advance(WindowKeys.Arming);
        var id = await StartChat(token, Home);
        await SayToChat(token, id, "Please start a build.");
        (await ChatAnswered(token, id)).Dispose();
        var build = Child();

        var restart = await Send(HttpMethod.Post, "/api/restart", key: Key);
        await serving.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.Accepted, restart.StatusCode);
        Assert.True(Gone(build, TimeSpan.Zero));
        Assert.Equal([false], successors);
    }

    /// <summary>The agent's words in a chat reach the page as text: the page that draws the chat builds every element with h() and never sets HTML.</summary>
    [Fact]
    public async Task AnAgentsWordsInAChatNeverReachThePageAsHtml()
    {
        var page = await (await Send(HttpMethod.Get, "/")).Content.ReadAsStringAsync();
        var chat = await Send(HttpMethod.Get, "/chat.js");
        var script = await chat.Content.ReadAsStringAsync();

        Assert.Contains("<script src=\"/chat.js\" defer></script>", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, chat.StatusCode);
        Assert.Empty(ReplaceChildren().Matches(script));
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
    }

    private static readonly string Home = Json(new { tab = ":home", place = "Home" });

    private async Task<JsonDocument> Chats(string token)
    {
        var reply = await Send(HttpMethod.Get, "/api/agents", token: token).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        return JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private async Task<string> StartChat(string token, string body)
    {
        var started = await Send(HttpMethod.Post, "/api/chat/start", body, token: token, origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var view = JsonDocument.Parse(await started.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.Empty(view.RootElement.GetProperty("phases").EnumerateArray());
        return view.RootElement.GetProperty("id").GetString()!;
    }

    private async Task SayToChat(string token, string id, string text)
    {
        var said = await Send(HttpMethod.Post, "/api/chat/say", Json(new { id, text }), token: token, origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, said.StatusCode);
    }

    /// <summary>The chat once its agent answered, followed the way the window follows it, version by version.</summary>
    private async Task<JsonDocument> ChatAnswered(string token, string id)
    {
        var since = string.Empty;
        for (var ask = 0; ask < 50; ask++)
        {
            var reply = await Send(HttpMethod.Get, $"/api/chat?id={id}{since}", token: token).ConfigureAwait(false);
            var view = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!view.RootElement.GetProperty("working").GetBoolean())
            {
                return view;
            }

            since = "&since=" + view.RootElement.GetProperty("version").GetInt64().ToString(CultureInfo.InvariantCulture);
            view.Dispose();
        }

        throw new InvalidOperationException("the chat's agent never finished its answer");
    }

    /// <summary>Every file under the data repo but the window's own launch files, so a test sees what a chat left behind.</summary>
    private static List<string> Files(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Where(path => !path.StartsWith(Path.Combine("cache", "serve"), StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
}
