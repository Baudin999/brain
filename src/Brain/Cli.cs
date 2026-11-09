using System.Diagnostics.CodeAnalysis;
using Brain.Commands;
using Brain.Usage;

namespace Brain;

/// <summary>
/// Dispatches the first argument to its command and turns refusals into stderr and exit 1. Any other failure goes on to
/// <see cref="Guard"/>, the process's one boundary, so the terminal never shows a stack.
/// </summary>
internal static class Cli
{
    private const string App = "brain";
    private const string ErrorPrefix = App + ": ";

    internal static readonly Dictionary<string, Func<BrainEnv, IReadOnlyList<string>, int>> Commands =
        new(StringComparer.Ordinal)
        {
            [ContextCommand.Name] = ContextCommand.Run,
            [LookupCommand.Name] = LookupCommand.Run,
            [NearestCommand.Name] = NearestCommand.Run,
            [NodeCommand.Name] = NodeCommand.Run,
            [WalkCommand.Name] = WalkCommand.Run,
            [AddCommand.Name] = AddCommand.Run,
            [UpdateCommand.Name] = UpdateCommand.Run,
            [SubjectsCommand.Name] = SubjectsCommand.Run,
            [EditCommand.Name] = EditCommand.Run,
            [LinkCommand.Name] = LinkCommand.Run,
            [UnlinkCommand.Name] = UnlinkCommand.Run,
            [ForgetCommand.Name] = ForgetCommand.Run,
            [MergeCommand.Name] = MergeCommand.Run,
            [LandCommand.Name] = LandCommand.Run,
            [ServeCommand.Name] = ServeCommand.Run,
            [UiCommand.Name] = UiCommand.Run,
            [WorkspaceCommand.Name] = WorkspaceCommand.Run,
            [CodeCommand.Name] = CodeCommand.Run,
            [AskCommand.Name] = AskCommand.Run,
            [ConversationCommand.Name] = ConversationCommand.Run,
            [DreamCommand.Name] = DreamCommand.Run,
            [CheckCommand.Name] = CheckCommand.Run,
            [MigrateCommand.Name] = MigrateCommand.Run,
            [FadedCommand.Name] = FadedCommand.Run,
            [FoldCommand.Name] = FoldCommand.Run,
            [VoicesCommand.Name] = VoicesCommand.Run,
            [InitCommand.Name] = InitCommand.Run,
            [StatsCommand.Name] = StatsCommand.Run,
            [RecordCommand.Name] = RecordCommand.Run,
            [InstallCommand.Name] = InstallCommand.Run,
            [UninstallCommand.Name] = UninstallCommand.Run,
        };

    /// <summary>
    /// The one boundary of a brain process, around everything it runs: <paramref name="run"/> builds its world and runs
    /// the call. Any failure but a refusal ends here as one line on <paramref name="errors"/> and exit 1, and as an error
    /// with the whole exception in the usage log of <paramref name="home"/>, when one is known.
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The CLI's one boundary: every failure is told and recorded, never a stack trace.")]
    public static int Guard(IReadOnlyList<string> arguments, string? home, string? session, TextWriter errors, Func<int> run)
    {
        try
        {
            return run();
        }
        catch (Exception error)
        {
            var command = Metering.Split(arguments).Command;
            var failed = command.Length > 0 ? $"{App} {command}" : App;
            errors.WriteLine(ErrorPrefix + Failures.Record(home, failed, error, session, errors));
            return ExitCodes.Failure;
        }
    }

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        try
        {
            (env, arguments) = ProjectFlag.Apply(env, arguments);
            if (arguments.Count == 0)
            {
                env.Err.WriteLine(Help.Overview());
                return ExitCodes.Usage;
            }

            return Dispatch(env, arguments);
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            env.Err.WriteLine(ErrorPrefix + refusal.Message);
            if (refusal.IsUsage)
            {
                env.Err.WriteLine(Help.HintFor(arguments.Count > 0 ? arguments[0] : string.Empty));
            }

            return ExitCodes.Failure;
        }
    }

    private static int Dispatch(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var name = arguments[0];
        if (name is "help" || Help.IsHelpFlag(name))
        {
            var topic = name is "help" && arguments.Count > 1 ? arguments[1] : null;
            return topic is null ? PrintHelp(env, null) : Help.Find(topic) is { } entry ? PrintHelp(env, entry) : Unknown(env, topic);
        }

        if (!Commands.TryGetValue(name, out var command))
        {
            return Unknown(env, name);
        }

        return arguments.Count > 1 && Help.IsHelpFlag(arguments[1]) ? PrintHelp(env, Help.Find(name)) : command(env, [.. arguments.Skip(1)]);
    }

    private static int PrintHelp(BrainEnv env, HelpEntry? entry)
    {
        env.Out.WriteLine(entry is null ? Help.Overview() : Help.For(entry));
        return ExitCodes.Ok;
    }

    private static int Unknown(BrainEnv env, string name)
    {
        env.Err.WriteLine($"{ErrorPrefix}unknown command: {name}");
        env.Err.WriteLine(Help.HintOverview);
        return ExitCodes.Usage;
    }
}
