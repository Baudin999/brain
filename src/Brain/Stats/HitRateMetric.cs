using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Hit rate: how often a lookup in an AI session is followed, later in the same session, by a node or a walk that opens
/// one of the refs it returned.
/// </summary>
internal sealed class HitRateMetric : IMetric
{
    private const string Lookup = "lookup";

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var lookups = 0;
        var returning = 0;
        var hits = 0;
        foreach (var session in events.Where(usage => usage.Event == UsageEvent.Call && usage.Session is not null).GroupBy(usage => usage.Session!, StringComparer.Ordinal))
        {
            var calls = session.ToList();
            for (var at = 0; at < calls.Count; at++)
            {
                if (calls[at].Command != Lookup)
                {
                    continue;
                }

                lookups++;
                var returned = calls[at].Returned ?? [];
                if (returned.Count == 0)
                {
                    continue;
                }

                returning++;
                if (calls.Skip(at + 1).Any(later => (later.Opened ?? []).Any(node => returned.Contains(node.Ref, StringComparer.Ordinal))))
                {
                    hits++;
                }
            }
        }

        return new MetricSection("Hit rate",
        [
            $"  {Figures.Number(lookups)} lookups in AI sessions, {Figures.Number(returning)} returned refs",
            $"  {Figures.Number(hits)} of {Figures.Number(returning)} ({Figures.Percent(hits, returning)}) were followed in the same session by opening a ref they returned",
        ]);
    }
}
