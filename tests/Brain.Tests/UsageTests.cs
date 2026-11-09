using System.Globalization;
using System.Text.RegularExpressions;
using Brain.Staging;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// The usage log records facts as they happen: every metered CLI call with what it returned or opened, every answer to
/// a pending write once, and every Agent, SendMessage and Skill call the hook hands to <c>brain record</c>.
/// </summary>
public sealed partial class UsageTests : IDisposable
{
    private readonly TempBrain brain = new();

    public UsageTests()
    {
        brain.Session = "s-1";
        brain.Ok("add", "--subjects", "test", "global", "tea", "The user drinks tea", "--kind", "idea", "--session", "s-0");
        File.Delete(LogFile());
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ALookupLogsItsCallerCommandSizeAndTheRefsItReturned()
    {
        var (code, output) = Metered("lookup", "tea");

        var call = Assert.Single(Events());
        Assert.Equal(0, code);
        Assert.Equal((UsageEvent.Call, "s-1", UsageEvent.Ai, "lookup", 0), (call.Event, call.Session, call.Caller, call.Command, call.Exit));
        Assert.Equal(["tea"], call.Args);
        Assert.Equal(output.Length, call.Chars);
        Assert.Equal(["global:tea"], call.Returned);
        Assert.Null(call.Opened);
    }

    [Fact]
    public void ANodeLogsTheNodeItOpenedWithItsBirthAndTheSessionThatWroteIt()
    {
        Metered("node", "tea");

        Assert.Equal(new OpenedNode("global:tea", "2026-10-03", "s-0"), Assert.Single(Assert.Single(Events()).Opened!));
    }

    [Fact]
    public void ACallFromTheUsersTerminalAndAProjectFlagAreLoggedAsSuch()
    {
        brain.Session = null;

        Metered("--project", "nowhere", "walk", "tea");

        var call = Assert.Single(Events());
        Assert.Equal((UsageEvent.User, "walk", 1), (call.Caller, call.Command, call.Exit));
        Assert.Equal(["tea"], call.Args);
    }

    [Fact]
    public void EveryAnswerIsLoggedOnceWithTheSessionThatStagedIt()
    {
        brain.SaysYes = false;
        var store = new PendingStore(brain.Home);
        foreach (var (slug, yes, reason) in new[] { ("a", true, (string?)null), ("b", false, null), ("c", false, "say chai") })
        {
            var id = PendingId().Match(brain.Ok("add", "--subjects", "test", "global", slug, "The user drinks " + slug, "--kind", "idea")).Groups[1].Value;
            store.Ask(id);
            store.Answer(id, store.Asked().Single(asked => asked.Write.Id == id).Digest, yes, reason);
        }

        var answers = Events().Where(usage => usage.Event == UsageEvent.Answer).ToList();
        Assert.Equal(["yes", "no", "change"], answers.Select(answer => answer.Verdict));
        Assert.Equal([true, null, null], answers.Select(answer => answer.Landed));
        Assert.All(answers, answer => Assert.Equal("s-1", answer.Session));
        Assert.Equal(["The user drinks a", "The user drinks b", "The user drinks c"], answers.Select(answer => answer.Title));
        Assert.Equal([null, null, "say chai"], answers.Select(answer => answer.Words));
    }

    [Fact]
    public void RecordLogsASpawnAMessageAndASkillAsTheHookCarriesThem()
    {
        var skill = Path.Combine(brain.Root, "SKILL.md");
        File.WriteAllText(skill, "twelve chars");

        Record("""{"session_id":"s-1","tool_name":"Agent","agent_type":"brain","tool_input":{"subagent_type":"builder","name":"first","prompt":"\n  Task: build it  \nmore"},"tool_response":{"agentId":"a1","resolvedModel":"opus"}}""", skill);
        Record("""{"session_id":"s-1","tool_name":"SendMessage","agent_id":"a2","agent_type":"validator","tool_input":{"to":"a1","message":"fix it"}}""", skill);
        Record("""{"session_id":"s-1","tool_name":"Skill","tool_input":{"skill":"brain"}}""", skill);
        Record("""{"session_id":"s-1","tool_name":"Bash","tool_input":{"command":"ls"}}""", skill);

        var events = Events();
        Assert.Equal([UsageEvent.Spawn, UsageEvent.Message, UsageEvent.Skill], events.Select(usage => usage.Event));
        Assert.Equal(("builder", "opus", "a1", "first", "brain", (string?)null), (events[0].SubagentType, events[0].Model, events[0].Spawned, events[0].Name, events[0].AgentType, events[0].AgentId));
        Assert.Equal("Task: build it", events[0].Brief);
        Assert.Equal(("a2", "validator", "a1"), (events[1].AgentId, events[1].AgentType, events[1].To));
        Assert.Equal("fix it", events[1].Text);
        Assert.Equal(("brain", 12L), (events[2].SkillName, events[2].Chars));
    }

    [Fact]
    public void RecordKeepsOnlyAValidatorsTextAndCutsItOnACharacterBoundary()
    {
        var skill = Path.Combine(brain.Root, "SKILL.md");
        var pair = "\\ud83d\\ude00";
        var message = new string('a', UsageEvent.TextLimit - 1) + pair;
        Record($$$"""{"session_id":"s-1","tool_name":"SendMessage","agent_id":"v","agent_type":"validator","tool_input":{"to":"b","message":"{{{message}}}"}}""", skill);
        Record("""{"session_id":"s-1","tool_name":"SendMessage","agent_id":"x","agent_type":"brain","tool_input":{"to":"b","message":"hello"}}""", skill);

        var events = Events();
        Assert.Equal(new string('a', UsageEvent.TextLimit - 1), events[0].Text);
        Assert.Null(events[1].Text);
    }

    [Fact]
    public void RecordLogsAReadinessChecksVerdictOnceAndNoOtherStop()
    {
        Record("""{"session_id":"s-1","hook_event_name":"SubagentStop","stop_hook_active":false,"agent_id":"r1","agent_type":"brain-ready","last_assistant_message":"NOT READY\n1. Which project?"}""", null);
        Record("""{"session_id":"s-1","hook_event_name":"SubagentStop","stop_hook_active":true,"agent_id":"r1","agent_type":"brain-ready","last_assistant_message":"READY"}""", null);
        Record("""{"session_id":"s-1","hook_event_name":"SubagentStop","stop_hook_active":false,"agent_id":"b1","agent_type":"builder","last_assistant_message":"Done"}""", null);

        var verdict = Assert.Single(Events());
        Assert.Equal((UsageEvent.Readiness, "s-1", "r1", "brain-ready", "NOT READY"), (verdict.Event, verdict.Session, verdict.AgentId, verdict.AgentType, verdict.Verdict));
    }

    [Fact]
    public void RecordLogsARefusalWithItsHookToolAndReason()
    {
        Record("""{"session_id":"s-1","hook_event_name":"PreToolUse","tool_name":"Edit","agent_id":"b1","agent_type":"builder","tool_input":{"file_path":"x.cs"}}""", null, "--refused", "developer-gate", "--reason", "BLOCKED by the developer gate");

        var refusal = Assert.Single(Events());
        Assert.Equal((UsageEvent.Refusal, "s-1", "b1", "builder"), (refusal.Event, refusal.Session, refusal.AgentId, refusal.AgentType));
        Assert.Equal(("developer-gate", "Edit", "BLOCKED by the developer gate"), (refusal.Hook, refusal.Tool, refusal.Reason));
    }

    [Fact]
    public void RecordRefusesInputThatIsNoJson()
    {
        var (code, _) = Metered("record");

        Assert.Equal(1, code);
        Assert.Empty(Events());
    }

    [Fact]
    public void AHomeThatDoesNotExistLogsNothing()
    {
        var gone = Path.Combine(brain.Root, "gone");
        using var errors = new StringWriter(CultureInfo.InvariantCulture);

        UsageLog.Append(gone, new UsageEvent(UsageLog.Now(), UsageEvent.Call, null), errors);

        Assert.False(Directory.Exists(gone));
        Assert.Equal(string.Empty, errors.ToString());
    }

    private (int Code, string Out) Metered(params string[] arguments)
    {
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var code = Metering.Run(brain.Env(input, output, errors), arguments);
        return (code, output.ToString() + errors.ToString());
    }

    private void Record(string hook, string? skill, params string[] options)
    {
        using var input = new StringReader(hook);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(0, Metering.Run(brain.Env(input, output, errors) with { SkillFile = skill }, ["record", .. options]));
    }

    private List<UsageEvent> Events() => [.. UsageLog.Read(brain.Home).Events];

    private string LogFile() => Directory.GetFiles(Path.Combine(brain.Home, UsageLog.Dir)).Single();

    [GeneratedRegex("pending ([0-9a-f]{12}): nothing is written yet")]
    private static partial Regex PendingId();
}
