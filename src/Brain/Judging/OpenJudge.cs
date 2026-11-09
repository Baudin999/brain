namespace Brain.Judging;

/// <summary>Passes everything. The CLI uses it only when BRAIN_JUDGE is off.</summary>
internal sealed class OpenJudge : IJudge
{
    public Verdict Judge(IReadOnlyList<JudgedEntry> entries) => Verdict.Accept;
}
