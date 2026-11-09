namespace Brain.Judging;

/// <summary>Reads the entries the CLI is about to write and says whether they keep the writing rules.</summary>
internal interface IJudge
{
    Verdict Judge(IReadOnlyList<JudgedEntry> entries);
}
