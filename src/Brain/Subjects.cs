using System.Text.RegularExpressions;

namespace Brain;

/// <summary>
/// The subjects a node is about: a list of lowercase-hyphenated words in the frontmatter, <c>subjects: [a, b]</c>.
/// An empty list is written <c>[]</c>. A subject is a free keyword a search finds the node by; no list holds them.
/// </summary>
internal static partial class Subjects
{
    public const string Empty = "[]";
    private const string Open = "[";
    private const string Close = "]";
    private const string Join = ", ";
    private const char Comma = ',';

    /// <summary>Why the field's value is not a subjects list, or null when it is one.</summary>
    public static string? Problem(string? value) =>
        value is null
            ? $"{FieldNames.Subjects} is missing"
            : List().IsMatch(value) ? null : $"{FieldNames.Subjects} {value} is not a list of lowercase-hyphenated words like [ui, judge]";

    /// <summary>The node's subjects in the order its file lists them; none when the field is missing or not a list.</summary>
    public static IReadOnlyList<string> Of(Node node) => Parse(node.Field(FieldNames.Subjects));

    /// <summary>The words of a list written <c>[a, b]</c>; none when the text is not such a list.</summary>
    public static IReadOnlyList<string> Parse(string? value) =>
        value is not null && List().IsMatch(value) ? Split(value.Trim()[Open.Length..^Close.Length]) : [];

    /// <summary>The words of a comma-separated list as the command line types it, <c>a,b</c>; an empty text clears.</summary>
    public static IReadOnlyList<string> FromArgument(string text)
    {
        var words = Split(text);
        BrainException.ThrowIf(words.FirstOrDefault(word => !Slugs.IsValid(word)) is { } bad ? $"subject {bad} is not lowercase-hyphenated" : null);
        return words;
    }

    /// <summary>Each subject the nodes carry with how many carry it, the most used first, then in ordinal order.</summary>
    public static IReadOnlyList<(string Subject, int Nodes)> InUse(IEnumerable<Node> nodes) =>
        [
            .. nodes
                .SelectMany(Of)
                .GroupBy(subject => subject, StringComparer.Ordinal)
                .Select(group => (Subject: group.Key, Nodes: group.Count()))
                .OrderByDescending(use => use.Nodes)
                .ThenBy(use => use.Subject, StringComparer.Ordinal),
        ];

    public static string Format(IEnumerable<string> subjects) => Open + string.Join(Join, subjects) + Close;

    /// <summary>The node with its subjects set to <paramref name="subjects"/>, in place.</summary>
    public static Node With(Node node, IEnumerable<string> subjects) => node.WithField(new NodeField(FieldNames.Subjects, Format(subjects)));

    private static List<string> Split(string text) =>
        [.. text.Split(Comma, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"^\[\s*(?:[a-z0-9]+(?:-[a-z0-9]+)*(?:\s*,\s*[a-z0-9]+(?:-[a-z0-9]+)*)*)?\s*\]$")]
    private static partial Regex List();
}
