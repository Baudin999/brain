using System.Text.Json;

namespace Brain.Tests;

/// <summary>brain stats reads the usage log and prints every metric's section from the facts it holds.</summary>
public sealed class StatsTests : IDisposable
{
    private static readonly string[] Log =
    [
        """{"at":"2026-11-01T09:00:00.000+01:00","event":"skill","session":"s-1","skillName":"brain","chars":4000}""",
        """{"at":"2026-11-01T09:00:01.000+01:00","event":"call","session":"s-1","caller":"ai","command":"lookup","args":["tea"],"chars":400,"ms":5,"exit":0,"returned":["global:tea","global:chai"]}""",
        """{"at":"2026-11-01T09:00:02.000+01:00","event":"call","session":"s-1","caller":"ai","command":"node","args":["tea"],"chars":800,"ms":5,"exit":0,"opened":[{"ref":"global:tea","born":"2026-10-22","session":"s-0"}]}""",
        """{"at":"2026-11-01T09:00:03.000+01:00","event":"call","session":"s-1","caller":"ai","command":"lookup","args":["cups"],"chars":40,"ms":5,"exit":0,"returned":["global:cups"]}""",
        """{"at":"2026-11-02T10:00:00.000+01:00","event":"call","session":"s-2","caller":"ai","command":"walk","args":["tea"],"chars":2000,"ms":5,"exit":0,"opened":[{"ref":"global:tea","born":"2026-10-22","session":"s-0"}]}""",
        """{"at":"2026-11-02T10:00:01.000+01:00","event":"call","caller":"user","command":"node","args":["mine"],"chars":400,"ms":5,"exit":0,"opened":[{"ref":"personal:mine","born":"2026-11-02","session":"s-2"}]}""",
        """{"at":"2026-11-02T10:00:02.000+01:00","event":"answer","session":"s-2","id":"0123456789ab","verdict":"yes","landed":true}""",
        """{"at":"2026-11-02T10:00:03.000+01:00","event":"answer","session":"s-2","id":"0123456789ac","verdict":"change","title":"The user drinks tea","words":"say chai"}""",
        """{"at":"2026-11-02T10:00:04.000+01:00","event":"answer","session":"s-2","id":"0123456789ad","verdict":"no"}""",
        """{"at":"2026-11-02T11:00:00.000+01:00","event":"spawn","session":"s-2","agentType":"brain","subagentType":"builder","model":"opus","spawned":"b1"}""",
        """{"at":"2026-11-02T11:00:01.000+01:00","event":"spawn","session":"s-2","agentType":"brain","subagentType":"builder","spawned":"b2","name":"second"}""",
        """{"at":"2026-11-02T11:00:02.000+01:00","event":"spawn","session":"s-2","agentType":"brain","subagentType":"validator","spawned":"v1"}""",
        """{"at":"2026-11-02T11:00:03.000+01:00","event":"message","session":"s-2","agentId":"v1","agentType":"validator","to":"b1","text":"missing test\nsecond"}""",
        """{"at":"2026-11-02T11:00:04.000+01:00","event":"message","session":"s-2","agentId":"v1","agentType":"validator","to":"b1"}""",
        """{"at":"2026-11-02T11:00:05.000+01:00","event":"message","session":"s-2","agentId":"v1","agentType":"validator","to":"main"}""",
        """{"at":"2026-11-02T11:00:05.500+01:00","event":"message","session":"s-9","agentId":"v9","agentType":"validator","to":"b1"}""",
        """{"at":"2026-11-02T11:00:06.000+01:00","event":"message","session":"s-2","agentType":"brain","to":"second"}""",
        """{"at":"2026-11-02T12:00:00.000+01:00","event":"spawn","session":"s-2","agentType":"brain","subagentType":"brain-ready","spawned":"r1","brief":"Task: an Evidence tab"}""",
        """{"at":"2026-11-02T12:00:01.000+01:00","event":"readiness","session":"s-2","agentId":"r1","agentType":"brain-ready","verdict":"NOT READY"}""",
        """{"at":"2026-11-02T12:00:02.000+01:00","event":"readiness","session":"s-2","agentId":"r2","agentType":"brain-ready","verdict":"READY"}""",
        """{"at":"2026-11-02T12:00:03.000+01:00","event":"refusal","session":"s-2","agentId":"b1","agentType":"builder","hook":"developer-gate","tool":"Edit","reason":"BLOCKED by the developer gate"}""",
        """{"at":"2026-11-02T12:00:04.000+01:00","event":"refusal","session":"s-2","agentType":"brain","hook":"graph-door","tool":"Write","reason":"BLOCKED: the graph"}""",
        """{"at":"2026-11-02T12:00:05.000+01:00","event":"refusal","session":"s-2","agentId":"b1","agentType":"builder","hook":"developer-gate","tool":"Bash","reason":"BLOCKED by the developer gate"}""",
        "not json",
    ];

    private readonly TempBrain brain = new();

    public StatsTests()
    {
        Directory.CreateDirectory(Path.Combine(brain.Home, "usage"));
        File.WriteAllLines(Path.Combine(brain.Home, "usage", "2026-11.jsonl"), Log);
    }

    public void Dispose() => brain.Dispose();

    [Fact]
    public void AValidatorCaseCarriesWhatEachRoundWroteAndNullForARoundWithoutText()
    {
        using var report = System.Text.Json.JsonDocument.Parse(brain.Ok("stats", "--json"));
        var validator = report.RootElement.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("title").GetString() == "Validator");
        var rounds = validator.GetProperty("cases")[0].GetProperty("rounds").EnumerateArray().Select(round => round.GetString()).ToList();

        Assert.Equal(["missing test\nsecond", null], rounds);
    }

    [Fact]
    public void StatsPrintsEverySectionFromTheLog()
    {
        var output = brain.Ok("stats");

        Assert.Contains("brain stats, all time: 23 events. Tokens are estimates: output characters divided by 4.", output, StringComparison.Ordinal);
        Assert.Contains("1 lines of the usage log could not be read and are left out.", output, StringComparison.Ordinal);
        Assert.Contains("  walk                 1             500", output, StringComparison.Ordinal);
        Assert.Contains("  total                5             910", output, StringComparison.Ordinal);
        Assert.Contains("  AI sessions: 4 calls, 810 tokens (est.); your terminal: 1 calls, 100 tokens (est.)", output, StringComparison.Ordinal);
        Assert.Contains("  brain skill: 1,000 tokens (est.) per load, 1 loads in 1 sessions, 1,000 tokens (est.) in total", output, StringComparison.Ordinal);
        Assert.Contains("  2 reads of 1 nodes in 2 AI sessions; 1 more from your terminal", output, StringComparison.Ordinal);
        Assert.Contains("  2 of 2 reads (100%) opened a node written in another session; 1 nodes served a session that did not write them", output, StringComparison.Ordinal);
        Assert.Contains("  age when read: median 11 days, oldest 11 days", output, StringComparison.Ordinal);
        Assert.Contains("  global:tea                                    2         2  10-11", output, StringComparison.Ordinal);
        Assert.Contains("  1 of 2 (50%) were followed in the same session by opening a ref they returned", output, StringComparison.Ordinal);
        Assert.Contains("  3 answers: 1 yes, 1 no, 1 no with words", output, StringComparison.Ordinal);
        Assert.Contains("  Without brain, your agent would have gone off the rails 1 time. You corrected it.\n    - The user drinks tea: say chai", output, StringComparison.Ordinal);
        Assert.Contains("  Without brain's validator, 1 of 2 builds would have shipped with problems.\n    - b1: 2 rounds of findings, session s-2", output, StringComparison.Ordinal);
        Assert.Contains("  Without brain's graph, your agents would have started from zero 2 times, not knowing what you decided up to 11 days earlier.\n    - global:tea: 10 days old, read in session s-1\n    - global:tea: 11 days old, read in session s-2", output, StringComparison.Ordinal);
        Assert.Contains("  Without the readiness check, 1 brief would have gone to a builder with questions only you could answer.\n    - Task: an Evidence tab: NOT READY, session s-2", output, StringComparison.Ordinal);
        Assert.Contains("  2 readiness checks: 1 READY, 1 NOT READY, 0 other", output, StringComparison.Ordinal);
        Assert.Contains("  Without brain's guards, agents would have done 3 things they must not: 2 building before the go, 1 hand-editing the graph.\n    - developer-gate: refused Edit for builder: BLOCKED by the developer gate", output, StringComparison.Ordinal);
        Assert.Contains("  Over the same period brain cost an estimated 1,910 tokens: 5 calls of the CLI and 1 load of the brain skill.", output, StringComparison.Ordinal);
        Assert.Contains("  2 rounds of findings sent to 1 builders; 1 of 2 builders got none", output, StringComparison.Ordinal);
        Assert.Contains("  b1                  s-2                                        2", output, StringComparison.Ordinal);
        Assert.Contains("  3 messages sent to 2 builders after they started: 2 from validator, 1 from brain", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ARangeKeepsOnlyTheDaysInIt()
    {
        var output = brain.Ok("stats", "--since", "2026-11-01", "--until", "2026-11-01");

        Assert.Contains("brain stats, from 2026-11-01 to 2026-11-01: 4 events.", output, StringComparison.Ordinal);
        Assert.Contains("  0 answers: 0 yes, 0 no, 0 no with words", output, StringComparison.Ordinal);
        Assert.Contains("  Without brain's guards, agents would have done 0 things they must not.", output, StringComparison.Ordinal);
        Assert.Contains("  Without the readiness check, 0 briefs would have gone to a builder with questions only you could answer.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCarriesEverySection()
    {
        using var report = JsonDocument.Parse(brain.Ok("stats", "--json"));

        var sections = report.RootElement.GetProperty("sections").EnumerateArray().ToList();
        Assert.Equal(
            ["Saves", "Validator", "Knowledge reuse", "Readiness", "Guards", "Judge", "Cost", "Hit rate", "Rework"],
            sections.Select(section => section.GetProperty("title").GetString()));
        Assert.Equal(1, report.RootElement.GetProperty("unreadable").GetInt32());
        var correction = Assert.Single(sections[0].GetProperty("cases").EnumerateArray());
        Assert.Equal(("The user drinks tea", "say chai"), (correction.GetProperty("subject").GetString(), correction.GetProperty("detail").GetString()));
        Assert.Equal(JsonValueKind.Null, sections[6].GetProperty("cases").ValueKind);
        Assert.Equal(JsonValueKind.Null, sections[7].GetProperty("story").ValueKind);
    }

    [Fact]
    public void TheTextPrintsFiveCasesAndCountsTheRestWhileJsonKeepsEvery()
    {
        var corrections = new List<string>();
        for (var answer = 0; answer < 7; answer++)
        {
            corrections.Add($$"""{"at":"2026-11-03T10:00:0{{answer}}.000+01:00","event":"answer","session":"s-3","id":"00000000000{{answer}}","verdict":"change","title":"Write {{answer}}","words":"fix {{answer}}"}""");
        }

        File.WriteAllLines(Path.Combine(brain.Home, "usage", "2026-11.jsonl"), corrections);

        var output = brain.Ok("stats");
        using var report = JsonDocument.Parse(brain.Ok("stats", "--json"));

        Assert.Contains("    - Write 4: fix 4\n    and 2 more; brain stats --json and the Evidence tab in brain's window list every case", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Write 5", output, StringComparison.Ordinal);
        Assert.Equal(7, report.RootElement.GetProperty("sections")[0].GetProperty("cases").GetArrayLength());
    }

    [Fact]
    public void AWrongDayIsRefused()
    {
        var (code, _, errors) = brain.Run("stats", "--since", "yesterday");

        Assert.Equal(1, code);
        Assert.Contains("--since takes a day as yyyy-MM-dd: yesterday", errors, StringComparison.Ordinal);
    }
}
