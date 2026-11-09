using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Brain.Code;
using Brain.Serving;
using Brain.Windows;

namespace Brain.Commands;

/// <summary>
/// <c>brain code [agent] [folder] [--resume &lt;session id&gt;]</c>: starts a Claude Code session of the agent, the open
/// agent unless named, in the folder, the current one unless named, in brain code's window, and brings the window
/// forward. A positional that names an existing folder is the folder and any other is the agent, in either order. There is one such window:
/// its server, a process of its own apart from brain serve's and every workspace's, runs every session the window shows
/// and starts with the first <c>brain code</c>. <c>--resume</c> moves a session that runs elsewhere, as in a terminal,
/// into the window, where it continues by its id in the folder it ran in. <c>--serve</c> runs that server in the
/// foreground, which <c>brain code</c> starts in the background, and which starts its own successor when the user reloads
/// it on a newer build. <c>--permission &lt;session id&gt;</c> is the
/// PermissionRequest hook each session carries: it hands the request on its stdin to the server, waits for the user's
/// answer in the window, and writes the decision; with no server, or one that went away, it writes deny, so it never
/// hangs.
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
            options.Expect(0, Usage);
            return Serve(env);
        }

        if (options.Get(PermissionHook.Flag) is { } session)
        {
            options.Expect(0, Usage);
            return Permission(env, session);
        }

        var (agent, folder) = Positionals(env, options.Expect(options.Positional.Count, Usage));
        var resume = options.Get(ResumeOption);
        if (folder is null && resume is null)
        {
            folder = env.Cwd;
        }

        return Open(env, new CodeStart(agent, folder, resume));
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

    /// <summary>Starts the session in the window's running server, or starts its server, which opens the window, and then the session.</summary>
    private static int Open(BrainEnv env, CodeStart start)
    {
        var files = new ServeFiles(env.Home, CodeServer.Folder);
        var running = WindowClient.Running(files, CodeServer.App, env.UserHome);
        var raise = running is not null;
        if (running is null)
        {
            BrainException.ThrowIf(Browser.Find() is null ? $"no Chromium-family browser found: set {Browser.Variable} to one" : null);
            var log = StartServer(env);
            running = WindowClient.Await(files, CodeServer.App, env.UserHome, log);
        }

        using var client = running;
        var session = Started(client, files, start);
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
    /// Starts brain code's server in a process of its own, on the binary beside this one, and gives the log it writes:
    /// <c>brain code</c> starts the first, and a server the user reloads starts its successor so, from its own
    /// environment.
    /// </summary>
    private static string StartServer(BrainEnv env)
    {
        var log = Path.Combine(new ServeFiles(env.Home, CodeServer.Folder).Dir, LogFile);
        ServeProcess.Start(log, env.UserHome, [Name, "--" + ServeFlag], "brain code's server");
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

    /// <summary>The hook's run: the request on stdin goes to the server, and its decision, or deny, goes to stdout.</summary>
    private static int Permission(BrainEnv env, string session)
    {
        var decision = CodeDecision.Deny("brain code could not read the permission request.");
        if (PermissionHook.Request(env.In.ReadToEnd(), session) is { } asked)
        {
            decision = Asked(env, asked);
        }

        env.Out.WriteLine(PermissionHook.Output(decision));
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Hands the request to the window's server and waits, as long as it takes, for the user's answer; deny when no
    /// server runs or it went away before he answered.
    /// </summary>
    private static CodeDecision Asked(BrainEnv env, CodePermission asked)
    {
        var files = new ServeFiles(env.Home, CodeServer.Folder);
        using var running = WindowClient.Running(files, CodeServer.App, env.UserHome);
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

    /// <summary>Runs the window's server until its window is gone, or an interrupt or a terminate signal; every session stops with it.</summary>
    private static int Serve(BrainEnv env)
    {
        var browser = Browser.Find();
        var theme = new OmarchyTheme(env.UserHome, OmarchyTheme.FindCommand());
        using var server = new CodeServer(env.Home, env.UserHome, "claude", ClaudeFiles.Of(env.UserHome), env.Err, url => WindowClient.OpenWindow(env, browser, url, WindowClass, "brain code"), Compositor.Raise, TimeProvider.System, theme, new CodeBuild(AppContext.BaseDirectory), () => StartServer(env), root => WorkspaceCommand.StartServer(env, root));
        Foreground.Run(server.Serve);
        return ExitCodes.Ok;
    }
}
