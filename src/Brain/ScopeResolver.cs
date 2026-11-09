using System.Security.Cryptography;
using System.Text;

namespace Brain;

/// <summary>Decides which scopes are in effect for a working directory.</summary>
internal static class ScopeResolver
{
    private const string KeySeparator = "-";
    private const int KeyHashLength = 12;

    /// <summary>
    /// The scopes in lookup order: project when <c>--project</c> named a repo, or when the cwd is in a repo with a brain,
    /// then global, then personal.
    /// </summary>
    public static IReadOnlyList<Scope> InEffect(BrainEnv env)
    {
        var scopes = new List<Scope>();
        var project = Project(env);
        if (project is not null)
        {
            scopes.Add(project);
        }

        scopes.Add(BrainScope(env.Home, ScopeNames.Global));
        scopes.Add(BrainScope(env.Home, ScopeNames.Personal));
        return scopes;
    }

    /// <summary>The personal or global scope of the brain at <paramref name="home"/>.</summary>
    public static Scope BrainScope(string home, string name) =>
        new(name, Path.Combine(home, Layout.GraphDir, name), LogPath(home, name), Repo: null);

    private static Scope? Project(BrainEnv env)
    {
        var top = env.ProjectRepo ?? Git.TopLevel(env.Cwd);
        if (top is null || string.Equals(top, Git.TopLevel(env.Home), StringComparison.Ordinal))
        {
            return null;
        }

        var scope = ForRepo(env.Home, ScopeNames.Project, top);
        return Directory.Exists(scope.GraphDir) ? scope : null;
    }

    /// <summary>The scope a repo's docs/brain makes, under <paramref name="name"/>, with its edge log in the brain's cache.</summary>
    public static Scope ForRepo(string home, string name, string repo) =>
        new(name, ProjectScaffold.GraphDir(repo), LogPath(home, RepoKey(repo)), repo);

    /// <summary>
    /// The edge log of the scope keyed <paramref name="key"/>, in the brain's gitignored cache: edge weights are derived,
    /// so reading a graph never changes a repo.
    /// </summary>
    private static string LogPath(string home, string key) =>
        Path.Combine(home, Layout.CacheDir, Layout.TraversalsDir, key + Layout.TraversalsExtension);

    /// <summary>
    /// A project's log key: the main checkout's folder name, then a hash of its full path, so two repos never share a log
    /// and every worktree of one repo shares one.
    /// </summary>
    private static string RepoKey(string repo)
    {
        repo = Git.MainCheckout(repo) ?? repo;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(repo)));
        return Path.GetFileName(repo) + KeySeparator + hash[..KeyHashLength];
    }
}
