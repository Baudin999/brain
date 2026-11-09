using System.Globalization;
using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Knowledge reuse: every node a node or a walk opened in an AI session, its age when it was read and how many sessions
/// it served. The headline is knowledge written in one session and read in another: a read of a node whose session is
/// not the reader's, or that keeps no session. Reads from the user's own terminal are counted on a line of their own.
/// Each such read is a case of the evidence: a session that would have started from zero without the graph.
/// </summary>
internal sealed class ReuseMetric : IMetric
{
    private const int Listed = 10;

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var opened = events.Where(usage => usage.Event == UsageEvent.Call && usage.Opened is not null).ToList();
        var reads = opened
            .Where(call => call.Session is not null)
            .SelectMany(call => call.Opened!.Select(node => new Read(node, call.Session!, AgeOf(node, call.At))))
            .ToList();
        var terminal = opened.Where(call => call.Session is null).Sum(call => call.Opened!.Count);
        var lines = new List<string>();
        var sessions = reads.Select(read => read.Session).Distinct(StringComparer.Ordinal).Count();
        var nodes = reads.GroupBy(read => read.Node.Ref, StringComparer.Ordinal).ToList();
        lines.Add($"  {Figures.Number(reads.Count)} reads of {Figures.Number(nodes.Count)} nodes in {Figures.Number(sessions)} AI sessions; {Figures.Number(terminal)} more from your terminal");
        var elsewhere = reads.Where(read => read.Elsewhere).ToList();
        var servedOthers = elsewhere.Select(read => read.Node.Ref).Distinct(StringComparer.Ordinal).Count();
        lines.Add($"  {Figures.Number(elsewhere.Count)} of {Figures.Number(reads.Count)} reads ({Figures.Percent(elsewhere.Count, reads.Count)}) opened a node written in another session; {Figures.Number(servedOthers)} nodes served a session that did not write them");
        var ages = reads.Select(read => read.Age).OfType<int>().Order().ToList();
        if (ages.Count > 0)
        {
            lines.Add($"  age when read: median {Figures.Number(ages[ages.Count / 2])} days, oldest {Figures.Number(ages[^1])} days");
        }

        if (nodes.Count > 0)
        {
            lines.Add($"  {"node",-40}{"reads",7}{"sessions",10}  age when read, days");
        }

        foreach (var node in nodes.Select(Summary).OrderByDescending(node => node.Sessions).ThenByDescending(node => node.Reads).ThenBy(node => node.Ref, StringComparer.Ordinal).Take(Listed))
        {
            lines.Add($"  {node.Ref,-40}{Figures.Number(node.Reads),7}{Figures.Number(node.Sessions),10}  {node.Ages}");
        }

        var oldest = elsewhere.Select(read => read.Age).OfType<int>().DefaultIfEmpty(0).Max();
        return new MetricSection("Knowledge reuse", lines)
        {
            Story = $"Without brain's graph, your agents would have started from zero {Figures.Count(elsewhere.Count, "time", "times")}, not knowing what you decided up to {Figures.Count(oldest, "day", "days")} earlier.",
            Cases = [.. elsewhere.Select(read => new EvidenceCase(read.Node.Ref, $"{(read.Age is { } age ? Figures.Count(age, "day", "days") + " old" : "age unknown")}, read in session {read.Session}"))],
        };
    }

    private static NodeSummary Summary(IGrouping<string, Read> reads)
    {
        var ages = reads.Select(read => read.Age).OfType<int>().ToList();
        var range = ages.Count == 0 ? "unknown" : ages.Min() == ages.Max() ? Figures.Number(ages.Min()) : $"{Figures.Number(ages.Min())}-{Figures.Number(ages.Max())}";
        return new NodeSummary(reads.Key, reads.Count(), reads.Select(read => read.Session).Distinct(StringComparer.Ordinal).Count(), range);
    }

    /// <summary>Days from the node's birth to the day it was read; null when either date does not read.</summary>
    private static int? AgeOf(OpenedNode node, string at) =>
        DateOnly.TryParseExact(node.Born, FieldNames.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var born)
        && Figures.DayOf(at) is { } read
            ? read.DayNumber - born.DayNumber
            : null;

    /// <summary>One read of a node in an AI session: the node, the session that read it and the node's age then.</summary>
    private sealed record Read(OpenedNode Node, string Session, int? Age)
    {
        public bool Elsewhere => Node.Session is null || !string.Equals(Node.Session, Session, StringComparison.Ordinal);
    }

    private sealed record NodeSummary(string Ref, int Reads, int Sessions, string Ages);
}
