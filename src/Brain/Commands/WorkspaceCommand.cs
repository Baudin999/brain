using Brain.Serving;
using Brain.Windows;
using Brain.Workspace;

namespace Brain.Commands;

/// <summary>
/// <c>brain workspace &lt;path&gt;</c>: opens a workspace window over one project folder, its file tree beside the file
/// open in it. Each folder has its own server, a process of its own, and its own window; a folder that is open already
/// has its window brought forward. <c>--serve</c> runs that server in the foreground, which <c>brain workspace</c>
/// starts in the background. It is apart from brain's window and brain serve.
/// </summary>
internal static class WorkspaceCommand
{
    public const string Name = "workspace";
    public const string ServeFlag = "serve";
    public const string WindowClass = "brain-workspace";
    private const string LogFile = "serve.log";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [ServeFlag]);
        var root = Root(env, options.Expect(1, Usage)[0]);
        if (options.Has(ServeFlag))
        {
            return Serve(env, root);
        }

        return Open(env, root);
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

    /// <summary>Brings the window of the folder's running server forward, or starts its server, which opens one.</summary>
    private static int Open(BrainEnv env, string root)
    {
        var files = new ServeFiles(env.Home, WorkspaceServer.Folder(WorkspaceServer.Id(root)));
        using var client = WindowClient.Running(files, WorkspaceServer.App, root);
        if (client is not null)
        {
            var window = WindowClient.Raise(client, files);
            env.Out.WriteLine(window switch
            {
                "raised" => $"brain workspace: {root} is open; brought its window forward",
                "opened" => $"brain workspace: {root} is open; opened its window again",
                _ => throw new BrainException($"the workspace of {root} could not open a window: set {Browser.Variable} to a Chromium-family browser"),
            });
            return ExitCodes.Ok;
        }

        if (Browser.Find() is null)
        {
            throw new BrainException($"no Chromium-family browser found: set {Browser.Variable} to one");
        }

        var log = Path.Combine(files.Dir, LogFile);
        ServeProcess.Start(log, root, [Name, "--" + ServeFlag, root], "the workspace's server");
        using var started = WindowClient.Await(files, WorkspaceServer.App, root, log);
        env.Out.WriteLine($"brain workspace: opened a window over {root}");
        return ExitCodes.Ok;
    }

    /// <summary>Runs the folder's server until its window is gone, or an interrupt or a terminate signal.</summary>
    private static int Serve(BrainEnv env, string root)
    {
        var browser = Browser.Find();
        var theme = new OmarchyTheme(env.UserHome, OmarchyTheme.FindCommand());
        var server = new WorkspaceServer(env.Home, env.UserHome, root, env.Err, url => WindowClient.OpenWindow(env, browser, url, WindowClass, "brain workspace"), Compositor.Raise, TimeProvider.System, theme);
        Foreground.Run(server.Serve);
        return ExitCodes.Ok;
    }
}
