using System.Security.Cryptography;
using System.Text;

namespace Brain.Search;

/// <summary>What the search index holds for a node: the keys it files it under, its full-text columns and the passage it embeds.</summary>
internal static class NodeDocument
{
    private const char Separator = ':';
    private const char ProjectMark = '@';
    private const char Space = ' ';
    private const int HashChars = 16;

    /// <summary>
    /// The scope's key. A project scope is keyed by its repo alone, so two projects never overwrite each other's rows,
    /// and one project's graph has one row per node whether lookup reads it as project or nearest by its projects.tsv
    /// name.
    /// </summary>
    public static string ScopeKey(Scope scope) =>
        scope.Repo is null ? scope.Name : ScopeNames.Project + ProjectMark + Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.Repo));

    public static string Key(PlacedNode node) => ScopeKey(node.Scope) + Separator + node.Node.Slug;

    /// <summary>The full-text columns in <see cref="SearchIndex"/> order: slug words, title, subjects, why, body.</summary>
    public static string[] Columns(Node node) =>
    [
        node.Slug.Replace('-', Space),
        node.Title ?? string.Empty,
        string.Join(Space, Subjects.Of(node).Select(subject => subject.Replace('-', Space))),
        node.Why,
        node.Body,
    ];

    /// <summary>The text of a node as a passage, before its model's passage prompt: slug words, title, why and body. A document not yet named has no slug words.</summary>
    public static string Passage(Node node) =>
        (node.Slug.Length > 0 ? node.Slug.Replace('-', Space) + Separator + Space : string.Empty)
        + string.Join(NodeSyntax.NewLine, new[] { node.Title ?? string.Empty, node.Why, node.Body }.Where(part => part.Length > 0));

    /// <summary>Changes when any part changes, so only those rows are written again. A model's name goes in as a part.</summary>
    public static string Hash(params string[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', parts))))[..HashChars];
}
