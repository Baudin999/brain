using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Validator rounds: every message a validator sent to an agent spawned as a builder in its session is one round of
/// findings sent back, counted per builder and in total. A builder that got at least one round is a build that would
/// have shipped with problems without the validator, and a case of the evidence.
/// </summary>
internal sealed class ValidatorMetric : IMetric
{
    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var spawned = Agents.Spawned(events, Agents.Builder);
        var builders = spawned.Values.Distinct(StringComparer.Ordinal).Count();
        var rounds = events
            .Where(usage => usage.AgentType == Agents.Validator)
            .Select(usage => (usage.Session, Builder: Agents.Reached(spawned, usage), usage.Text))
            .Where(round => round.Builder is not null)
            .GroupBy(round => (round.Session, round.Builder))
            .ToList();
        var total = rounds.Sum(builder => builder.Count());
        var lines = new List<string>
        {
            $"  {Figures.Number(total)} rounds of findings sent to {Figures.Number(rounds.Count)} builders; {Figures.Number(builders - rounds.Count)} of {Figures.Number(builders)} builders got none",
        };
        if (rounds.Count > 0)
        {
            lines.Add($"  {"builder",-20}{"session",-38}{"rounds",6}");
        }

        foreach (var builder in rounds.OrderByDescending(builder => builder.Count()).ThenBy(builder => builder.Key.Builder, StringComparer.Ordinal))
        {
            lines.Add($"  {builder.Key.Builder,-20}{builder.Key.Session ?? "-",-38}{Figures.Number(builder.Count()),6}");
        }

        return new MetricSection("Validator", lines)
        {
            Story = $"Without brain's validator, {Figures.Number(rounds.Count)} of {Figures.Count(builders, "build", "builds")} would have shipped with problems.",
            Cases = [.. rounds.Select(builder => new EvidenceCase(builder.Key.Builder!, $"{Figures.Count(builder.Count(), "round", "rounds")} of findings, session {builder.Key.Session ?? "unknown"}") { Rounds = [.. builder.Select(round => round.Text)] })],
        };
    }
}
