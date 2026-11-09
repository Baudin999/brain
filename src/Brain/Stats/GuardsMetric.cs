using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Guards: every tool call a hook refused. Each refusal is a thing an agent must not do and a case of the evidence, with
/// the hook and the first line it said. The story names the kinds by the hook that refused them.
/// </summary>
internal sealed class GuardsMetric : IMetric
{
    /// <summary>What each of the harness's hooks refuses, as the story names it; a hook not here is named by itself.</summary>
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["developer-gate"] = "building before the go",
        ["graph-door"] = "hand-editing the graph",
        ["cpu-guard"] = "loading the protected CPUs",
        ["block-bad-worktree"] = "a worktree outside its place",
        ["brain-ready"] = "a readiness check reading past the graph",
    };

    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var refusals = events.Where(usage => usage.Event == UsageEvent.Refusal).ToList();
        var kinds = refusals
            .GroupBy(KindOf, StringComparer.Ordinal)
            .OrderByDescending(kind => kind.Count())
            .ThenBy(kind => kind.Key, StringComparer.Ordinal)
            .Select(kind => $"{Figures.Number(kind.Count())} {kind.Key}")
            .ToList();
        var named = kinds.Count == 0 ? string.Empty : ": " + string.Join(", ", kinds);
        return new MetricSection("Guards", [])
        {
            Story = $"Without brain's guards, agents would have done {Figures.Count(refusals.Count, "thing", "things")} they must not{named}.",
            Cases = [.. refusals.Select(refusal => new EvidenceCase(refusal.Hook ?? "a hook", $"refused {refusal.Tool ?? "a tool call"} for {refusal.AgentType ?? "the session"}: {refusal.Reason ?? "it said nothing"}"))],
        };
    }

    private static string KindOf(UsageEvent refusal) =>
        refusal.Hook is { } hook ? Kinds.GetValueOrDefault(hook, hook) : "by an unnamed hook";
}
