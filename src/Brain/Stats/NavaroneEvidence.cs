namespace Brain.Stats;

/// <summary>
/// What Navarone's event log shows it caught, read through <see cref="NavaroneLog"/>. Two lines of the evidence: the
/// problems a builder fixed because of it, and the problems whose fix or reason the validator refused. A problem is a
/// worktree and an id, since ids are granted per worktree, and it counts once however many rounds it took. Each case
/// names its problem, whose story the window's Navarone tab tells. The reading is null without a log or with a log
/// that holds no event, so brain works without Navarone.
/// </summary>
internal sealed class NavaroneEvidence
{
    private const string Limits = "Counted in agent work, not on master: Navarone logs in a worktree before any commit, and work thrown away counts.";

    private readonly IReadOnlyList<NavaroneEvent> period;

    private NavaroneEvidence(IReadOnlyList<NavaroneEvent> period)
    {
        this.period = period;
    }

    // navarone: 0000149
    public IReadOnlyList<MetricSection> Sections => [Fixes(), Refusals()];

    /// <summary>The tokens the period's runs and other commands printed, with how many of each.</summary>
    public NavaroneShare Share
    {
        get
        {
            var runs = period.Where(one => one.Type == "run").ToList();
            var costs = period.Where(one => one.Type == "cost").ToList();
            return new NavaroneShare(runs.Sum(one => one.Tokens) + costs.Sum(one => one.Tokens), runs.Count, costs.Count);
        }
    }

    // navarone: 0000150
    public static NavaroneEvidence? Read(string home, DateOnly? since, DateOnly? until)
    {
        var log = NavaroneLog.Read(home);
        if (log is null || log.Count == 0)
        {
            return null;
        }

        return new NavaroneEvidence([.. log.Where(one => one.Within(since, until))]);
    }

    private MetricSection Fixes()
    {
        var cases = FirstOfEach("fixed").Select(Case).ToList();
        var story = $"Without Navarone, {Figures.Count(cases.Count, "method", "methods")} would have shipped as {(cases.Count == 1 ? "it was" : "they were")}.";
        return new MetricSection("Navarone fixes", []) { Story = story, Cases = cases, Note = Limits };
    }

    private MetricSection Refusals()
    {
        var cases = FirstOfEach("refused-fix", "refused-reason").Select(Case).ToList();
        var story = $"Without the validator reading Navarone's answers, {Figures.Count(cases.Count, "weak answer", "weak answers")} would have stood.";
        return new MetricSection("Navarone weak answers", []) { Story = story, Cases = cases, Note = Limits };
    }

    /// <summary>A problem as a case: its symbol in the code's own words, its check and its key.</summary>
    private static EvidenceCase Case(NavaroneEvent first)
    {
        var subject = NavaroneSymbol.Name(first.Symbol ?? string.Empty);
        return new EvidenceCase(subject, first.Check ?? string.Empty) { Problem = new EvidenceProblem(first.Worktree, first.Id!) };
    }

    /// <summary>The first event of the given types of each problem, in the order those happened in the period.</summary>
    private List<NavaroneEvent> FirstOfEach(params string[] types)
    {
        var seen = new HashSet<(string, string)>();
        var firsts = new List<NavaroneEvent>();
        foreach (var one in period)
        {
            if (!types.Contains(one.Type))
            {
                continue;
            }

            if (one.Id is null)
            {
                continue;
            }

            if (seen.Add((one.Worktree, one.Id)))
            {
                firsts.Add(one);
            }
        }

        return firsts;
    }
}

/// <summary>Navarone's cost in a period: the estimated tokens it printed, in how many runs and other commands.</summary>
internal sealed record NavaroneShare(long Tokens, int Runs, int Commands);
