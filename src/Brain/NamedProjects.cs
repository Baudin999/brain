namespace Brain;

/// <summary>The registered projects edges name, each read from its own docs/brain once, on first use.</summary>
internal sealed class NamedProjects(string home, string userHome)
{
    private readonly Dictionary<string, NamedProject> loaded = new(StringComparer.Ordinal);

    public NamedProject Get(string name)
    {
        if (!loaded.TryGetValue(name, out var project))
        {
            project = Load(name);
            loaded[name] = project;
        }

        return project;
    }

    /// <summary>Every registered project whose repo is on this machine.</summary>
    public IEnumerable<NamedProject> Cloned() =>
        ProjectRegistry.All(home).Select(entry => Get(entry.Name)).Where(project => project.Cloned);

    private NamedProject Load(string name)
    {
        var root = ProjectRegistry.Named(home, name)?.Root(userHome);
        if (root is null)
        {
            return new NamedProject(name, Scope: null, new Dictionary<string, PlacedNode>());
        }

        var scope = ScopeResolver.ForRepo(home, name, root);
        var nodes = NodeStore.All(scope).ToDictionary(node => node.Slug, node => new PlacedNode(scope, node), StringComparer.Ordinal);
        return new NamedProject(name, scope, nodes);
    }
}
