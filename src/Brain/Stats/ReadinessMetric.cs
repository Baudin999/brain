using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Readiness: the verdict every readiness check stopped on. A NOT READY is a brief that would have gone to a builder
/// with questions only the user could answer, and a case of the evidence, named by the first line of the brief its
/// spawn carried.
/// </summary>
internal sealed class ReadinessMetric : IMetric
{
    private const string Ready = "READY";
    private const string NotReady = "NOT READY";

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var briefs = new Dictionary<(string? Session, string Agent), string>();
        foreach (var spawn in events.Where(usage => usage.Event == UsageEvent.Spawn && usage.SubagentType == Agents.ReadinessCheck && usage.Spawned is not null && usage.Brief is not null))
        {
            briefs[(spawn.Session, spawn.Spawned!)] = spawn.Brief!;
        }

        var verdicts = events.Where(usage => usage.Event == UsageEvent.Readiness).ToList();
        var notReady = verdicts.Where(verdict => verdict.Verdict == NotReady).ToList();
        var ready = verdicts.Count(verdict => verdict.Verdict == Ready);
        return new MetricSection("Readiness",
        [
            $"  {Figures.Number(verdicts.Count)} readiness checks: {Figures.Number(ready)} READY, {Figures.Number(notReady.Count)} NOT READY, {Figures.Number(verdicts.Count - ready - notReady.Count)} other",
        ])
        {
            Story = $"Without the readiness check, {Figures.Count(notReady.Count, "brief", "briefs")} would have gone to a builder with questions only you could answer.",
            Cases = [.. notReady.Select(verdict => new EvidenceCase(Brief(briefs, verdict), $"NOT READY, session {verdict.Session ?? "unknown"}"))],
        };
    }

    private static string Brief(Dictionary<(string? Session, string Agent), string> briefs, UsageEvent verdict) =>
        verdict.AgentId is { } agent && briefs.TryGetValue((verdict.Session, agent), out var brief) ? brief : "the brief was not logged";
}
