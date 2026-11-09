using Brain.Serving;
using Brain.Workspace;

namespace Brain.Commands;

/// <summary>
/// <c>brain workspace --serve &lt;path&gt;</c>: runs the server of one project folder's workspace in the foreground,
/// its file tree, its reader and git. It opens no window of its own: brain code starts it, in the background, the first
/// time it shows a session's folder, and shows its page in a frame of brain code's window. It is apart from brain
/// serve.
/// </summary>
internal static class WorkspaceCommand
{
    public const string Name = "workspace";
    public const string ServeFlag = "serve";
    private const string LogFile = "serve.log";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [ServeFlag]);
        var root = Root(env, options.Expect(1, Usage)[0]);
        if (!options.Has(ServeFlag))
        {
            throw new BrainException($"a workspace opens no window of its own: brain code shows a session's folder as its workspace\n{Usage}");
        }

        return Serve(env, root);
    }

    /// <summary>
    /// Starts the server of <paramref name="root"/>'s workspace in a process of its own, on the binary beside this one,
    /// and gives the log it writes.
    /// </summary>
    public static string StartServer(BrainEnv env, string root)
    {
        var log = Path.Combine(new ServeFiles(env.Home, WorkspaceServer.Folder(WorkspaceServer.Id(root))).Dir, LogFile);
        ServeProcess.Start(log, root, [Name, "--" + ServeFlag, root], "the workspace's server");
        return log;
    }

    /// <summary>The folder <paramref name="path"/> names from the working directory, refused when there is none.</summary>
    private static string Root(BrainEnv env, string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, env.Cwd));
        if (!Directory.Exists(full))
        {
            throw new BrainException($"{full} is no folder");
        }

        return full;
    }

    /// <summary>Runs the folder's server until nothing shows it, or an interrupt or a terminate signal.</summary>
    private static int Serve(BrainEnv env, string root)
    {
        var theme = new OmarchyTheme(env.UserHome, OmarchyTheme.FindCommand());
        var server = new WorkspaceServer(env.Home, env.UserHome, root, env.Err, TimeProvider.System, theme);
        Foreground.Run(server.Serve);
        return ExitCodes.Ok;
    }
}
