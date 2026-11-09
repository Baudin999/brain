using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Brain.Serving;
using static Brain.Serving.WebCalls;

namespace Brain.Windows;

/// <summary>
/// What one kind of small window is: its app's name, the command that opens it, the folder it shows, its name and title,
/// and its page's files.
/// </summary>
/// <param name="Folder">The folder of the data repo's cache that holds its key, its port and its launch files.</param>
/// <param name="Assets">Its own page's files, by the path each is served at, the shared ones of <see cref="WebAssets"/> with them.</param>
internal sealed record WindowKind(string App, string Command, string Folder, string Root, string Name, string Title, Dictionary<string, (string Resource, string Type)> Assets);

/// <summary>
/// The server of one small window, such as a workspace's or brain code's, on a free port of 127.0.0.1. It is not brain
/// serve. It serves its window's page with the shared stylebook and scripts, and answers the plumbing every such window
/// shares; the rest of <c>/api</c> it hands to the window's own <c>api</c>. Every request must name its host exactly,
/// and a POST from a browser must come from its origin. Hello, raise and the window's own keyed calls need the key in its
/// <see cref="ServeFiles"/>, which only the user's account can read; launch needs a launch code; everything else under
/// <c>/api</c> needs the session of its window from <see cref="WindowSessions"/>. Its window hears on its event stream
/// what the server publishes and each Omarchy theme switch, and moves onto the server's successor once it hands on. It sends the window's records and its own to its
/// <see cref="Records"/>, and once a minute records the CPU it used, which an idle window keeps near none. It ends once
/// its window is gone.
/// </summary>
internal sealed class WindowServer
{
    public const string SessionHeader = "X-Window-Session";
    public const string PortFile = "port";
    public const string Get = "GET";
    public const string Post = "POST";

    /// <summary>The largest batch of records the window sends: ten seconds of busy keys.</summary>
    private const int MaxRecords = 256 * 1024;
    private const int PortTries = 5;

    /// <summary>The CPU a window's server may use in a minute with no call from its window: 1 percent.</summary>
    private const double IdleBudgetMs = 600;

    /// <summary>The event stream says it is alive this often, so the stream of a window that went away ends.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    /// <summary>How long the server waits, once its last window went, for one to come back, as after a reload.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(3);

    /// <summary>How long the server waits for its first window.</summary>
    private static readonly TimeSpan FirstWindow = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>How long a server that moves its window waits for its successor to serve, and how often it looks.</summary>
    private static readonly TimeSpan SuccessorWait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SuccessorPoll = TimeSpan.FromMilliseconds(50);

    /// <summary>How long a server that moved its window waits for the window's streams to carry the word.</summary>
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(2);

    private readonly string home;
    private readonly WindowKind kind;
    private readonly TextWriter err;
    private readonly Func<string, bool> openWindow;
    private readonly Func<string, bool> raise;
    private readonly TimeProvider time;
    private readonly OmarchyTheme theme;
    private readonly ServeFiles files;
    private readonly Records records;
    private readonly WindowSessions windows;
    private readonly Action<HttpListenerRequest, HttpListenerResponse, string> keyed;
    private readonly Action<HttpListenerRequest, HttpListenerResponse, string> api;
    private CancellationTokenSource? serving;
    private ITimer? idle;
    private string key = string.Empty;
    private TimeSpan cpuSeen;
    private int calls;

    /// <param name="keyed">Answers a call under <c>/api/key/</c>, by its path, once its key was checked: one the window's command makes.</param>
    /// <param name="api">Answers a call of the window's session, by its path, which no plumbing took.</param>
    public WindowServer(string home, WindowKind kind, TextWriter err, Func<string, bool> openWindow, Func<string, bool> raise, TimeProvider time, OmarchyTheme theme, Records records, Action<HttpListenerRequest, HttpListenerResponse, string> keyed, Action<HttpListenerRequest, HttpListenerResponse, string> api)
    {
        this.home = home;
        this.kind = kind;
        this.err = err;
        this.openWindow = openWindow;
        this.raise = raise;
        this.time = time;
        this.theme = theme;
        this.records = records;
        this.keyed = keyed;
        this.api = api;
        files = new ServeFiles(home, kind.Folder);
        windows = new WindowSessions(time);
    }

    public string Origin { get; private set; } = string.Empty;

    /// <summary>
    /// Opens its window and listens until <paramref name="stop"/>, or until the window is gone. A launch code its
    /// predecessor handed it takes the place of a new window: the window already open moves onto this server. The code is
    /// admitted before the port file names this server, so a predecessor that waits on that file tells its window only
    /// once the code trades.
    /// </summary>
    // navarone: 0000174
    public async Task Serve(CancellationToken stop)
    {
        using var listener = Listen();
        key = files.WriteKey();
        var handed = HandedLaunch.Take(files, time);
        if (handed is not null)
        {
            windows.Admit(handed);
        }

        files.Write(PortFile, Origin["http://127.0.0.1:".Length..]);
        using var watching = theme.Watch(time);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop);
        serving = stopping;
        var themes = Task.Run(() => FollowTheme(stopping.Token), CancellationToken.None);
        await err.WriteLineAsync($"{kind.Command}: serving {kind.Root} on {Origin}").ConfigureAwait(false);
        cpuSeen = CpuUsed();
        using var cpu = time.CreateTimer(_ => RecordCpu(), null, Minute, Minute);
        using var gone = time.CreateTimer(_ => Stop(), null, FirstWindow, Timeout.InfiniteTimeSpan);
        idle = gone;
        if (handed is null)
        {
            OpenWindow();
        }

        using var closing = stopping.Token.Register(() =>
        {
            files.RemoveLaunches();
            theme.Wake();
            listener.Close();
        });
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => Handle(context), CancellationToken.None);
        }

        idle = null;
        serving = null;
        await themes.ConfigureAwait(false);
    }

    /// <summary>Says <paramref name="text"/>, one whole event, on the stream of every window.</summary>
    public void Publish(string text) => windows.Publish(text);

    /// <summary>
    /// Moves the window onto <paramref name="successor"/>: while a window listens, it hands the successor a launch code,
    /// starts it, waits until the successor's port file names it, and tells the window the successor's launch URL on the
    /// window's own event stream, waiting a moment for the stream to carry it, so the window goes on there and no second
    /// window opens. False, with nothing started, when no window listens: the successor then opens its own. A successor
    /// that does not serve within <see cref="SuccessorWait"/> leaves the window to find this server gone.
    /// </summary>
    public bool MoveTo(Action successor)
    {
        if (!windows.Listened)
        {
            return false;
        }

        var code = HandedLaunch.Hand(files, time);
        var mine = files.Read(PortFile);
        successor();
        var deadline = DateTime.UtcNow + SuccessorWait;
        var theirs = files.Read(PortFile);
        while (theirs is null || theirs == mine)
        {
            if (DateTime.UtcNow > deadline)
            {
                return true;
            }

            Thread.Sleep(SuccessorPoll);
            theirs = files.Read(PortFile);
        }

        // The streams carry moved to the window before the server stops, since closing its listener cuts them off.
        windows.Move("http://127.0.0.1:" + theirs + "/#launch=" + code);
        SpinWait.SpinUntil(() => !windows.Listened, Drain);
        return true;
    }

    /// <summary>Brings the window forward: the compositor raises it, or a fresh window takes its place.</summary>
    public string Raise()
    {
        if (raise(kind.Title))
        {
            return "raised";
        }

        return OpenWindow() ? "opened" : "no-browser";
    }

    /// <summary>Answers one request in the boundary <see cref="WebCalls.Answer"/> gives every server.</summary>
    public void Handle(HttpListenerContext context) => Answer(context, home, kind.Command, err, Route);

    /// <summary>Stops serving, as once the window is gone.</summary>
    private void Stop()
    {
        try
        {
            serving?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The server stopped just now on its own.
        }
    }

    /// <summary>A listener on a free port of 127.0.0.1, and the origin it answers as.</summary>
    private HttpListener Listen()
    {
        for (var tries = 1; ; tries++)
        {
            var port = FreePort();
            var listener = new HttpListener();
            var origin = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            listener.Prefixes.Add(origin + "/");
            try
            {
                listener.Start();
                Origin = origin;
                return listener;
            }
            catch (HttpListenerException) when (tries < PortTries)
            {
                // Another process took the port between the probe and the start; try another.
                listener.Close();
            }
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>Opens a window with a fresh launch code, through a launch file, so no command line shows the code.</summary>
    private bool OpenWindow() => openWindow(new Uri(files.WriteLaunch(Origin + "/#launch=" + windows.Issue())).AbsoluteUri);

    /// <summary>Tells the window of every Omarchy theme switch, until the server stops.</summary>
    private void FollowTheme(CancellationToken stop)
    {
        var seen = theme.Version;
        while (!stop.IsCancellationRequested)
        {
            if (theme.WaitPast(seen, Heartbeat))
            {
                seen = theme.Version;
                windows.Publish("event: theme\ndata: switched\n\n");
            }
        }
    }

    /// <summary>The CPU the server used in the last minute; over its budget only when the window made no call in it.</summary>
    private void RecordCpu()
    {
        var used = CpuUsed();
        var ms = (used - cpuSeen).TotalMilliseconds;
        cpuSeen = used;
        var made = Interlocked.Exchange(ref calls, 0);
        double? budget = made == 0 ? IdleBudgetMs : null;
        records.Add("cpu", $"{made} calls in a minute", ms, WindowRecord.Ok, budget);
    }

    private static TimeSpan CpuUsed()
    {
        using var self = Process.GetCurrentProcess();
        return self.TotalProcessorTime;
    }

    /// <summary>
    /// Routes a request by who makes it: the window's command with the key, the browser for the page and its files, the
    /// window for its launch, and the window's session for everything else under <c>/api</c>.
    /// </summary>
    // navarone: 0000156
    private void Route(HttpListenerRequest request, HttpListenerResponse response)
    {
        var path = request.Url?.AbsolutePath ?? string.Empty;
        if (RefusedStranger(request, response, Origin))
        {
            return;
        }

        if (path is "/api/hello" or "/api/raise" || path.StartsWith("/api/key/", StringComparison.Ordinal))
        {
            Keyed(request, response, path);
            return;
        }

        if (request.HttpMethod == Get && Page(response, path))
        {
            return;
        }

        if ((request.HttpMethod, path) == (Post, "/api/launch") && request.Headers["Origin"] is not null)
        {
            Launch(request, response);
            return;
        }

        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            Refuse(response, HttpStatusCode.NotFound, "no such page");
            return;
        }

        var token = request.Headers[SessionHeader];
        if (!windows.Knows(token))
        {
            Refuse(response, HttpStatusCode.Unauthorized, $"this window's session ended; run {kind.Command} again");
            return;
        }

        Interlocked.Increment(ref calls);
        Session(request, response, token, path);
    }

    /// <summary>A call of the window's command, which needs the key: hello, raise, or one of the window's own under <c>/api/key/</c>.</summary>
    private void Keyed(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if (!HasKey(request, key))
        {
            Refuse(response, HttpStatusCode.Forbidden, $"this call needs the key {kind.Command} keeps in the data repo's cache");
            return;
        }

        switch (request.HttpMethod, path)
        {
            case (Get, "/api/hello"):
                Send(response, new WindowHello(kind.App, kind.Root), WindowJson.Default.WindowHello);
                return;
            case (Post, "/api/raise"):
                Send(response, new WindowRaised(Raise()), WindowJson.Default.WindowRaised);
                return;
            default:
                keyed(request, response, path);
                return;
        }
    }

    /// <summary>Sends the page's file at <paramref name="path"/>, its theme sheet among them; false when it is none.</summary>
    // navarone: 0000177
    private bool Page(HttpListenerResponse response, string path)
    {
        if (kind.Assets.TryGetValue(path, out var asset))
        {
            WebAssets.Send(response, asset.Resource, asset.Type);
            return true;
        }

        if (path != "/theme.css")
        {
            return false;
        }

        SendText(response, theme.Css, WebAssets.Sheet);
        return true;
    }

    /// <summary>A call of the window's session: the plumbing every window has, then the window's own <c>api</c>.</summary>
    // navarone: 0000172
    private void Session(HttpListenerRequest request, HttpListenerResponse response, string token, string path)
    {
        var writes = request.Headers["Origin"] is not null;
        switch (request.HttpMethod, path)
        {
            case (Get, "/api/records"):
                Send(response, records.Since(Seen(request.QueryString["after"])), WindowJson.Default.ServerRecords);
                return;
            case (Get, "/api/events"):
                Events(response, token);
                return;
            case (Post, "/api/records") when writes:
                Keep(request, response);
                return;
            case (Post, "/api/bye") when writes:
                windows.Goodbye(token);
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            default:
                api(request, response, path);
                return;
        }
    }

    private void Launch(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, WindowJson.Default.WindowLaunch) is not { Code: { } code } || windows.Exchange(code) is not { } token)
        {
            Refuse(response, HttpStatusCode.Forbidden, $"this window's key is no longer valid; run {kind.Command} again");
            return;
        }

        files.RemoveLaunches();
        Send(response, new WindowSession(token, kind.Root, kind.Name, kind.Title), WindowJson.Default.WindowSession);
    }

    /// <summary>
    /// The window's event stream: what the server publishes, a theme switch, close once a newer window took over, and
    /// moved once the window goes on in the server's successor. It
    /// ends with the window's goodbye or its connection. Once the last stream ended and none came back within a few
    /// seconds, the server stops.
    /// </summary>
    // navarone: 0000157
    private void Events(HttpListenerResponse response, string token)
    {
        const string Alive = ": alive\n\n";
        var stream = windows.Listen(token);
        idle?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            response.ContentType = "text/event-stream; charset=utf-8";
            response.SendChunked = true;
            Say(response, Alive);
            while (true)
            {
                if (!stream.TryTake(out var said, Heartbeat))
                {
                    said = Alive;
                }

                if (said == WindowSessions.End)
                {
                    break;
                }

                Say(response, said);
            }
        }
        catch (Exception gone) when (gone is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The window went away; the stream ends with it.
        }
        finally
        {
            if (windows.Leave(token, stream) == 0)
            {
                idle?.Change(Grace, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Keep(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (Read(request, WindowJson.Default.RecordBatch, MaxRecords) is not { Records: { } kept })
        {
            Refuse(response, HttpStatusCode.BadRequest, "a batch of records names its records");
            return;
        }

        records.AddWindows(kept);
        response.StatusCode = (int)HttpStatusCode.NoContent;
    }

    private static long Seen(string? after) =>
        long.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out var seen) ? seen : 0;
}
