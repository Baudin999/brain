namespace Brain;

/// <summary>
/// The two kinds a node may have. Information is what is true now, and carries a why; its scope says which kind of
/// information it is. An idea is a record of thinking together: what we discussed, what we rejected, and the reasons. It
/// needs no why.
/// </summary>
internal static class Kinds
{
    public const string Information = "information";
    public const string Idea = "idea";

    public static readonly IReadOnlyList<string> All = [Information, Idea];

    public static bool IsKnown(string? kind) => All.Contains(kind, StringComparer.Ordinal);

    /// <summary>True for information, which says why it is true; an idea needs no why.</summary>
    public static bool NeedsWhy(string? kind) => string.Equals(kind, Information, StringComparison.Ordinal);
}
