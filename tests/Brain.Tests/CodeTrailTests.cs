using System.Text.Json;
using Brain.Code;
using Brain.Stats;

namespace Brain.Tests;

/// <summary>
/// The trail of a piece of work as brain code reads it from real transcript shapes: a validator's findings reach its
/// builder as a message whose origin is a peer's, a validator sends them with SendMessage to the builder's agent id, a
/// builder answers with SendMessage or hands its report back, a commit shows in a Bash result as git prints it, and a
/// landing as git's fast-forward. Navarone's signals end as its event log tells them, and a clash is one its disputes
/// hold.
/// </summary>
public sealed class CodeTrailTests
{
    private const string Builder = "a6bc33033f88b2057";
    private const string Validator = "aeb4a3934e995aec2";

    [Fact]
    public void APeersMessageAndAMessageSentReadWholeWithWhenTheyWereWritten()
    {
        var findings = """{"type":"user","isMeta":true,"timestamp":"2026-11-06T19:37:06.877Z","origin":{"kind":"peer","from":"aeb4a3934e995aec2","name":"validator","body":"1. BLOCKER code.css is empty."},"message":{"role":"user","content":"Another Claude session sent a message"}}""";
        var answer = """{"type":"assistant","timestamp":"2026-11-06T19:41:00Z","message":{"content":[{"type":"tool_use","id":"t2","name":"SendMessage","input":{"to":"aeb4a3934e995aec2","message":"All 8 findings fixed."}}]}}""";

        var heard = Transcript.Events(findings).Single();
        var sent = Transcript.Events(answer).Single();

        Assert.Equal(new CodeEvent(CodeEvent.Message, Text: "1. BLOCKER code.css is empty.", Id: Validator, Name: "validator", At: DateTimeOffset.Parse("2026-11-06T19:37:06.877Z")), heard);
        Assert.Equal((CodeEvent.Tool, "SendMessage", Validator, "All 8 findings fixed."), (sent.Kind, sent.Name, sent.Subject, sent.Text));
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T19:41:00Z"), sent.At);
    }

    [Fact]
    public void TheTrailTellsTheReportEachRoundAndTheLandingInTheOrderTheyHappened()
    {
        var builder = new Worker(Builder, [
            Bash("b1", 1),
            Result("b1", 2, "[worktree-agent-x 70125ec] brain code takes a folder\n 3 files changed"),
            Handback(3, "Built it. The gate is green."),
            new CodeEvent(CodeEvent.Message, Text: "1. BLOCKER", Id: Validator, Name: "validator", At: At(5)),
            Bash("b2", 6),
            Result("b2", 7, "[worktree-agent-x 8124cb8] brain code takes a folder, and says why"),
            Send("b3", 8, Validator, "All findings fixed in 8124cb8."),
            Handback(9, "All findings fixed; report."),
        ], Done: true);
        var validator = new Worker(Validator, [
            Send("v1", 4, Builder, "1. BLOCKER"),
            Bash("v2", 10),
            Result("v2", 11, "Updating cbfdcf1..8124cb8\nFast-forward\n README.md | 23 +++"),
            Handback(12, "Landed 8124cb8 on main."),
        ], Done: true);

        var steps = Trail.Of(builder, [validator]);

        Assert.Equal([Trail.Report, Trail.Findings, Trail.Answer, Trail.Verdict], steps.Select((step) => step.Kind));
        Assert.Equal(["Built it. The gate is green.", "1. BLOCKER", "All findings fixed; report.", "Landed 8124cb8 on main."], steps.Select((step) => step.Text));
        Assert.Equal([new CodeCommit("70125ec", "brain code takes a folder")], steps[0].Commits);
        Assert.Equal([new CodeCommit("8124cb8", "brain code takes a folder, and says why")], steps[2].Commits);
        Assert.Equal("cbfdcf1..8124cb8", steps[3].Landed);
        Assert.Null(steps[1].Landed);
        Assert.Equal([0, 1, 1, null], steps.Select((step) => step.Round));
    }

    [Fact]
    public void ABuilderStillWorkingHasNoReportAndAFinishedOneReportsItsLastWords()
    {
        IReadOnlyList<CodeEvent> words = [new CodeEvent(CodeEvent.Said, Text: "Done: it works.", At: At(1))];

        var working = Trail.Of(new Worker(Builder, words, Done: false), []);
        var finished = Trail.Of(new Worker(Builder, words, Done: true), []);

        Assert.Empty(working);
        Assert.Equal(["Done: it works."], finished.Select((step) => step.Text));
    }

    [Theory]
    [InlineData("Updating a1b2c3d..d4e5f6a\nFast-forward\n x | 1 +", "a1b2c3d..d4e5f6a")]
    [InlineData("Already up to date.", null)]
    [InlineData("Updating a1b2c3d..d4e5f6a\nerror: Your local changes would be overwritten", null)]
    public void OnlyAFastForwardIsALanding(string output, string? range)
    {
        var validator = new Worker(Validator, [Bash("v1", 1), Result("v1", 2, output), Handback(3, "Done.")], Done: true);

        var verdict = Trail.Of(new Worker(Builder, [], Done: false), [validator]).Single();

        Assert.Equal(range, verdict.Landed);
    }

    [Fact]
    public void EachSignalEndsAsItsEventsTellIt()
    {
        const string tree = "/repo/.claude/worktrees/w";
        var log = Log(
            Event("raised", "0000001", tree),
            Event("fixed", "0000001", tree, """ "before":"c1","after":"c2" """),
            Event("accepted", "0000001", tree),
            Event("raised", "0000002", tree),
            Event("resolved", "0000002", tree, """ "reason":"One mapping.","defended":{"code":"c3"} """),
            Event("accepted", "0000002", tree),
            Event("raised", "0000003", tree),
            Event("resolved", "0000003", tree, """ "reason":"Fine.","defended":{"code":"c4"} """),
            Event("refused-reason", "0000003", tree, """ "finding":"It crams three steps.","before":"c4" """),
            Event("raised", "0000004", tree),
            Event("resolved", "0000004", tree, """ "reason":"Fine.","defended":{"code":"c5"} """),
            Event("refused-reason", "0000004", tree, """ "finding":"It hides a test.","before":"c5" """),
            Event("resolved", "0000004", tree, """ "reason":"Still fine.","defended":{"code":"c5"} """),
            Event("raised", "0000005", tree),
            Event("resolved", "0000005", tree, """ "reason":"A.","defended":{"code":"c6"} """),
            Event("refused-reason", "0000005", tree, """ "finding":"B.","before":"c6" """),
            Event("resolved", "0000005", tree, """ "reason":"C.","defended":{"code":"c7"} """),
            Event("settled", "0000004", tree, """ "user":"replace","words":"Split it." """),
            Event("raised", "0000009", "/repo/.claude/worktrees/other"));

        var ended = WorkSignals.Ended(log, new HashSet<(string, string)>(), tree);

        Assert.Equal(["0000001", "0000002", "0000003", "0000005", "0000004"], ended.Select((signal) => signal.Id));
        Assert.Equal([WorkSignals.Fixed, WorkSignals.Kept, WorkSignals.Replaced, WorkSignals.Answered, WorkSignals.Judged], ended.Select((signal) => signal.Ended));
        Assert.Equal(("It crams three steps.", "Fine."), (ended[2].Finding, ended[2].Reason));
        Assert.Equal(("replace", "Split it."), (ended[4].Judgement, ended[4].Words));
        Assert.Equal("CodeSessions.Start", ended[0].Name);
    }

    /// <summary>Whether a signal is a clash is Navarone's to say, through its disputes: brain never works it out from the log.</summary>
    [Fact]
    public void ASignalNavaronesDisputesHoldIsAClashWaitingForAJudge()
    {
        const string tree = "/repo/w";
        var log = Log(
            Event("resolved", "0000004", tree, """ "reason":"Fine." """),
            Event("refused-reason", "0000004", tree, """ "finding":"It hides a test.","before":"c5" """),
            Event("resolved", "0000004", tree, """ "reason":"Still fine." """));

        var clash = WorkSignals.Ended(log, new HashSet<(string, string)> { (tree, "0000004") }, tree).Single();
        var answered = WorkSignals.Ended(log, new HashSet<(string, string)> { ("/repo/other", "0000004") }, tree).Single();

        Assert.Equal((WorkSignals.Clash, "Still fine.", "It hides a test."), (clash.Ended, clash.Reason, clash.Finding));
        Assert.Equal(WorkSignals.Answered, answered.Ended);
    }

    /// <summary>
    /// When messages cross, each step keeps its own round: the findings by their place among the validator's messages,
    /// the builder's answer by how many it had been sent before it, as in a session where a validator resent its findings
    /// as a new round before the builder's answer to the last one came.
    /// </summary>
    [Fact]
    public void ARoundIsTheServersToNumberEvenWhenMessagesCross()
    {
        var builder = new Worker(Builder, [
            Handback(1, "Report."),
            Peer(3, "Round 1."),
            Handback(5, "Answer to round 1."),
            Peer(11, "Round 2."),
            Handback(13, "Answer to round 2."),
            Peer(14, "Round 3."),
        ], Done: false);
        var validator = new Worker(Validator, [Send("v1", 2, Builder, "Round 1."), Send("v2", 10, Builder, "Round 2."), Send("v3", 12, Builder, "Round 3.")], Done: false);

        var steps = Trail.Of(builder, [validator]);

        Assert.Equal(["Report.", "Round 1.", "Answer to round 1.", "Round 2.", "Round 3.", "Answer to round 2."], steps.Select((step) => step.Text));
        Assert.Equal([0, 1, 1, 2, 3, 2], steps.Select((step) => step.Round));
    }

    private static DateTimeOffset At(int second) => new(2026, 11, 6, 19, 0, second, TimeSpan.Zero);

    private static CodeEvent Bash(string id, int second) => new(CodeEvent.Tool, Id: id, Name: "Bash", Subject: "Run it", At: At(second));

    private static CodeEvent Peer(int second, string text) => new(CodeEvent.Message, Text: text, Id: Validator, Name: "validator", At: At(second));

    private static CodeEvent Result(string id, int second, string text) => new(CodeEvent.Result, Text: text, Id: id, At: At(second));

    private static CodeEvent Send(string id, int second, string to, string text) => new(CodeEvent.Tool, Text: text, Id: id, Name: "SendMessage", Subject: to, At: At(second));

    private static CodeEvent Handback(int second, string text) => new(CodeEvent.Tool, Text: text, Id: "h" + second, Name: "SubagentHandback", At: At(second));

    private static string Event(string type, string id, string worktree, string more = "")
    {
        var extra = more.Trim();
        if (extra.Length > 0)
        {
            extra = "," + extra;
        }

        return $$"""{"type":"{{type}}","at":"2026-11-06T19:00:00Z","repo":"/repo","worktree":"{{worktree}}","id":"{{id}}","check":"cyclomatic","symbol":"brain::Brain.Code.CodeSessions.Start(string)"{{extra}}}""";
    }

    private static List<NavaroneEvent> Log(params string[] lines)
    {
        var events = new List<NavaroneEvent>();
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            events.Add(NavaroneEvent.Parse(document.RootElement, events.Count)!);
        }

        return events;
    }
}
