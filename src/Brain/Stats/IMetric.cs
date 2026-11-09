using Brain.Usage;

namespace Brain.Stats;

/// <summary>One metric of <c>brain stats</c>: it reads the events of the usage log in the range asked and returns its section.</summary>
internal interface IMetric
{
    MetricSection Measure(IReadOnlyList<UsageEvent> events);
}

/// <summary>
/// A metric's section: its title and its lines, as plain text fit for a slide. A section may also tell one line of the
/// evidence brain's window shows: <see cref="Story"/>, a fixed sentence filled from the log, and <see cref="Cases"/>,
/// every caught event the sentence counted, which are its proof.
/// </summary>
internal sealed record MetricSection(string Title, IReadOnlyList<string> Lines)
{
    /// <summary>The section's line of the evidence; null for a section that tells none.</summary>
    public string? Story { get; init; }

    /// <summary>The events <see cref="Story"/> counted, in the order they happened; null for a story that counts none, such as the cost, an estimate.</summary>
    public IReadOnlyList<EvidenceCase>? Cases { get; init; }

    /// <summary>A sentence under the line that states what its count does not cover; null for a line that needs none.</summary>
    public string? Note { get; init; }
}

/// <summary>One caught event a story counted: what it was about, such as a node or a builder, and what happened.</summary>
internal sealed record EvidenceCase(string Subject, string Detail)
{
    /// <summary>What the validator wrote in each round of findings, in order, null for a round recorded without text; null for a case that has no rounds.</summary>
    public IReadOnlyList<string?>? Rounds { get; init; }

    /// <summary>The Navarone problem the case is, whose story the window tells; null for a case that is none.</summary>
    public EvidenceProblem? Problem { get; init; }
}

/// <summary>A Navarone problem by its key: the worktree it was raised in and its id, which Navarone grants per worktree.</summary>
internal sealed record EvidenceProblem(string Worktree, string Id);
