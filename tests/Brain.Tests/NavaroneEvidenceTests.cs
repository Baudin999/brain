using System.Text.Json;
using Brain.Stats;

namespace Brain.Tests;

/// <summary>The Navarone lines of the evidence, read through the events column of sources.tsv.</summary>
public sealed class NavaroneEvidenceTests : IDisposable
{
    private readonly TempBrain brain = new();

    [Fact]
    public void WithoutANavaroneLineThereIsNoNavaroneSection()
    {
        var report = Metrics.Measure(brain.Home, null, null);

        Assert.DoesNotContain(report.Sections, section => section.Title.StartsWith("Navarone", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailingEventsCommandLeavesTheSectionsOutAndNothingBroken()
    {
        File.WriteAllText(Path.Combine(brain.Home, "sources.tsv"), "navarone\ttrue\ttrue\tfalse\ttrue\n");

        var report = Metrics.Measure(brain.Home, null, null);

        Assert.DoesNotContain(report.Sections, section => section.Title.StartsWith("Navarone", StringComparison.Ordinal));
    }

    [Fact]
    public void ProblemsCountOncePerWorktreeAndIdAndNameTheirProblem()
    {
        var events = new[]
        {
            Event("run", "/w1", ("tokens", 100)),
            Event("cost", "/w1", ("command", "lens"), ("tokens", 20)),
            Event("fixed", "/w1", ("id", "1"), ("check", "cc"), ("symbol", "p::N.A.B(int)"), ("before", "aaa"), ("after", "bbb"), ("secondRound", false)),
            Event("refused-fix", "/w1", ("id", "1"), ("check", "cc"), ("symbol", "p::N.A.B(int)"), ("finding", "still long"), ("before", "aaa"), ("after", "bbb")),
            Event("fixed", "/w2", ("id", "1"), ("check", "cc"), ("symbol", "p::N.C.D()"), ("before", "aaa"), ("secondRound", false)),
            Event("fixed", "/w1", ("id", "1"), ("check", "cc"), ("symbol", "p::N.A.B(int)"), ("before", "bbb"), ("after", "ccc"), ("secondRound", true)),
            Event("refused-reason", "/w2", ("id", "2"), ("check", "cc"), ("symbol", "p::N.E"), ("finding", "weak"), ("before", "zzz")),
        };
        Write(events);

        var report = Metrics.Measure(brain.Home, null, null);

        var fixes = report.Sections.Single(section => section.Title == "Navarone fixes");
        Assert.Equal("Without Navarone, 2 methods would have shipped as they were.", fixes.Story);
        Assert.Equal(["A.B", "C.D"], fixes.Cases!.Select(one => one.Subject));
        Assert.Equal([new EvidenceProblem("/w1", "1"), new EvidenceProblem("/w2", "1")], fixes.Cases!.Select(one => one.Problem));
        Assert.Empty(fixes.Lines);
        var weak = report.Sections.Single(section => section.Title == "Navarone weak answers");
        Assert.Equal("Without the validator reading Navarone's answers, 2 weak answers would have stood.", weak.Story);
        Assert.Equal(["A.B", "E"], weak.Cases!.Select(one => one.Subject));
        Assert.Equal("cc", weak.Cases![1].Detail);
        var cost = report.Sections.Single(section => section.Title == "Cost");
        Assert.Contains("Navarone's share of that is 120 tokens", cost.Story, StringComparison.Ordinal);
        Assert.Contains("a floor", cost.Story, StringComparison.Ordinal);
    }

    [Fact]
    public void ALogWithNoEventsLeavesTheSectionsOut()
    {
        Write([]);

        var report = Metrics.Measure(brain.Home, null, null);

        Assert.DoesNotContain(report.Sections, section => section.Title.StartsWith("Navarone", StringComparison.Ordinal));
    }

    [Fact]
    public void APeriodWithNoCaseStillShowsZeroLines()
    {
        Write(
        [
            Event("resolved", "/w1", ("id", "2"), ("reason", "it is fine")),
            Event("refused-reason", "/w1", ("id", "2"), ("check", "cc"), ("symbol", "E.F()"), ("finding", "weak"), ("before", "aaa"), ("day", "2026-11-06")),
        ]);

        var late = Metrics.Measure(brain.Home, new DateOnly(2026, 11, 6), null);
        var early = Metrics.Measure(brain.Home, null, new DateOnly(2026, 11, 5));

        Assert.Single(late.Sections.Single(section => section.Title == "Navarone weak answers").Cases!);
        Assert.Contains("0 weak answers", early.Sections.Single(section => section.Title == "Navarone weak answers").Story, StringComparison.Ordinal);
    }

    public void Dispose() => brain.Dispose();

    private static string Event(string type, string worktree, params (string Name, object Value)[] fields)
    {
        var one = new Dictionary<string, object>
        {
            ["type"] = type,
            ["at"] = "2026-11-05T10:00:00Z",
            ["repo"] = "/r",
            ["worktree"] = worktree,
        };
        foreach (var (name, value) in fields)
        {
            one[name] = value;
        }

        if (one.Remove("day", out var day))
        {
            one["at"] = day + "T10:00:00Z";
        }

        return JsonSerializer.Serialize(one);
    }

    private void Write(string[] events)
    {
        var json = Path.Combine(brain.Root, "events.json");
        File.WriteAllText(json, """{"source":"navarone","events":[""" + string.Join(',', events) + "]}");
        File.WriteAllText(Path.Combine(brain.Home, "sources.tsv"), $"navarone\ttrue\ttrue\tcat '{json}'\n");
    }
}
