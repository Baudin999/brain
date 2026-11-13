using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brain.Code;
using Brain.Serving;
using Brain.Windows;

namespace Brain.Commands;

/// <summary>
/// <c>brain code [agent] [folder] [--resume &lt;session id&gt;]</c>: starts a Claude Code session of the agent, the open
/// agent unless named, in the folder, the current one unless named, in that project's brain code window, and brings
/// the window forward. A positional that names an existing folder is the folder and any other is the agent, in either
/// order. A project is a folder, and each has its own window: its server, a process of its own apart from brain
/// serve's, every workspace's and every other project's, runs every session the window shows and starts with the
/// project's first <c>brain code</c>. <c>--resume</c> moves a session that runs elsewhere, as in a terminal, into the
/// window of the project it ran in, where it continues by its id. <c>--serve &lt;folder&gt;</c> runs the folder's
/// server in the foreground, which <c>brain code</c> starts in the background, and which starts its own successor when
/// the user reloads it on a newer build. <c>--permission &lt;session id&gt; &lt;folder&gt;</c> is the
/// PermissionRequest hook each session carries: it hands the request on its stdin to the server of the session's
/// project, waits for the user's answer in the window, and writes the decision; with no server, or one that went away,
/// it writes deny, so it never hangs.
/// <para>
/// The forms that name no folder are those of the one shared server of the builds before each project had its own
/// window, and of its sessions. A hook that names none reaches that server in <c>cache/code</c>. A serve that names
/// none is that server's successor once the user reloads it: it moves each of its sessions into its project's window.
/// </para>
/// </summary>
internal static class CodeCommand
{
    public const string Name = "code";
    public const string ServeFlag = "serve";
    public const string ResumeOption = "resume";
    public const string WindowClass = "brain-code";
    private const string LogFile = "serve.log";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly string Usage = Help.UsageOf(Name);

    // navarone: 0000167
    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [ServeFlag], ResumeOption, PermissionHook.Flag);
        if (options.Has(ServeFlag))
        {
            return Served(env, options);
        }

        if (options.Get(PermissionHook.Flag) is { } session)
        {
            return Permission(env, session, options);
        }

        var (agent, folder) = Positionals(env, options.Expect(options.Positional.Count, Usage));
        var resume = options.Get(ResumeOption);
        if (folder is null && resume is null)
        {
            folder = Folders.Resolve(".", env.UserHome, env.Cwd);
        }

        return Open(env, new CodeStart(agent, folder, resume));
    }

    /// <summary>
    /// The project a serve or a hook names, in full; null when it names none, as the server and the sessions of a build
    /// from before each project had its own window do.
    /// </summary>
    private static string? Named(BrainEnv env, Options options)
    {
        if (options.Positional.Count == 0)
        {
            return null;
        }

        return Folders.Resolve(options.Expect(1, Usage)[0], env.UserHome, env.Cwd);
    }

    /// <summary>
    /// Tells the agent from the folder among the positionals: one that names an existing folder is the folder, one
    /// spelled as a path that names none is refused, and any other is the agent. Two of a kind are refused.
    /// </summary>
    internal static (string? Agent, string? Folder) Positionals(BrainEnv env, IReadOnlyList<string> positional)
    {
        string? agent = null;
        string? folder = null;
        foreach (var typed in positional)
        {
            var path = Folders.Resolve(typed, env.UserHome, env.Cwd);
            if (Directory.Exists(path))
            {
                BrainException.ThrowIf(folder is not null ? $"two folders, {folder} and {path}: a session runs in one\n{Usage}" : null);
                folder = path;
                continue;
            }

            BrainException.ThrowIf(Folders.LooksLikePath(typed) ? $"{path} is no folder\n{Usage}" : null);
            BrainException.ThrowIf(agent is not null ? $"two agents, {agent} and {typed}: a session runs one\n{Usage}" : null);
            agent = typed;
        }

        return (agent, folder);
    }

    /// <summary>
    /// Starts the session in the running server of its project's window, or starts that server, which opens the window,
    /// and then the session. Another project's window is left as it is.
    /// </summary>
    private static int Open(BrainEnv env, CodeStart start)
    {
        var project = ProjectOf(env, start);
        var files = CodeProjectFiles.Of(env.Home, project);
        var running = WindowClient.Running(files, CodeServer.App, project);
        var raise = running is not null;
        if (running is null)
        {
            BrainException.ThrowIf(Browser.Find() is null ? $"no Chromium-family browser found: set {Browser.Variable} to one" : null);
            var log = StartServer(env, project);
            running = WindowClient.Await(files, CodeServer.App, project, log);
        }

        using var client = running;
        var session = Started(client, files, start with { Cwd = project });
        Ended(client, files, session.Id);
        if (raise)
        {
            WindowClient.Raise(client, files);
        }

        if (start.Resume is not null)
        {
            env.Out.WriteLine($"brain code: moved session {session.Id} into the window, in {session.Cwd}; close its terminal yourself");
            return ExitCodes.Ok;
        }

        env.Out.WriteLine($"brain code: started {session.Agent} in {session.Cwd}, session {session.Id}");
        return ExitCodes.Ok;
    }

    /// <summary>
    /// The project whose window the session runs in: the folder the start names, or for a moved session the folder its
    /// transcript says it ran in; refused when there is neither, or the two differ.
    /// </summary>
    private static string ProjectOf(BrainEnv env, CodeStart start)
    {
        var folder = start.Cwd;
        if (start.Resume is not null)
        {
            folder = CodeSessions.RunsIn(ClaudeFiles.Of(env.UserHome), start.Resume, start.Cwd);
        }

        if (folder is null)
        {
            throw new BrainException($"the session {start.Resume} has no folder to continue in: name it\n{Usage}");
        }

        return folder;
    }

    /// <summary>
    /// Starts the server of <paramref name="project"/>'s window in a process of its own, on the binary beside this one,
    /// and gives the log it writes: <c>brain code</c> starts the first, and a server the user reloads starts its
    /// successor so, from its own environment.
    /// </summary>
    private static string StartServer(BrainEnv env, string project)
    {
        var log = Path.Combine(CodeProjectFiles.Of(env.Home, project).Dir, LogFile);
        ServeProcess.Start(log, env.UserHome, [Name, "--" + ServeFlag, project], "brain code's server");
        return log;
    }

    /// <summary>
    /// Waits a moment for a session whose claude fails at once, such as a wrong agent, and says why it ended. A session
    /// that is alive after that is left to the window.
    /// </summary>
    private static void Ended(HttpClient client, ServeFiles files, string id)
    {
        var until = DateTimeOffset.UtcNow + Settle;
        while (DateTimeOffset.UtcNow < until)
        {
            using var request = WindowClient.Keyed(HttpMethod.Get, "/api/key/session?id=" + Uri.EscapeDataString(id), files);
            using var response = client.Send(request);
            Checked(response);
            var view = response.Content.ReadFromJsonAsync(CodeJson.Default.CodeSessionView).GetAwaiter().GetResult();
            if (view is { Alive: false })
            {
                throw new BrainException(view.Problem ?? "the session ended at once");
            }

            Thread.Sleep(Poll);
        }
    }

    /// <summary>Lets a good answer through; the server's refusal is the command's message, and any other answer is a failure.</summary>
    private static void Checked(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = response.Content.ReadFromJsonAsync(ReviewJson.Default.Problem).GetAwaiter().GetResult();
            throw new BrainException(problem?.Error ?? "brain code's server refused the session");
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>Asks the server to start the session; its refusal is the command's.</summary>
    private static CodeSessionView Started(HttpClient client, ServeFiles files, CodeStart start)
    {
        using var request = WindowClient.Keyed(HttpMethod.Post, "/api/key/start", files);
        request.Content = JsonContent.Create(start, CodeJson.Default.CodeStart);
        using var response = client.Send(request);
        Checked(response);
        var session = response.Content.ReadFromJsonAsync(CodeJson.Default.CodeSessionView).GetAwaiter().GetResult();
        return session ?? throw BrainException.Failed("brain code's server said nothing of the session");
    }

    /// <summary>
    /// The hook's run: the request on stdin goes to the server of the project the hook names, or to the shared server
    /// of the builds before when it names none, and its decision, or deny, goes to stdout.
    /// </summary>
    private static int Permission(BrainEnv env, string session, Options options)
    {
        var files = new ServeFiles(env.Home, CodeServer.Folder);
        var root = env.UserHome;
        if (Named(env, options) is { } project)
        {
            files = CodeProjectFiles.Of(env.Home, project);
            root = project;
        }

        var decision = CodeDecision.Deny("brain code could not read the permission request.");
        if (PermissionHook.Request(env.In.ReadToEnd(), session) is { } asked)
        {
            decision = Asked(files, root, asked);
        }

        env.Out.WriteLine(PermissionHook.Output(decision));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Hands the request to the server and waits, as long as it takes, for the user's answer; deny when no server runs
    /// or it went away before he answered.
    /// </summary>
    private static CodeDecision Asked(ServeFiles files, string root, CodePermission asked)
    {
        using var running = WindowClient.Running(files, CodeServer.App, root);
        if (running is null)
        {
            return CodeDecision.Deny("brain code's window is not open, so nobody can answer.");
        }

        using var waiting = new HttpClient { BaseAddress = running.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        using var request = WindowClient.Keyed(HttpMethod.Post, "/api/key/permission", files);
        request.Content = JsonContent.Create(asked, CodeJson.Default.CodePermission);
        try
        {
            using var response = waiting.Send(request);
            response.EnsureSuccessStatusCode();
            var decision = response.Content.ReadFromJsonAsync(CodeJson.Default.CodeDecision).GetAwaiter().GetResult();
            return decision ?? CodeDecision.Deny("brain code's server gave no decision.");
        }
        catch (Exception gone) when (gone is HttpRequestException or IOException or JsonException)
        {
            return CodeDecision.Deny("brain code's server went away before the user answered.");
        }
    }

    /// <summary>
    /// The serve's run: the server of the project it names, or, when it names none, the successor of the shared server
    /// of the builds before.
    /// </summary>
    private static int Served(BrainEnv env, Options options)
    {
        if (Named(env, options) is { } project)
        {
            return Serve(env, project);
        }

        return Rehome(env);
    }

    /// <summary>
    /// The successor a shared server of a build from before each project had its own window starts when the user
    /// reloads it: it takes that server's handover and moves each session that was alive into the window of its
    /// project, as <c>brain code --resume</c> does, and says why of one that would not move. A session that had ended
    /// is not carried. With no handover waiting, a serve that names no folder is refused.
    /// </summary>
    private static int Rehome(BrainEnv env)
    {
        var handover = Handover.Take(new ServeFiles(env.Home, CodeServer.Folder));
        if (handover is null)
        {
            throw new BrainException($"brain code --serve names the folder whose window it serves\n{Usage}");
        }

        foreach (var carried in handover.Sessions)
        {
            if (!carried.Alive)
            {
                continue;
            }

            try
            {
                Open(env, new CodeStart(carried.Agent, carried.Cwd, carried.Id));
            }
            catch (BrainException refusal) when (!refusal.IsFailure)
            {
                env.Err.WriteLine($"brain code: session {carried.Id} did not move into its project's window: {refusal.Message}");
            }
        }

        return ExitCodes.Ok;
    }

    /// <summary>
    /// Runs the server of <paramref name="project"/>'s window until its window is gone, or an interrupt or a terminate
    /// signal; every session stops with it.
    /// </summary>
    private static int Serve(BrainEnv env, string project)
    {
        var browser = Browser.Find();
        var theme = new OmarchyTheme(env.UserHome, OmarchyTheme.FindCommand());
        using var server = new CodeServer(env.Home, env.UserHome, project, "claude", ClaudeFiles.Of(env.UserHome), env.Err, url => WindowClient.OpenWindow(env, browser, url, WindowClass, "brain code"), Compositor.Raise, TimeProvider.System, theme, new CodeBuild(AppContext.BaseDirectory), () => StartServer(env, project), root => WorkspaceCommand.StartServer(env, root));
        Foreground.Run(server.Serve);
        return ExitCodes.Ok;
    }
}
