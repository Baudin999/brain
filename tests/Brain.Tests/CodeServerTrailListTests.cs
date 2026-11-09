using System.Text.Json;

namespace Brain.Tests;

/// <summary>
/// The agents list of a session as a trail: a readiness check attached to the agent it checked, by the brief it shares
/// with it or, when it shares none, by the next builder or validator sent, and the outline of a piece of work's steps
/// the list draws between the agents.
/// </summary>
public sealed partial class CodeServerTests
{
    private const string Landing = "Updating cbfdcf1..8124cb8\\nFast-forward\\n README.md | 23 +++";
    private const string Review = "Validate the lens. Builder agent ID: b1.";

    [Fact]
    public async Task APieceOfWorkListsItsChecksAndItsStepsAsOutlinedTrail()
    {
        var id = await SentAgentsOf(
            ("toolu_r1", "brain-ready", "r1", true, [Brief(0, "Build the lens."), Said(1, "READY")]),
            ("toolu_b", "builder", "b1", true, [Brief(2, "Build the lens."), Handback(4, "Built the lens."), Peer(7, "v1", "1. No test."), Bash(8, "c1"), BashResult(9, "c1", "[worktree-agent-b1 1234abc] Test the lens"), Handback(10, "Tested.")]),
            ("toolu_r2", "brain-ready", "r2", true, [Brief(5, Review), Said(5, "READY")]),
            ("toolu_v", "validator", "v1", true, [Brief(6, Review), Message(7, "b1", "1. No test."), Bash(11, "c2"), BashResult(12, "c2", Landing), Handback(13, "Landed.")]));

        var agents = await ListedAgents(id);

        Assert.Equal(["toolu_r1", "toolu_b", "toolu_r2", "toolu_v"], agents.Select((one) => one.GetProperty("tool").GetString()!));
        Assert.Equal(["toolu_b", null, "toolu_v", null], agents.Select((one) => One(one, "attached")));
        var steps = agents[1].GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(["report", "findings", "answer", "verdict"], steps.Select((one) => one.GetProperty("kind").GetString()!));
        Assert.Equal(["toolu_b", "toolu_v", "toolu_b", "toolu_v"], steps.Select((one) => one.GetProperty("tool").GetString()!));
        Assert.Equal([0, 1, 1], steps[..3].Select((one) => one.GetProperty("round").GetInt32()));
        Assert.Equal([0, 0, 1, 0], steps.Select((one) => one.GetProperty("commits").GetInt32()));
        Assert.Equal("cbfdcf1..8124cb8", steps[3].GetProperty("landed").GetString());
        Assert.False(agents[0].TryGetProperty("steps", out _));
    }

    [Fact]
    public async Task ACheckWhoseBriefMatchesNoAgentAttachesToTheNextOneSentAndOneAfterAllStandsAlone()
    {
        var id = await SentAgentsOf(
            ("toolu_r1", "brain-ready", "r1", true, [Brief(0, "Build the lens, draft."), Said(1, "NOT READY: the brief names no repo.")]),
            ("toolu_r2", "brain-ready", "r2", true, [Brief(2, "Build the lens."), Said(3, "READY")]),
            ("toolu_b", "builder", "b1", false, [Brief(4, "Build the lens."), Said(5, "Working on it.\\nMore.")]),
            ("toolu_r3", "brain-ready", "r3", true, [Brief(6, "Validate something else."), Said(7, "READY")]));

        var agents = await ListedAgents(id);

        Assert.Equal(["toolu_b", "toolu_b", null, null], agents.Select((one) => One(one, "attached")));
        Assert.Equal("Working on it.", One(agents[2], "now"));
        Assert.Null(One(agents[0], "now"));
    }

    private static string? One(JsonElement agent, string name)
    {
        if (agent.TryGetProperty(name, out var found))
        {
            return found.GetString();
        }

        return null;
    }

    private async Task<List<JsonElement>> ListedAgents(string id)
    {
        var token = await Trade(Code(windows[0])).ConfigureAwait(false);
        await Send(HttpMethod.Post, "/api/start", Json(new { agent = "brain", cwd = project, resume = id }), token: token, origin: server.Origin).ConfigureAwait(false);
        var listed = await Json(await Send(HttpMethod.Get, $"/api/sent?session={id}", token: token).ConfigureAwait(false)).ConfigureAwait(false);
        return [.. listed.RootElement.GetProperty("agents").EnumerateArray().Select((one) => one.Clone())];
    }

    /// <summary>Writes a session that sent the given agents, each as its call's tool id, type, agent id, whether it finished and its transcript's lines; its id.</summary>
    private async Task<string> SentAgentsOf(params (string Tool, string Type, string Agent, bool Finished, string[] Lines)[] sent)
    {
        var id = Guid.NewGuid().ToString("D");
        var lines = new List<string> { $$$"""{"type":"user","cwd":"{{{project}}}","message":{"role":"user","content":"build it"}}""" };
        lines.AddRange(sent.Select((one) => Call(one.Tool, one.Type, one.Agent)));
        WriteTranscript("q", id, [.. lines]);
        var agents = Path.Combine(config, "projects", "q", id, "subagents");
        Directory.CreateDirectory(agents);
        foreach (var one in sent)
        {
            var finished = one.Finished ? "true" : "false";
            await Agent(agents, one.Agent, $$$"""{"agentType":"{{{one.Type}}}","toolUseId":"{{{one.Tool}}}","description":"{{{one.Agent}}}","finished":{{{finished}}}}""", one.Lines).ConfigureAwait(false);
        }

        return id;
    }
}
