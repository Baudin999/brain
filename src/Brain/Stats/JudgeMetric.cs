using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Judge: every node write the judge's gate refused against the nearest entries. A duplicate said the same as a node
/// the graph holds, a contradiction said the opposite of one, and a missing-edges write carried no edge while the judge
/// named the nodes it belongs with. Each is a case of the evidence, with the write's title and the node it matched or
/// the edges named. A duplicate and a contradiction exclude each other: a duplicate whose session, write and node
/// match a contradiction the same gate pass logged counts as the contradiction only. A gate pass ends with the call
/// event of its session, which the CLI logs after the command has run.
/// </summary>
internal sealed class JudgeMetric : IMetric
{
    private const string Join = ", ";

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var caught = Caught(events);
        var duplicates = caught.Count(usage => usage.Event == UsageEvent.Duplicate);
        var contradictions = caught.Count(usage => usage.Event == UsageEvent.Contradiction);
        var bare = caught.Count(usage => usage.Event == UsageEvent.MissingEdges);
        return new MetricSection("Judge",
        [
            $"  {Figures.Number(caught.Count)} writes refused against the nearest entries: {Figures.Number(duplicates)} duplicates, {Figures.Number(contradictions)} contradictions, {Figures.Number(bare)} without edges",
        ])
        {
            Story = $"Without brain's judge, {Figures.Count(duplicates, "duplicate", "duplicates")} and {Figures.Count(contradictions, "contradiction", "contradictions")} "
                + $"would have entered the graph and {Figures.Count(bare, "node", "nodes")} would have landed with no edges.",
            Cases = [.. caught.Select(Case)],
        };
    }

    /// <summary>Every write the gate refused, in the order logged, without a duplicate its own gate pass contradicted.</summary>
    private static List<UsageEvent> Caught(IReadOnlyList<UsageEvent> events)
    {
        var caught = new List<UsageEvent>();
        for (var at = 0; at < events.Count; at++)
        {
            var usage = events[at];
            if (usage.Event is not (UsageEvent.Duplicate or UsageEvent.Contradiction or UsageEvent.MissingEdges))
            {
                continue;
            }

            if (usage.Event == UsageEvent.Duplicate && Contradicted(events, at))
            {
                continue;
            }

            caught.Add(usage);
        }

        return caught;
    }

    /// <summary>
    /// True when a contradiction of the same session, write and node follows the duplicate at
    /// <paramref name="duplicateAt"/> before its session's next call event, which closes the gate pass that logged the
    /// duplicate.
    /// </summary>
    private static bool Contradicted(IReadOnlyList<UsageEvent> events, int duplicateAt)
    {
        var duplicate = events[duplicateAt];
        for (var at = duplicateAt + 1; at < events.Count; at++)
        {
            var usage = events[at];
            if (!string.Equals(usage.Session, duplicate.Session, StringComparison.Ordinal))
            {
                continue;
            }

            if (usage.Event == UsageEvent.Call)
            {
                return false;
            }

            if (usage.Event == UsageEvent.Contradiction && Same(usage, duplicate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Same(UsageEvent contradiction, UsageEvent duplicate) =>
        string.Equals(contradiction.Ref, duplicate.Ref, StringComparison.Ordinal)
        && string.Equals(contradiction.Match, duplicate.Match, StringComparison.Ordinal);

    private static EvidenceCase Case(UsageEvent usage)
    {
        var title = usage.Title ?? usage.Ref ?? "a write";
        var write = usage.Ref ?? "a node";
        return usage.Event switch
        {
            UsageEvent.Duplicate => new EvidenceCase(title, $"{write} says the same as {usage.Match}"),
            UsageEvent.Contradiction => new EvidenceCase(title, $"{write} contradicts {usage.Match}: {usage.Reason}"),
            _ => new EvidenceCase(title, $"{write} carried no edge; the judge named {string.Join(Join, usage.Edges ?? [])}"),
        };
    }
}
