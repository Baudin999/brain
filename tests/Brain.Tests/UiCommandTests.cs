using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Brain.Commands;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// brain ui --restart, which brain install runs, against stand-ins for what may hold brain's port: a brain serve from
/// before the restart, something that refuses this brain's key, and nothing. When the window cannot restart, the refusal
/// names the one command that fixes it, so install can say so.
/// </summary>
public sealed class UiCommandTests : IDisposable
{
    private readonly TempBrain brain = new();
    private readonly StringWriter output = new();

    public void Dispose()
    {
        output.Dispose();
        brain.Dispose();
    }

    [Fact]
    public void AServerFromBeforeTheRestartNamesTheCommandThatFixesIt()
    {
        var key = new ServeFiles(brain.Home).WriteKey();
        using var old = new StandIn(request => request.Url!.AbsolutePath == "/api/hello" && request.Headers[ServeFiles.KeyHeader] == key
            ? (HttpStatusCode.OK, JsonSerializer.Serialize(new Hello(ReviewServer.App, Build: null), ReviewJson.Default.Hello))
            : (HttpStatusCode.Unauthorized, "{}"));

        var refusal = Assert.Throws<BrainException>(() => UiCommand.Run(Env(), restart: true, old.Port));

        Assert.Contains("runs a build from before brain ui --restart", refusal.Message, StringComparison.Ordinal);
        Assert.EndsWith($"Fix it with: kill {Environment.ProcessId} && brain ui", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingThatRefusesTheKeyIsNotNothingToRestart()
    {
        using var other = new StandIn(_ => (HttpStatusCode.Forbidden, "{}"));

        var refusal = Assert.Throws<BrainException>(() => UiCommand.Run(Env(), restart: true, other.Port));

        Assert.Contains("answers, but not to this brain's key", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"fix it with: kill {Environment.ProcessId} && brain ui", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNothingOnThePortThereIsNothingToRestart()
    {
        var code = UiCommand.Run(Env(), restart: true, FreePort());

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("nothing to restart", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A successor starts the binary beside the server it replaces, so a restart asked for from another build says which
    /// build now runs, and the command that serves the caller's instead.
    /// </summary>
    [Fact]
    public void ARestartSaysWhichBuildTheServerRunsAndHowToServeThisOne()
    {
        var files = new ServeFiles(brain.Home);
        var key = files.WriteKey();
        (HttpStatusCode, string) Restarted()
        {
            key = files.WriteKey();
            return (HttpStatusCode.Accepted, "{}");
        }

        using var main = new StandIn(request => request.Url!.AbsolutePath switch
        {
            "/api/restart" => Restarted(),
            "/api/hello" when request.Headers[ServeFiles.KeyHeader] == key => (HttpStatusCode.OK, JsonSerializer.Serialize(new Hello(ReviewServer.App, "/opt/brain/cache/publish/"), ReviewJson.Default.Hello)),
            _ => (HttpStatusCode.Unauthorized, "{}"),
        });
        var mine = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        var code = UiCommand.Run(Env(), restart: true, main.Port);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains(
            $"it runs the build in /opt/brain/cache/publish, not this one in {mine}. To serve this one: kill {Environment.ProcessId} && {Path.Combine(mine, "brain")} ui",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.Equal($"it runs this build, in {mine}", UiCommand.Serves(AppContext.BaseDirectory, main.Port));
        Assert.Equal("it does not say which build it runs", UiCommand.Serves(null, main.Port));
    }

    [Fact]
    public void TheListenerOfAPortIsTheProcessThatHoldsIt()
    {
        using var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();
        var port = ((IPEndPoint)held.LocalEndpoint).Port;

        Assert.Equal(Environment.ProcessId, ServeProcess.Listener(port));
        Assert.Null(ServeProcess.Listener(FreePort()));
    }

    [Fact]
    public void TheFixStopsTheHolderOfThePortOrOnlyStartsBrainServeWhenNoneHoldsIt()
    {
        using var held = new TcpListener(IPAddress.Loopback, 0);
        held.Start();

        Assert.Equal($"kill {Environment.ProcessId} && brain ui", UiCommand.Fix(((IPEndPoint)held.LocalEndpoint).Port));
        Assert.Equal("brain ui", UiCommand.Fix(FreePort()));
    }

    private BrainEnv Env() => brain.Env(TextReader.Null, output, TextWriter.Null);

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>An HTTP server on a free port of 127.0.0.1 that answers every request as <c>answer</c> says.</summary>
    private sealed class StandIn : IDisposable
    {
        private readonly HttpListener listener = new();

        public StandIn(Func<HttpListenerRequest, (HttpStatusCode Status, string Body)> answer)
        {
            Port = FreePort();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                    {
                        return;
                    }

                    var (status, body) = answer(context.Request);
                    context.Response.StatusCode = (int)status;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body)).ConfigureAwait(false);
                    context.Response.Close();
                }
            });
        }

        public int Port { get; }

        public void Dispose() => listener.Close();
    }
}
