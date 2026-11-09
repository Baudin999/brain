using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Brain.Serving;

namespace Brain.Commands;

/// <summary>
/// <c>brain ui</c>: opens brain's window, and starts <c>brain serve</c> in the background first when it is not running.
/// There is one window: the new one closes every window open before it. Bind it to a key of your own. <c>--restart</c>
/// has a running brain serve of this brain close its windows and start its successor on the build now published beside
/// it, and says which build that is; brain install runs it.
/// </summary>
internal static class UiCommand
{
    public const string Name = "ui";
    public const string RestartFlag = "restart";
    private static readonly string Usage = Help.UsageOf(Name);
    private static readonly TimeSpan Ask = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(200);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [RestartFlag]);
        options.Expect(0, Usage);
        return Run(env, options.Has(RestartFlag), ServeCommand.Port());
    }

    /// <summary><c>brain ui</c>, or <c>brain ui --restart</c> when <paramref name="restart"/> holds, on <paramref name="port"/>.</summary>
    internal static int Run(BrainEnv env, bool restart, int port)
    {
        var files = new ServeFiles(env.Home);
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)), Timeout = Ask };
        var hello = Hello(client, files).Status;
        if (hello == HttpStatusCode.Forbidden)
        {
            throw new BrainException(ServeProcess.Listener(port) is { } holder
                ? $"127.0.0.1:{port} answers, but not to this brain's key: an older brain serve or another brain's runs there. If it is an old one of this brain, fix it with: kill {holder} && brain ui. If it is another brain's, set {ServeCommand.PortVariable} to another port"
                : $"127.0.0.1:{port} answers, but not to this brain's key, and no process of this account holds it: set {ServeCommand.PortVariable} to another port");
        }

        if (restart)
        {
            if (hello != HttpStatusCode.OK)
            {
                env.Out.WriteLine($"brain ui: no brain serve of this brain runs on 127.0.0.1:{port}; nothing to restart");
                return ExitCodes.Ok;
            }

            Restart(env, client, files, port);
            env.Out.WriteLine($"brain ui: restarted brain serve on 127.0.0.1:{port}, and it opens the window; {Serves(Hello(client, files).Build, port)}");
            return ExitCodes.Ok;
        }

        if (hello != HttpStatusCode.OK)
        {
            Start(env, client, files, port);
            env.Out.WriteLine($"brain ui: started brain serve on 127.0.0.1:{port}, and it opens the window");
            return ExitCodes.Ok;
        }

        using var request = Keyed(HttpMethod.Post, "/api/window", files);
        using var response = client.Send(request);
        var window = response.IsSuccessStatusCode ? response.Content.ReadFromJsonAsync(ReviewJson.Default.WindowReply).GetAwaiter().GetResult()?.Window : null;
        if (window != "opened")
        {
            throw new BrainException($"brain serve could not open a window: set {Browser.Variable} to a Chromium-family browser");
        }

        env.Out.WriteLine("brain ui: opened brain's window; it closes every window open before it");
        return ExitCodes.Ok;
    }

    /// <summary>A request that carries the key the running brain serve wrote, when there is one.</summary>
    private static HttpRequestMessage Keyed(HttpMethod method, string path, ServeFiles files)
    {
        var request = new HttpRequestMessage(method, path);
        if (files.ReadKey() is { } key)
        {
            request.Headers.Add(ServeFiles.KeyHeader, key);
        }

        return request;
    }

    /// <summary>
    /// Which build the restarted brain serve runs: this one, or the one beside the server it replaced, with the command
    /// that serves this one instead. A successor always starts the binary beside the server it replaces.
    /// </summary>
    internal static string Serves(string? build, int port)
    {
        var mine = Folder(AppContext.BaseDirectory);
        if (build is null)
        {
            return "it does not say which build it runs";
        }

        if (Folder(build) == mine)
        {
            return $"it runs this build, in {mine}";
        }

        var stop = ServeProcess.Listener(port) is { } pid ? $"kill {pid.ToString(CultureInfo.InvariantCulture)} && " : string.Empty;
        return $"it runs the build in {Folder(build)}, not this one in {mine}. To serve this one: {stop}{Path.Combine(mine, "brain")} ui";
    }

    private static string Folder(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// OK when brain serve answers to this brain's key, with the build it runs from; Forbidden when something on the port
    /// answers but not as that, such as a brain serve from before the key or of another brain; and null when nothing
    /// answers.
    /// </summary>
    private static (HttpStatusCode? Status, string? Build) Hello(HttpClient client, ServeFiles files)
    {
        HttpResponseMessage response;
        try
        {
            using var request = Keyed(HttpMethod.Get, "/api/hello", files);
            response = client.Send(request);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return (null, null);
        }

        using (response)
        {
            try
            {
                var hello = response.IsSuccessStatusCode ? response.Content.ReadFromJsonAsync(ReviewJson.Default.Hello).GetAwaiter().GetResult() : null;
                return hello is { App: ReviewServer.App } ? (HttpStatusCode.OK, hello.Build) : (HttpStatusCode.Forbidden, null);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return (HttpStatusCode.Forbidden, null);
            }
        }
    }

    /// <summary>
    /// The one command that puts brain serve on this build: stop the process of this account that holds the port, then
    /// start it again, or only start it when nothing holds the port. Null when something this account cannot see holds
    /// it, since no command of this account fixes that.
    /// </summary>
    internal static string? Fix(int port)
    {
        if (ServeProcess.Listener(port) is { } pid)
        {
            return $"kill {pid} && brain ui";
        }

        try
        {
            using var probe = new TcpClient();
            probe.Connect(IPAddress.Loopback, port);
            return null;
        }
        catch (SocketException)
        {
            return "brain ui";
        }
    }

    /// <summary>The sentence that names <see cref="Fix"/>, or <paramref name="otherwise"/> when there is no command.</summary>
    private static string FixItWith(int port, string otherwise) => Fix(port) is { } fix ? "Fix it with: " + fix : otherwise;

    /// <summary>
    /// Asks the running brain serve to restart, and waits until its successor answers with a key of its own. The old one
    /// tells its windows to close, then starts the successor itself, from its own environment, so the successor is never
    /// a child of this process. It lands an answer in flight before it ends, so every write before the user stays before
    /// the user in the new window.
    /// </summary>
    private static void Restart(BrainEnv env, HttpClient client, ServeFiles files, int port)
    {
        var old = files.ReadKey();
        using (var request = Keyed(HttpMethod.Post, "/api/restart", files))
        using (var response = client.Send(request))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new BrainException($"brain serve on 127.0.0.1:{port} runs a build from before brain ui --restart, so it cannot restart itself. {FixItWith(port, "Stop it by hand, then run brain ui")}");
            }
        }

        Await(env, client, files, port, () => files.ReadKey() != old);
    }

    /// <summary>Starts brain serve in its own process and waits until it answers.</summary>
    private static void Start(BrainEnv env, HttpClient client, ServeFiles files, int port)
    {
        if (Browser.Find() is null)
        {
            throw new BrainException($"no Chromium-family browser found: set {Browser.Variable} to one");
        }

        ServeProcess.Start(env);
        Await(env, client, files, port, () => true);
    }

    /// <summary>Waits until brain serve answers to this brain's key and <paramref name="ready"/> holds.</summary>
    private static void Await(BrainEnv env, HttpClient client, ServeFiles files, int port, Func<bool> ready)
    {
        var deadline = DateTime.UtcNow + Startup;
        while (Hello(client, files).Status != HttpStatusCode.OK || !ready())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new BrainException($"brain serve did not answer within {Startup.TotalSeconds} seconds; see {ServeProcess.Log(env.Home)}. {FixItWith(port, "Something this account cannot stop holds the port; set " + ServeCommand.PortVariable + " to another port")}");
            }

            Thread.Sleep(Poll);
        }
    }
}
