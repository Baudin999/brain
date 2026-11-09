using System.Net;
using System.Text.Json;

namespace Brain.Tests;

/// <summary>
/// The agents panel as a flow: a session whose open agent sent three builders and an Explore agent. The first builder's
/// work landed after two rounds, with a NOT READY then a READY check before it, and its worktree, which its own Bash
/// call opened, was removed by its validator after it removed a stale one; the second's validator is in round 1, and its worktree is still there; the
/// third, in the worktree Claude Code gave it, still builds. The server says where each worktree was opened and whether
/// it was removed; the window draws three chains apart and the Explore agent on its own, and draws the list instead
/// once the settings say so.
/// </summary>
public sealed partial class CodeServerTests
{
    private const string LensReview = "Validate the lens. Builder agent ID: b1.";
    private const string DialReview = "Validate the dial. Builder agent ID: b2.";

    [Fact]
    public async Task TheServerSaysWhereEachWorktreeWasOpenedAndWhetherItWasRemoved()
    {
        var id = await ThreeBuilders();

        var agents = await ListedAgents(id);

        var lens = Worktree(agents, "toolu_b1");
        Assert.Equal(Path.Combine(project, ".claude", "worktrees", "lens"), lens.GetProperty("path").GetString());
        Assert.Equal(At(180), lens.GetProperty("opened").GetDateTimeOffset());
        Assert.True(lens.GetProperty("removed").GetBoolean());
        Assert.Equal(At(2050), lens.GetProperty("removedAt").GetDateTimeOffset());
        var dial = Worktree(agents, "toolu_b2");
        Assert.Equal(Path.Combine(project, ".claude", "worktrees", "dial"), dial.GetProperty("path").GetString());
        Assert.False(dial.GetProperty("removed").GetBoolean());
        Assert.False(dial.TryGetProperty("removedAt", out _));
        var gauge = Worktree(agents, "toolu_b3");
        Assert.Equal(Path.Combine(project, ".claude", "worktrees", "agent-b3"), gauge.GetProperty("path").GetString());
        Assert.False(gauge.GetProperty("removed").GetBoolean());
        Assert.False(Listed(agents, "toolu_x").TryGetProperty("worktree", out _));
        Assert.True(Listed(agents, "toolu_r1").GetProperty("asks").GetBoolean());
        Assert.False(Listed(agents, "toolu_r2").TryGetProperty("asks", out _));
        var landing = Listed(agents, "toolu_b1").GetProperty("steps").EnumerateArray().Last();
        Assert.Equal("cbfdcf1..8124cb8", landing.GetProperty("landed").GetString());
    }

    [Fact]
    public async Task AWorktreeWhoseFolderIsGoneWasRemovedThoughNoCallRemovedIt()
    {
        var id = await ThreeBuilders();
        Directory.Delete(Path.Combine(project, ".claude", "worktrees", "dial"));

        var agents = await ListedAgents(id);

        var dial = Worktree(agents, "toolu_b2");
        Assert.True(dial.GetProperty("removed").GetBoolean());
        Assert.False(dial.TryGetProperty("removedAt", out _));
    }

    [Theory]
    [InlineData("git -C /p worktree add .claude/worktrees/x -b x main", "add", "/s", "/p/.claude/worktrees/x")]
    [InlineData("cd /p && git worktree add -b x ../y main", "add", "/s", "/y")]
    [InlineData("git worktree add w", "add", "/s", "/s/w")]
    [InlineData("git -C /p worktree remove --force /p/w/", "remove", "/s", "/p/w")]
    [InlineData("git -C /p worktree list", "add", "/s", null)]
    [InlineData("git -C /p worktree remove /p/w", "add", "/s", null)]
    [InlineData("echo git worktree add", "add", "/s", null)]
    [InlineData("git worktree add -b x", "add", "/s", null)]
    [InlineData("git -C \"/p\" worktree add --lock \"w\"", "add", "/s", "/p/w")]
    [InlineData("git -C ~/p worktree add w", "add", "/s", "~/p/w")]
    [InlineData("git -C /p worktree add ~/w", "add", "/s", "~/w")]
    [InlineData("git -C $P worktree add w", "add", "/s", null)]
    [InlineData("git -C \"$P\" worktree add /p/w", "add", "/s", "/p/w")]
    [InlineData("git worktree add \"$WT\"", "add", "/s", null)]
    [InlineData("cd $P && git worktree add w", "add", "/s", null)]
    public void AWorktreeCommandNamesTheFullPathOfItsWorktree(string command, string verb, string cwd, string? path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = path?.Replace("~", home, StringComparison.Ordinal);

        Assert.Equal(expected, Brain.Code.Worktrees.Named(command, verb, cwd));
    }

    [Fact]
    public async Task TheAgentsPanelIsAFlowUntilTheSettingsSayAListAndAnyOtherChoiceIsRefused()
    {
        var token = await Trade(Code(windows[0]));
        var first = await Json(await Send(HttpMethod.Get, "/api/settings", token: token));

        var saved = await Send(HttpMethod.Post, "/api/settings", Json(new { tools = true, brain = true, agents = "list" }), token: token, origin: server.Origin);
        var refused = await Send(HttpMethod.Post, "/api/settings", Json(new { tools = true, brain = true, agents = "tree" }), token: token, origin: server.Origin);
        var read = await Json(await Send(HttpMethod.Get, "/api/settings", token: token));

        Assert.Equal("flow", first.RootElement.GetProperty("agents").GetString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("list", read.RootElement.GetProperty("agents").GetString());
    }

    [WindowFact]
    public async Task TheFlowDrawsEachPieceOfWorkApartAndTheListSettingDrawsTheList()
    {
        var id = await ThreeBuilders().ConfigureAwait(false);
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project, resume = id }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.agent-flow-work').length", "3").ConfigureAwait(false);

            await window.Until("document.querySelector('.agent-flow-root-name').textContent", "brain").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.agent-flow-lone .agent-flow-agent').length", "1").ConfigureAwait(false);
            await window.Until("[...document.querySelectorAll('.agent-flow-work')].map((work) => work.dataset.end).join()", "landed,review,building").ConfigureAwait(false);
            const string Lens = "document.querySelectorAll('.agent-flow-work')[0]";
            await window.Until($"[...{Lens}.querySelectorAll('.agent-flow-check')].map((check) => check.dataset.verdict).join()", "NOT READY,READY,READY").ConfigureAwait(false);
            await window.Until($"[...{Lens}.querySelectorAll('.agent-flow-step')].map((step) => step.dataset.way).join()", "forth,back,forth,back,forth").ConfigureAwait(false);
            await window.Until($"[...{Lens}.querySelectorAll('.agent-flow-tree')].map((tree) => tree.dataset.opened).join()", "true,false").ConfigureAwait(false);
            await window.Until($"{Lens}.querySelector('.agent-flow-end-range').textContent", "cbfdcf1..8124cb8").ConfigureAwait(false);
            const string Dial = "document.querySelectorAll('.agent-flow-work')[1]";
            await window.Until($"{Dial}.querySelector('.agent-flow-end').textContent.trim()", "The validator is in round 1").ConfigureAwait(false);
            await window.Until($"[...{Dial}.querySelectorAll('.agent-flow-tree')].map((tree) => tree.dataset.opened).join()", "true").ConfigureAwait(false);

            await window.Eval($"{Lens}.querySelectorAll('.agent-flow-step')[1].click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-main .pane-title')?.textContent", "Validator: Validate the lens").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-head .button').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.agent-flow-work').length", "3").ConfigureAwait(false);

            await window.Eval("document.querySelector('.icon[aria-label=\"Settings\"]').click()").ConfigureAwait(false);
            await window.Eval("[...document.querySelectorAll('.segmented[aria-label=\"Agents panel\"] button')].find((one) => one.textContent === 'List').click()").ConfigureAwait(false);
            await window.Until("document.querySelector('.segmented[aria-label=\"Agents panel\"] [aria-pressed=\"true\"]')?.textContent", "List").ConfigureAwait(false);
            await window.Eval("document.querySelector('.code-session').click()").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.code-work').length", "3").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.agent-flow').length", "0").ConfigureAwait(false);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static DateTimeOffset At(int second) => DateTimeOffset.Parse(Stamp(second), System.Globalization.CultureInfo.InvariantCulture);

    private static JsonElement Listed(List<JsonElement> agents, string tool) => agents.Single((one) => one.GetProperty("tool").GetString() == tool);

    private static JsonElement Worktree(List<JsonElement> agents, string tool) => Listed(agents, tool).GetProperty("worktree");

    /// <summary>Writes the session of three builders and an Explore agent; its id.</summary>
    private async Task<string> ThreeBuilders()
    {
        Directory.CreateDirectory(Path.Combine(project, ".claude", "worktrees", "dial"));
        Directory.CreateDirectory(Path.Combine(project, ".claude", "worktrees", "agent-b3"));
        var lens = Path.Combine(project, ".claude", "worktrees", "lens");
        var id = Guid.NewGuid().ToString("D");
        (string Tool, string Type)[] calls =
        [
            ("toolu_r1", "brain-ready"), ("toolu_r2", "brain-ready"), ("toolu_b1", "builder"), ("toolu_r4", "brain-ready"), ("toolu_b2", "builder"), ("toolu_x", "Explore"),
            ("toolu_r3", "brain-ready"), ("toolu_v1", "validator"), ("toolu_r5", "brain-ready"), ("toolu_v2", "validator"), ("toolu_r6", "brain-ready"), ("toolu_b3", "builder"),
        ];
        var lines = new List<string> { $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build three things"}}""" };
        lines.AddRange(calls.Select((call) => Call(call.Tool, call.Type, call.Tool)));
        WriteTranscript("q", id, [.. lines]);
        var folder = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(folder);
        await Sent(folder, "r1", "brain-ready", "toolu_r1", "Check the lens", true, Brief(0, "Build the lens, draft."), Said(40, "NOT READY: which repo does it build in?")).ConfigureAwait(false);
        await Sent(folder, "r2", "brain-ready", "toolu_r2", "Check the lens", true, Brief(60, "Build the lens."), Said(90, "READY")).ConfigureAwait(false);
        await Sent(
            folder, "b1", "builder", "toolu_b1", "Build the lens", true,
            Brief(120, "Build the lens."),
            Bash(180, "w1", $"git -C {project} worktree add .claude/worktrees/lens -b lens main"), BashResult(185, "w1", "Preparing worktree (new branch 'lens')"),
            Bash(800, "c1"), BashResult(805, "c1", "[lens 1234abc] Draw the lens"),
            Handback(900, "Built the lens."),
            Peer(1200, "v1", "1. No test."), Bash(1300, "c2"), BashResult(1305, "c2", "[lens 2345bcd] Test the lens"), Handback(1500, "Tested."),
            Peer(1700, "v1", "2. Name it."), Bash(1800, "c3"), BashResult(1805, "c3", "[lens 3456cde] Name the lens"), Handback(1900, "Named.")).ConfigureAwait(false);
        await Sent(folder, "r4", "brain-ready", "toolu_r4", "Check the dial", true, Brief(300, "Build the dial."), Said(330, "READY")).ConfigureAwait(false);
        await Sent(
            folder, "b2", "builder", "toolu_b2", "Build the dial", false,
            Brief(360, "Build the dial."),
            Bash(400, "w2", $"cd {project} && git worktree add .claude/worktrees/dial -b dial main"), BashResult(405, "w2", "Preparing worktree (new branch 'dial')"),
            Handback(1300, "Built the dial."),
            Peer(2000, "v2", "1. The dial has no stop.")).ConfigureAwait(false);
        await Sent(folder, "x", "Explore", "toolu_x", "Find the old gauge", true, Brief(500, "Where is the old gauge?"), Said(560, "In src/Gauge.cs.")).ConfigureAwait(false);
        await Sent(folder, "r3", "brain-ready", "toolu_r3", "Check the lens review", true, Brief(1000, LensReview), Said(1020, "READY")).ConfigureAwait(false);
        await Sent(
            folder, "v1", "validator", "toolu_v1", "Validate the lens", true,
            Brief(1050, LensReview), Message(1200, "b1", "1. No test."), Message(1700, "b1", "2. Name it."),
            Bash(2000, "l1", $"git -C {project} merge --ff-only lens"), BashResult(2005, "l1", Landing),
            Bash(2030, "stale", $"git -C {project} worktree remove {project}/.claude/worktrees/stale"), BashResult(2035, "stale", ""),
            Bash(2050, "rm", $"git -C {project} worktree remove {lens}"), BashResult(2055, "rm", ""),
            Handback(2100, "Landed.")).ConfigureAwait(false);
        await Sent(folder, "r5", "brain-ready", "toolu_r5", "Check the dial review", true, Brief(1400, DialReview), Said(1420, "READY")).ConfigureAwait(false);
        await Sent(folder, "v2", "validator", "toolu_v2", "Validate the dial", false, Brief(1450, DialReview), Message(2000, "b2", "1. The dial has no stop."), Bash(2100, "t2", "tests/run.sh")).ConfigureAwait(false);
        await Sent(folder, "r6", "brain-ready", "toolu_r6", "Check the gauge", true, Brief(2200, "Build the gauge."), Said(2220, "READY")).ConfigureAwait(false);
        var given = Path.Combine(project, ".claude", "worktrees", "agent-b3");
        await SentWith(folder, "b3", "builder", "toolu_b3", "Build the gauge", false, $$$""","worktreePath":"{{{given}}}" """, Brief(2250, "Build the gauge."), Bash(2400, "t3", "scripts/dotnet-guard.sh build brain.slnx")).ConfigureAwait(false);
        return id;
    }

    /// <summary>Writes one agent's meta and transcript.</summary>
    private static Task Sent(string folder, string agent, string type, string tool, string description, bool finished, params string[] lines) =>
        SentWith(folder, agent, type, tool, description, finished, string.Empty, lines);

    /// <summary>Writes one agent's meta, with <paramref name="extra"/> at its end, and its transcript.</summary>
    private static Task SentWith(string folder, string agent, string type, string tool, string description, bool finished, string extra, params string[] lines)
    {
        var done = finished ? "true" : "false";
        return Agent(folder, agent, $$$"""{"agentType":"{{{type}}}","toolUseId":"{{{tool}}}","description":"{{{description}}}","finished":{{{done}}}{{{extra}}}}""", lines);
    }
}
