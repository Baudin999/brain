namespace Brain;

/// <summary>
/// A project an edge names, as in <c>afterlife:wg-19</c>: for a registered project the scope its nodes are read under,
/// whose repo is the project's root, and its nodes by slug when that repo is on this machine.
/// </summary>
internal sealed record NamedProject(string Name, Scope? Scope, IReadOnlyDictionary<string, PlacedNode> Nodes)
{
    public bool Registered => Scope is not null;

    /// <summary>The repo root of a registered project, null for one projects.tsv does not name.</summary>
    public string? Root => Scope?.Repo;

    /// <summary>False for a registered project whose repo is not cloned here, so its slugs cannot be judged.</summary>
    public bool Cloned => Directory.Exists(Root);
}
