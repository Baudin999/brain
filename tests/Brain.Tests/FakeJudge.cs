using Brain.Judging;

namespace Brain.Tests;

/// <summary>
/// Stands in for the model: records every call, refuses with <see cref="Refusal"/> when it is set, and otherwise answers
/// <see cref="Answer"/>, which accepts until a test sets one.
/// </summary>
internal sealed class FakeJudge : IJudge
{
    public List<IReadOnlyList<JudgedEntry>> Calls { get; } = [];

    public string? Refusal { get; set; }

    public Verdict Answer { get; set; } = Verdict.Accept;

    /// <summary>Runs while the judge reads, as another session writing at that moment would.</summary>
    public Action? During { get; set; }

    public Verdict Judge(IReadOnlyList<JudgedEntry> entries)
    {
        Calls.Add(entries);
        During?.Invoke();
        return Refusal is null ? Answer : new Verdict(Ok: false, Refusal);
    }
}
