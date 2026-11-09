using System.Globalization;
using System.Text.Json.Serialization;
using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Every metric, in the one list <c>brain stats</c> and brain's window both read, so they report the same numbers.
/// <see cref="All"/> is the one place a metric is added or taken out. The sections that tell the evidence come first,
/// in the order the window tells them, and the cost follows them. When the data repo's <c>sources.tsv</c> has a navarone
/// line that reads, <see cref="Measure"/> also puts Navarone's two lines of the evidence before the cost and adds
/// Navarone's share to the cost.
/// </summary>
internal static class Metrics
{
    /// <summary>The metrics, in the order they report.</summary>
    public static readonly IReadOnlyList<IMetric> All =
    [
        new SavesMetric(),
        new ValidatorMetric(),
        new ReuseMetric(),
        new ReadinessMetric(),
        new GuardsMetric(),
        new JudgeMetric(),
        new CostMetric(),
        new HitRateMetric(),
        new ReworkMetric(),
    ];

    /// <summary>Reads the usage log in <paramref name="home"/>, keeps the events from <paramref name="since"/> to <paramref name="until"/>, each open when null, and runs every metric over them.</summary>
    public static StatsReport Measure(string home, DateOnly? since, DateOnly? until)
    {
        var (all, unreadable) = UsageLog.Read(home);
        var events = all.Where(usage => Figures.DayOf(usage.At) is { } day && (since is null || day >= since) && (until is null || day <= until)).ToList();
        var range = since is null && until is null ? "all time" : $"from {Show(since, "the start")} to {Show(until, "today")}";
        var heading = $"brain stats, {range}: {Figures.Number(events.Count)} events. Tokens are estimates: output characters divided by {Figures.CharsPerToken}.";
        var sections = All.Select(metric => metric.Measure(events)).ToList();
        if (NavaroneEvidence.Read(home, since, until) is { } navarone)
        {
            var cost = sections.FindIndex(section => section.Title == CostMetric.Title);
            sections[cost] = CostMetric.Section(events, navarone.Share);
            sections.InsertRange(cost, navarone.Sections);
        }

        return new StatsReport(heading, unreadable, sections);
    }

    private static string Show(DateOnly? day, string open) => day?.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture) ?? open;
}

/// <summary>What the metrics report over one range: the heading, the lines of the log that could not be read and every section.</summary>
internal sealed record StatsReport(string Heading, int Unreadable, IReadOnlyList<MetricSection> Sections);

/// <summary>The JSON of a report, which <c>brain stats --json</c> prints and brain's window reads, generated at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatsReport))]
internal sealed partial class StatsJson : JsonSerializerContext;
