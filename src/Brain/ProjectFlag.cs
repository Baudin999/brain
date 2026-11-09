namespace Brain;

/// <summary>
/// The global <c>--project &lt;name|path&gt;</c> before the command: puts that project's scope in effect from any
/// directory, for an agent that may not leave its own. A name is looked up in projects.tsv, anything else is a path.
/// </summary>
internal static class ProjectFlag
{
    public const string Flag = "--project";

    /// <summary>Strips a leading <c>--project</c> and its value; refuses a value that names no project with a brain.</summary>
    public static (BrainEnv Env, IReadOnlyList<string> Arguments) Apply(BrainEnv env, IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || !string.Equals(arguments[0], Flag, StringComparison.Ordinal))
        {
            return (env, arguments);
        }

        if (arguments.Count < 2)
        {
            throw new BrainException($"{Flag} needs a project name from projects.tsv or a repo path");
        }

        return (env with { ProjectRepo = Resolve(env, arguments[1]) }, [.. arguments.Skip(2)]);
    }

    private static string Resolve(BrainEnv env, string value)
    {
        var registered = ProjectRegistry.Named(env.Home, value);
        var directory = registered?.Root(env.UserHome) ?? Path.GetFullPath(value, env.Cwd);
        var repo = Git.TopLevel(directory);
        if (repo is null)
        {
            throw new BrainException(registered is null
                ? $"{value} is neither a project in projects.tsv nor a git repo"
                : $"project {value} is not cloned here: {directory}");
        }

        var brainRepo = Git.TopLevel(env.Home);
        if (string.Equals(repo, brainRepo, StringComparison.Ordinal) || !Directory.Exists(ScopeResolver.ForRepo(env.Home, ScopeNames.Project, repo).GraphDir))
        {
            throw new BrainException($"{repo} has no project brain; run brain init there");
        }

        return repo;
    }
}
