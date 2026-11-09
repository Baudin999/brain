using Brain.Installing;

namespace Brain.Commands;

/// <summary><c>brain uninstall</c>: puts back what every <c>brain install</c> changed, from its record, and leaves the data repo alone.</summary>
internal static class UninstallCommand
{
    public const string Name = "uninstall";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments) => Run(env, arguments, Installation.FromProcess(env));

    /// <summary><c>brain uninstall</c> with <paramref name="arguments"/>, from <paramref name="at"/>.</summary>
    internal static int Run(BrainEnv env, IReadOnlyList<string> arguments, Installation at)
    {
        var options = Options.ParseFlagged(arguments, [InstallCommand.YesFlag]);
        options.Expect(0, Usage);
        return new Uninstaller(env, at, new Choices(env.In, env.Out, env.Err, options.Has(InstallCommand.YesFlag))).Run();
    }
}
