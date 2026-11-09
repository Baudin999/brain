using Brain.Installing;

namespace Brain.Commands;

/// <summary>
/// <c>brain install</c>: the guided installer. It links the installed harness into Claude Code's config directory: the
/// one a package put beside the binary, or the one the binary carries, unpacked into the data repo's cache.
/// <c>--harness</c> links the parts <c>--harness-parts</c> names to another harness root instead, as ./link.sh does for
/// brain's own development. Without either, a part linked to a checkout now stays linked there; <c>--harness-parts
/// none</c> links every part to the installed harness.
/// </summary>
internal static class InstallCommand
{
    public const string Name = "install";
    public const string YesFlag = "yes";
    public const string ModeOption = "permission-mode";
    public const string HarnessOption = "harness";
    public const string PartsOption = "harness-parts";
    private const string NoParts = "none";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments) => Run(env, arguments, Installation.FromProcess(env));

    /// <summary><c>brain install</c> with <paramref name="arguments"/>, into <paramref name="at"/>.</summary>
    internal static int Run(BrainEnv env, IReadOnlyList<string> arguments, Installation at)
    {
        var options = Options.ParseFlagged(arguments, [YesFlag], ModeOption, HarnessOption, PartsOption);
        options.Expect(0, Usage);
        var mode = options.Get(ModeOption);
        var harness = options.Get(HarnessOption) is { } dir ? Path.GetFullPath(dir) : null;
        var named = options.Get(PartsOption);
        string[]? parts = named == NoParts ? [] : named?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if ((mode is not null && !Choices.Modes.Any(each => each.Mode == mode)) ||
            (parts is not null && parts.Any(part => !InstallPlan.Parts.Contains(part, StringComparer.Ordinal))) ||
            (parts is { Length: > 0 } && harness is null))
        {
            throw BrainException.Usage(Usage);
        }

        if (harness is not null && !File.Exists(Path.Combine(harness, "settings.json")))
        {
            env.Err.WriteLine($"brain install: {harness} is no harness: it has no settings.json. Nothing was changed.");
            return ExitCodes.Failure;
        }

        var choices = new Choices(env.In, env.Out, env.Err, options.Has(YesFlag));
        return new Installer(env, at, choices, new InstallRequest(mode, harness, parts)).Run();
    }
}
