using System.Globalization;

namespace Brain.Commands;

/// <summary>Gives the project around the working directory a docs/brain and registers it in the brain's projects.tsv.</summary>
internal static class InitCommand
{
    public const string Name = "init";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string NoRemote = "-";
    private const string Parent = "..";
    private const string Created = "created ";
    private const string Exists = "exists ";
    private const string Registered = "registered";
    private const string AlreadyRegistered = "already registered";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var positional = Options.Parse(arguments).Positional;
        var repo = Repo(env);
        var project = new RegisteredProject(
            ProjectName(positional, repo),
            Shown(env.UserHome, repo),
            Git.OriginUrl(repo) ?? NoRemote,
            env.Today.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture));
        var registered = ProjectRegistry.Holds(env.Home, project);
        var root = ProjectScaffold.Root(repo);
        env.Out.WriteLine((ProjectScaffold.Create(repo) ? Created : Exists) + root);
        if (!registered)
        {
            ProjectRegistry.Append(env.Home, project);
        }

        var state = registered ? AlreadyRegistered : Registered;
        env.Out.WriteLine($"{state} {project.Name} {project.RepoPath} {project.Remote}");
        return ExitCodes.Ok;
    }

    private static string Repo(BrainEnv env)
    {
        var repo = Git.TopLevel(env.Cwd) ?? throw new BrainException("not inside a git repo; run brain init from a project");
        return string.Equals(repo, Git.TopLevel(env.Home), StringComparison.Ordinal)
            ? throw new BrainException("this is the brain itself; run brain init from a project")
            : repo;
    }

    private static string ProjectName(IReadOnlyList<string> positional, string repo)
    {
        if (positional.Count > 1)
        {
            throw BrainException.Usage(Usage);
        }

        var name = positional.Count == 1 ? positional[0] : Slugs.From(Path.GetFileName(repo)) ?? string.Empty;
        if (ScopeNames.IsScope(name) || TypedRefKinds.IsKind(name))
        {
            throw new BrainException($"name {name} is a scope or a ref kind, so {name}:slug would not name this project; pass another name");
        }

        return Slugs.IsValid(name)
            ? name
            : throw new BrainException($"name {name} is not lowercase-hyphenated; usage: {Usage}");
    }

    /// <summary>The repo path with the user's home as ~, so it reads the same after a restore.</summary>
    private static string Shown(string userHome, string repo)
    {
        var relative = Path.GetRelativePath(userHome, repo);
        var outside = relative.StartsWith(Parent, StringComparison.Ordinal) || Path.IsPathRooted(relative);
        return outside ? repo : Path.Combine(RegisteredProject.HomeMark, relative);
    }
}
