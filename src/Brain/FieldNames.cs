namespace Brain;

/// <summary>The frontmatter fields a node carries, in the order a new node writes them, and who may name a node.</summary>
internal static class FieldNames
{
    public const string Kind = "kind";
    public const string Subjects = "subjects";
    public const string NamedBy = "named-by";
    public const string Born = "born";
    public const string Session = "session";

    public const string User = "user";
    public const string Claude = "claude";
    public const string Agent = "agent";

    public const string DateFormat = "yyyy-MM-dd";

    public static readonly IReadOnlyList<string> Namers = [User, Claude, Agent];
}
