namespace Brain;

/// <summary>
/// What add and merge ask for when they create a node: its title, its body and why, empty when not given, and its
/// subjects, none when not given.
/// </summary>
internal sealed record NewNode(string Scope, string Slug, string Title, string Kind, string NamedBy, string? Session)
{
    public const string DefaultKind = Kinds.Information;
    public const string DefaultNamer = FieldNames.Claude;

    public string Body { get; init; } = string.Empty;

    public string Why { get; init; } = string.Empty;

    public IReadOnlyList<string> Subjects { get; init; } = [];
}
