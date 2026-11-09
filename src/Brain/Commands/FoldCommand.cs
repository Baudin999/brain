namespace Brain.Commands;

/// <summary>
/// The one-off migration of the old row logs: rewrites the edge log of global, personal and every project in projects.tsv
/// folded, one line per edge. Exponential decay makes the fold exact, so every weight reads the same after it. A log
/// already folded is rewritten unchanged.
/// </summary>
internal static class FoldCommand
{
    public const string Name = "fold";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Usage);
        var projects = ProjectRegistry.All(env.Home).Select(project => ScopeResolver.ForRepo(env.Home, project.Name, project.Root(env.UserHome)));
        var scopes = projects.Prepend(ScopeResolver.BrainScope(env.Home, ScopeNames.Personal)).Prepend(ScopeResolver.BrainScope(env.Home, ScopeNames.Global));
        foreach (var path in scopes.Select(scope => scope.LogPath))
        {
            if (!File.Exists(path))
            {
                env.Out.WriteLine($"{path}: no log");
                continue;
            }

            var (before, after) = EdgeLog.Fold(path);
            env.Out.WriteLine($"{path}: {before} lines -> {after} lines");
        }

        return ExitCodes.Ok;
    }
}
