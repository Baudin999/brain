namespace Brain;

/// <summary>
/// A ref naming a node. A null scope means the scope of whoever holds the ref. A scope that is not one of the three
/// names a registered project, as in <c>afterlife:wg-19</c>.
/// </summary>
internal sealed record NodeRef(string Text, string? Scope, string Slug) : IRef
{
    public string? Project => Scope is null || ScopeNames.IsScope(Scope) ? null : Scope;

    public NodeRef Node => this;

    public string? Problem()
    {
        if (!Slugs.IsValid(Slug))
        {
            return $"bad slug in {Text}";
        }

        return Project is null || Slugs.IsValid(Project) ? null : $"bad project name in {Text}";
    }

    public static string Qualified(string scope, string slug) => scope + Ref.Separator + slug;
}
