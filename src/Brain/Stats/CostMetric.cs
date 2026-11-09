using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// What brain costs: calls and estimated tokens per command and in total, calls by AI sessions and by the user's own
/// terminal, and the fixed cost of loading the brain skill, per load and in total. Its story is the cost under the
/// evidence: the tokens of every call and every load together, an estimate that says so.
/// </summary>
internal sealed class CostMetric : IMetric
{
    public const string Title = "Cost";

    public MetricSection Measure(IReadOnlyList<UsageEvent> events) => Section(events, null);

    /// <summary>The section; with Navarone's share, its tokens are in the total and the story says how much of it is Navarone's.</summary>
    public static MetricSection Section(IReadOnlyList<UsageEvent> events, NavaroneShare? navarone)
    {
        var calls = events.Where(usage => usage.Event == UsageEvent.Call).ToList();
        var lines = new List<string> { $"  {"command",-14}{"calls",8}{"tokens (est.)",16}" };
        foreach (var command in calls.GroupBy(call => call.Command ?? string.Empty).OrderByDescending(Chars).ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            lines.Add(Row(command.Key, command.Count(), Chars(command)));
        }

        lines.Add(Row("total", calls.Count, Chars(calls)));
        lines.Add($"  AI sessions: {By(calls, UsageEvent.Ai)}; your terminal: {By(calls, UsageEvent.User)}");
        var loads = events.Where(usage => usage.Event == UsageEvent.Skill && usage.SkillName == UsageEvent.BrainSkill).ToList();
        var sessions = loads.Select(load => load.Session).Distinct(StringComparer.Ordinal).Count();
        lines.Add(loads.Count == 0
            ? "  brain skill: no load recorded"
            : $"  brain skill: {Figures.Number(Figures.Tokens(loads[^1].Chars ?? 0))} tokens (est.) per load, {Figures.Number(loads.Count)} loads in {Figures.Number(sessions)} sessions, {Figures.Number(Figures.Tokens(Chars(loads)))} tokens (est.) in total");
        var tokens = Figures.Tokens(Chars(calls) + Chars(loads)) + (navarone?.Tokens ?? 0);
        var story = $"Over the same period brain cost an estimated {Figures.Count(tokens, "token", "tokens")}: {Figures.Count(calls.Count, "call", "calls")} of the CLI and {Figures.Count(loads.Count, "load", "loads")} of the brain skill.";
        if (navarone is not null)
        {
            story += $" Navarone's share of that is {Figures.Count(navarone.Tokens, "token", "tokens")}, the output of {Figures.Count(navarone.Runs, "run", "runs")} and {Figures.Count(navarone.Commands, "other command", "other commands")}. It is a floor: the tokens agents spend acting on what Navarone says are not counted.";
        }

        return new MetricSection(Title, lines) { Story = story };
    }

    private static long Chars(IEnumerable<UsageEvent> events) => events.Sum(usage => usage.Chars ?? 0);

    private static string Row(string command, int calls, long chars) =>
        $"  {command,-14}{Figures.Number(calls),8}{Figures.Number(Figures.Tokens(chars)),16}";

    private static string By(List<UsageEvent> calls, string caller)
    {
        var mine = calls.Where(call => call.Caller == caller).ToList();
        return $"{Figures.Number(mine.Count)} calls, {Figures.Number(Figures.Tokens(Chars(mine)))} tokens (est.)";
    }
}
