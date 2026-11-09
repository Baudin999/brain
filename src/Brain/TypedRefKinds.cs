using System.Text.RegularExpressions;

namespace Brain;

/// <summary>The typed ref kinds and the shape each one's value must have.</summary>
internal static partial class TypedRefKinds
{
    public const string Commit = "commit";
    public const string Session = "session";
    public const string File = "file";
    public const string Url = "url";

    private static readonly Dictionary<string, Regex> Shapes = new(StringComparer.Ordinal)
    {
        [Commit] = CommitShape(),
        [Session] = SessionShape(),
        [File] = FileShape(),
        [Url] = UrlShape(),
    };

    public static bool IsKind(string kind) => Shapes.ContainsKey(kind);

    public static string? Problem(string kind, string value) =>
        Shapes[kind].IsMatch(value) ? null : $"malformed {kind} ref {value}";

    [GeneratedRegex("^[0-9a-f]{4,40}$")]
    private static partial Regex CommitShape();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex SessionShape();

    [GeneratedRegex(@"^\S+$")]
    private static partial Regex FileShape();

    [GeneratedRegex(@"^https?://\S+$")]
    private static partial Regex UrlShape();
}
