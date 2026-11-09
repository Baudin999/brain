using Brain.Stats;
using Brain.Usage;

namespace Brain.Tests;

/// <summary>
/// The Judge metric reads the facts the gate logged: a duplicate its own gate pass also logged as a contradiction of the
/// same node counts as the contradiction only.
/// </summary>
public sealed class JudgeMetricTests : IDisposable
{
    private const string Duplicate = """{"at":"2026-11-04T14:17:09.608+01:00","event":"duplicate","session":"b181b209-fd32-456d-9a77-ca51e2493d33","title":"A project is a folder; a subject is something the nodes have in their keywords","ref":"global:project-and-subject","match":"global:subject-vocabulary"}""";

    private const string Contradiction = """{"at":"2026-11-04T14:17:09.621+01:00","event":"contradiction","session":"b181b209-fd32-456d-9a77-ca51e2493d33","title":"A project is a folder; a subject is something the nodes have in their keywords","reason":"The entry says subjects are free keywords with no vocabulary or list to confirm; the nearest node says each scope has a small vocabulary the user confirms and the agent tags from.","ref":"global:project-and-subject","match":"global:subject-vocabulary"}""";

    private const string Call = """{"at":"2026-11-04T14:17:10.000+01:00","event":"call","session":"b181b209-fd32-456d-9a77-ca51e2493d33","caller":"ai","command":"add","args":[],"chars":0,"ms":5,"exit":1}""";

    private const string OtherSession = """{"at":"2026-11-04T14:17:09.615+01:00","event":"call","session":"other","caller":"ai","command":"node","args":[],"chars":0,"ms":5,"exit":0}""";

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ADuplicateItsGatePassContradictedCountsAsTheContradictionOnly()
    {
        var section = Measure(Duplicate, OtherSession, Contradiction, Call);

        Assert.Equal(["  1 writes refused against the nearest entries: 0 duplicates, 1 contradictions, 0 without edges"], section.Lines);
        var only = Assert.Single(section.Cases!);
        Assert.StartsWith("global:project-and-subject contradicts global:subject-vocabulary: ", only.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateAndAContradictionInTwoGatePassesBothCount()
    {
        var section = Measure(Duplicate, Call, Contradiction);

        Assert.Equal(["  2 writes refused against the nearest entries: 1 duplicates, 1 contradictions, 0 without edges"], section.Lines);
    }

    private MetricSection Measure(params string[] lines)
    {
        Directory.CreateDirectory(Path.Combine(brain.Home, "usage"));
        File.WriteAllLines(Path.Combine(brain.Home, "usage", "2026-11.jsonl"), lines);
        return new JudgeMetric().Measure(UsageLog.Read(brain.Home).Events);
    }
}
