using Brain.Staging;
using Brain.Usage;

namespace Brain.Stats;

/// <summary>
/// Saves: the user's answers to pending writes. A no with words is a correction: the agent thought the write was right
/// and the user said what to change. Each correction is a case of the evidence, with the write's title and the user's
/// words.
/// </summary>
internal sealed class SavesMetric : IMetric
{
    public MetricSection Measure(IReadOnlyList<UsageEvent> events)
    {
        var answers = events.Where(usage => usage.Event == UsageEvent.Answer).ToList();
        int Count(string verdict) => answers.Count(answer => answer.Verdict == verdict);
        var corrections = answers.Where(answer => answer.Verdict == UsageEvent.Change).ToList();
        var lines = new List<string>
        {
            $"  {Figures.Number(answers.Count)} answers: {Figures.Number(Count(PendingAnswer.Yes))} yes, {Figures.Number(Count(PendingAnswer.No))} no, {Figures.Number(corrections.Count)} no with words",
        };
        var unlanded = answers.Count(answer => answer.Landed == false);
        if (unlanded > 0)
        {
            lines.Add($"  {Figures.Number(unlanded)} yes did not land: a node changed after the write was staged");
        }

        return new MetricSection("Saves", lines)
        {
            Story = $"Without brain, your agent would have gone off the rails {Figures.Count(corrections.Count, "time", "times")}. You corrected it.",
            Cases = [.. corrections.Select(answer => new EvidenceCase(answer.Title ?? $"pending write {answer.Id}", answer.Words ?? "your words were not logged"))],
        };
    }
}
