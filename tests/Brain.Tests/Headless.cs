using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>A test of a window in a real browser, skipped, which a run shows, when the machine has none.</summary>
internal sealed class WindowFactAttribute : FactAttribute
{
    public WindowFactAttribute()
    {
        if (Browser.Find() is null)
        {
            Skip = "no Chromium-family browser to open the window in";
        }
    }
}

/// <summary>
/// A headless Chromium on one page, driven over the DevTools protocol: it evaluates a script and returns its text. Its
/// process and everything it started end when it is disposed. It starts on a blank page and records every uncaught
/// error before it loads the page asked for, so an error while that page's scripts load is recorded too.
/// </summary>
internal sealed class Headless : IAsyncDisposable
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    private readonly Process process;
    private readonly ClientWebSocket socket;
    private int next;

    /// <summary>The request of each file held back, as <see cref="Open"/>'s hold names, until <see cref="Release"/>.</summary>
    private readonly List<string> held = [];

    private Headless(Process process, ClientWebSocket socket)
    {
        this.process = process;
        this.socket = socket;
    }

    /// <summary>Each uncaught error the page reported, with where it was thrown.</summary>
    public List<string> Exceptions { get; } = [];

    /// <summary>
    /// Opens <paramref name="url"/> in a headless browser with its profile in <paramref name="profile"/>. With
    /// <paramref name="hold"/>, a URL pattern, each file it matches is held back until <see cref="Release"/>.
    /// </summary>
    public static async Task<Headless> Open(string url, string profile, string? hold = null)
    {
        var browser = Browser.Find() ?? throw new InvalidOperationException("no browser");
        var debugPort = FreePort();
        var start = new ProcessStartInfo(browser) { UseShellExecute = false };
        foreach (var argument in new[]
                 {
                     "--headless=new", "--no-sandbox", "--disable-gpu", "--no-first-run", "--no-default-browser-check",
                     "--remote-debugging-port=" + debugPort, "--user-data-dir=" + profile, "about:blank",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("the browser did not start");
        try
        {
            var headless = new Headless(process, await Connect(debugPort).ConfigureAwait(false));
            await headless.Call("Runtime.enable", new { }).ConfigureAwait(false);
            if (hold is not null)
            {
                await headless.Call("Fetch.enable", new { patterns = new[] { new { urlPattern = hold } } }).ConfigureAwait(false);
            }

            await headless.Call("Page.navigate", new { url }).ConfigureAwait(false);
            return headless;
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        socket.Dispose();
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync().ConfigureAwait(false);
        process.Dispose();
    }

    /// <summary>The value of <paramref name="script"/> in the page, as text.</summary>
    public async Task<string> Eval(string script)
    {
        var result = await Call("Runtime.evaluate", new { expression = script, awaitPromise = true, returnByValue = true }).ConfigureAwait(false);
        Assert.False(result.TryGetProperty("exceptionDetails", out var failed), failed.ToString());
        return result.GetProperty("result").TryGetProperty("value", out var value) ? value.ToString() : string.Empty;
    }

    /// <summary>Evaluates <paramref name="script"/>, which may throw while the page loads, until it says <paramref name="expected"/>, or fails after the wait.</summary>
    public async Task Until(string script, string expected, TimeSpan? wait = null)
    {
        var give = DateTime.UtcNow + (wait ?? Wait);
        var seen = string.Empty;
        while (DateTime.UtcNow < give)
        {
            seen = await Eval($"(() => {{ try {{ return String({script}); }} catch {{ return ''; }} }})()").ConfigureAwait(false);
            if (seen == expected)
            {
                return;
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        Assert.Equal(expected, seen);
    }

    /// <summary>Presses <paramref name="key"/> in the page, as the keys do: a keydown and a keyup on what has the focus.</summary>
    public async Task Press(string key, string code, string? text = null)
    {
        if (text is null)
        {
            await Call("Input.dispatchKeyEvent", new { type = "keyDown", key, code }).ConfigureAwait(false);
        }
        else
        {
            await Call("Input.dispatchKeyEvent", new { type = "keyDown", key, code, text }).ConfigureAwait(false);
        }

        await Call("Input.dispatchKeyEvent", new { type = "keyUp", key, code }).ConfigureAwait(false);
    }

    /// <summary>Waits until a file is held back, as <see cref="Open"/>'s hold names.</summary>
    public async Task UntilHeld()
    {
        var give = DateTime.UtcNow + Wait;
        while (held.Count == 0)
        {
            Assert.True(DateTime.UtcNow < give, "no file was held back");
            await Eval("1").ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>Lets each file held back so far load.</summary>
    public async Task Release()
    {
        foreach (var request in held.ToList())
        {
            held.Remove(request);
            await Call("Fetch.continueRequest", new { requestId = request }).ConfigureAwait(false);
        }
    }

    /// <summary>Calls one method of the protocol and returns its result, keeping each uncaught error reported meanwhile.</summary>
    public async Task<JsonElement> Call(string method, object parameters)
    {
        var id = ++next;
        var request = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await socket.SendAsync(Encoding.UTF8.GetBytes(request), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        while (true)
        {
            var reply = JsonDocument.Parse(await Receive().ConfigureAwait(false)).RootElement;
            if (reply.TryGetProperty("method", out var name) && name.GetString() == "Runtime.exceptionThrown")
            {
                Exceptions.Add(reply.GetProperty("params").GetProperty("exceptionDetails").ToString());
                continue;
            }

            if (reply.TryGetProperty("method", out var paused) && paused.GetString() == "Fetch.requestPaused")
            {
                held.Add(reply.GetProperty("params").GetProperty("requestId").GetString()!);
                continue;
            }

            if (reply.TryGetProperty("id", out var replyId) && replyId.GetInt32() == id)
            {
                return reply.GetProperty("result").Clone();
            }
        }
    }

    private static async Task<ClientWebSocket> Connect(int debugPort)
    {
        using var http = new HttpClient();
        var give = DateTime.UtcNow + Wait;
        while (true)
        {
            try
            {
                var listing = await http.GetStringAsync(new Uri($"http://127.0.0.1:{debugPort}/json")).ConfigureAwait(false);
                var targets = JsonDocument.Parse(listing).RootElement;
                var page = targets.EnumerateArray().FirstOrDefault(target => target.GetProperty("type").GetString() == "page");
                if (page.ValueKind == JsonValueKind.Object)
                {
                    var socket = new ClientWebSocket();
                    await socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), CancellationToken.None).ConfigureAwait(false);
                    return socket;
                }
            }
            catch (HttpRequestException)
            {
                // The browser is not listening yet.
            }

            Assert.True(DateTime.UtcNow < give, "the browser opened no page");
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private async Task<string> Receive()
    {
        var buffer = new byte[1 << 16];
        using var text = new MemoryStream();
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            await text.WriteAsync(buffer.AsMemory(0, part.Count)).ConfigureAwait(false);
        }
        while (!part.EndOfMessage);
        return Encoding.UTF8.GetString(text.ToArray());
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
