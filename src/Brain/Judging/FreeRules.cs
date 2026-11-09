namespace Brain.Judging;

/// <summary>
/// The deterministic rules an entry meets before the judge: one of the two kinds, a why behind information, a new or
/// changed title that is not a commit subject, and at least one subject on a node write. They are instant and exact,
/// so the model only answers the question of meaning.
/// </summary>
internal static class FreeRules
{
    private const string Or = ", ";

    /// <summary>Why the entry breaks a free rule, or null when it keeps them all.</summary>
    public static string? Problem(JudgedEntry entry, IReadOnlyList<string> commits) =>
        KindProblem(entry) ?? WhyProblem(entry) ?? EchoProblem(entry, commits) ?? SubjectProblem(entry);

    private static string? KindProblem(JudgedEntry entry) =>
        Kinds.IsKnown(entry.Kind)
            ? null
            : $"kind {entry.Kind} is not one of {string.Join(Or, Kinds.All)}. Information is what is true now; an idea records what we thought through together";

    private static string? WhyProblem(JudgedEntry entry) =>
        Kinds.NeedsWhy(entry.Kind) && entry.Why.Length == 0
            ? $"{Kinds.Information} needs a why. Ask the user for the reason, or write it as an {Kinds.Idea}"
            : null;

    private static string? EchoProblem(JudgedEntry entry, IReadOnlyList<string> commits) =>
        entry.TitleIsNew && CommitEcho.Find(entry.Title, commits) is { } subject
            ? $"the title repeats the commit subject \"{subject}\". Git holds what happened; the graph holds the subject that lasts"
            : null;

    private static string? SubjectProblem(JudgedEntry entry) =>
        entry.NodeWrite && entry.Subjects.Count == 0
            ? $"a node needs at least one subject, a keyword a search finds it by. Run brain subjects {entry.Scope} to reuse a word already in use"
            : null;
}
