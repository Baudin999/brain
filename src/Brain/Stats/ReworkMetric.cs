using Brain.Usage;

namespace Brain.Stats;

/// <summary>Rework: every message sent to an agent after it was spawned as a builder, by the type of agent that sent it.</summary>
internal sealed class ReworkMetric : IMetric
{
    private const string NoType = "an agent of no type";

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var spawned = Agents.Spawned(events, Agents.Builder);
        var messages = events.Select(usage => (Message: usage, Builder: Agents.Reached(spawned, usage))).Where(pair => pair.Builder is not null).ToList();
        var bySender = messages
            .GroupBy(pair => pair.Message.AgentType ?? NoType, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{Figures.Number(group.Count())} from {group.Key}");
        var senders = messages.Count == 0 ? string.Empty : ": " + string.Join(", ", bySender);
        var builders = messages.Select(pair => pair.Builder).Distinct(StringComparer.Ordinal).Count();
        return new MetricSection("Rework",
        [
            $"  {Figures.Number(messages.Count)} messages sent to {Figures.Number(builders)} builders after they started{senders}",
        ]);
    }
}
