using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Brain.Code;
using Brain.Serving;
using Brain.Windows;

namespace Brain.Tests;

/// <summary>
/// brain code's reload onto a newer build: the server notices a build published beside it by itself and tells its
/// window; its session list says the reload waits while a session runs and is ready once none does; a reload is refused
/// before a newer build and while a session runs; and once ready, the server hands every session on, starts its
/// successor itself, moves the window that listens onto it, and stops. The successor resumes each session that was
/// alive by its id, with its agent and in its folder, in the same order, keeps one that had ended as it was shown, and
/// shows the session that was in view. It opens no second window: the window moves to its launch URL. With no window
/// listening, the successor starts once the server stopped and opens its own.
/// </summary>
public sealed partial class CodeServerTests
{
    [Fact]
    public async Task ANewerBuildReloadsOnceNoSessionRunsAndTheSuccessorResumesEverySessionByItsId()
    {
        var token = await Trade(Code(windows[0]));
        using var events = await OpenEvents(token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());
        var atlas = Path.Combine(brain.Root, "atlas");
        Directory.CreateDirectory(atlas);
        var first = await StartIn("immediate", project);
        var second = await StartIn("brain", atlas);
        var third = await StartIn("brain", project);
        await Say(token, first, "start a background job");
        var job = await StartedJob();
        await Say(token, second, "hello");
        await Say(token, third, "quitnow");
        var ended = await SessionWhen(token, third, (session) => !session.GetProperty("alive").GetBoolean());
        await SessionWhen(token, second, (session) => !session.GetProperty("working").GetBoolean());
        var none = await Reload(token, second);
        using var unpublished = await Listed(token);

        await File.WriteAllTextAsync(Path.Combine(publish, CodeBuild.StampFile), "second\n");
        var heard = await NextEvent(reader, "build");
        using var running = await ListedWhen(token, CodeReload.Running);
        var refused = await Reload(token, second);
        await EndProcess(token, first, job);
        using var ready = await ListedWhen(token, CodeReload.Ready);
        using var stopping = new CancellationTokenSource();
        CodeServer? successor = null;
        Task? served = null;
        succeeding = () =>
        {
            successor = Server();
            served = successor.Serve(stopping.Token);
        };
        var reloaded = await Reload(token, second);
        var moved = await NextEvent(reader, "moved");
        await serving.WaitAsync(Wait);

        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.False(unpublished.RootElement.TryGetProperty("reload", out _));
        Assert.Equal("published", heard);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("still runs", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, reloaded.StatusCode);
        Assert.Equal(1, successors);

        Assert.NotNull(successor);
        using var next = successor;
        var launch = next.Origin + "/#launch=";
        Assert.StartsWith(launch, moved, StringComparison.Ordinal);
        Assert.Single(windows);
        var shown = ended.GetProperty("problem").GetString();
        using var http = new HttpClient { BaseAddress = new Uri(next.Origin) };
        using var listed = await FirstList(http, next.Origin, moved[launch.Length..]);
        var resumed = listed.RootElement.GetProperty("sessions").EnumerateArray().ToList();

        Assert.Equal([first, second, third], resumed.Select((one) => one.GetProperty("id").GetString()));
        Assert.Equal(["immediate", "brain", "brain"], resumed.Select((one) => one.GetProperty("agent").GetString()));
        Assert.Equal([project, atlas, project], resumed.Select((one) => one.GetProperty("cwd").GetString()));
        Assert.Equal([true, true, false], resumed.Select((one) => one.GetProperty("alive").GetBoolean()));
        Assert.Equal(shown, resumed[2].GetProperty("problem").GetString());
        Assert.Equal(second, listed.RootElement.GetProperty("opened").GetString());
        Assert.False(File.Exists(Path.Combine(brain.Home, "cache", CodeServer.Folder, Handover.FileName)));
        Assert.False(File.Exists(Path.Combine(brain.Home, "cache", CodeServer.Folder, HandedLaunch.FileName)));
        var runs = Runs(5);
        Assert.Contains(runs, (run) => run.StartsWith(project + " ", StringComparison.Ordinal) && run.Contains("--agent immediate", StringComparison.Ordinal) && run.EndsWith("--resume " + first, StringComparison.Ordinal));
        Assert.Contains(runs, (run) => run.StartsWith(atlas + " ", StringComparison.Ordinal) && run.Contains("--agent brain", StringComparison.Ordinal) && run.EndsWith("--resume " + second, StringComparison.Ordinal));
        Assert.DoesNotContain(runs, (run) => run.EndsWith("--resume " + third, StringComparison.Ordinal));
        await stopping.CancelAsync();
        await served!.WaitAsync(Wait);
    }

    [Fact]
    public async Task AReloadWithNoWindowListeningStartsTheSuccessorOnceStoppedAndItOpensItsOwnWindow()
    {
        var token = await Trade(Code(windows[0]));
        await File.WriteAllTextAsync(Path.Combine(publish, CodeBuild.StampFile), "second\n");
        using var ready = await ListedWhen(token, CodeReload.Ready);

        var reloaded = await Reload(token, string.Empty);
        await serving.WaitAsync(Wait);
        using var successor = Server();
        using var stopping = new CancellationTokenSource();
        var served = successor.Serve(stopping.Token);

        Assert.Equal(HttpStatusCode.NoContent, reloaded.StatusCode);
        Assert.Equal(1, successors);
        Assert.False(File.Exists(Path.Combine(brain.Home, "cache", CodeServer.Folder, HandedLaunch.FileName)));
        Assert.Equal(2, windows.Count);
        await stopping.CancelAsync();
        await served.WaitAsync(Wait);
    }

    /// <summary>
    /// The window in a real browser: no reload button shows until a newer build is published; then it shows, disabled
    /// while a session runs a job, and it lights up once the job ended, with no event from the server; its click reloads:
    /// the server starts its successor, and the same window moves onto it with no second window and no question asked.
    /// </summary>
    [WindowFact]
    public async Task TheReloadButtonShowsForANewerBuildAndLightsOnceNoSessionRuns()
    {
        const string Button = "document.querySelector('.code-side .pane-head .button.highlight')";
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await StartSession().ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);
            await window.Eval("const box = document.querySelector('.code-words'); box.value = 'start a background job'; box.dispatchEvent(new Event('input')); box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))").ConfigureAwait(false);
            var job = await StartedJob().ConfigureAwait(false);
            await window.Until("document.querySelector('.process-fold .chip')?.textContent", "1 running").ConfigureAwait(false);
            var before = await window.Eval($"String({Button} === null)").ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(publish, CodeBuild.StampFile), "second\n").ConfigureAwait(false);
            await window.Until($"String({Button}?.disabled)", "true").ConfigureAwait(false);
            using (var running = Process.GetProcessById(job))
            {
                running.Kill(entireProcessTree: true);
            }

            await window.Until($"String({Button}?.disabled)", "false").ConfigureAwait(false);
            using var stopping = new CancellationTokenSource();
            CodeServer? successor = null;
            Task? served = null;
            succeeding = () =>
            {
                successor = Server();
                served = successor.Serve(stopping.Token);
            };
            await window.Eval($"{Button}.click()").ConfigureAwait(false);
            await serving.WaitAsync(Wait).ConfigureAwait(false);
            Assert.NotNull(successor);
            using var next = successor;
            var port = new Uri(next.Origin).Port.ToString(CultureInfo.InvariantCulture);
            await window.Until("location.port", port).ConfigureAwait(false);
            await window.Until("document.querySelector('.code-session')?.getAttribute('aria-current')", "true").ConfigureAwait(false);

            Assert.Equal("true", before);
            Assert.Equal(1, successors);
            Assert.Single(windows);
            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
            await stopping.CancelAsync().ConfigureAwait(false);
            await served!.WaitAsync(Wait).ConfigureAwait(false);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Starts a session of <paramref name="agent"/> in <paramref name="cwd"/> with the key, as brain code does, and gives its id.</summary>
    private async Task<string> StartIn(string agent, string cwd)
    {
        using var started = await Json(await Send(HttpMethod.Post, "/api/key/start", Json(new { agent, cwd }), key: Key()).ConfigureAwait(false)).ConfigureAwait(false);
        return started.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Each start of the stand-in, its folder and its arguments, once there are <paramref name="count"/>; it fails after a wait.</summary>
    private string[] Runs(int count)
    {
        var path = Path.Combine(brain.Root, "claude.runs");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path) && File.ReadAllLines(path).Length >= count, Wait), "the stand-in for claude did not start " + count + " times");
        return File.ReadAllLines(path);
    }

    private Task<HttpResponseMessage> Reload(string token, string open) =>
        Send(HttpMethod.Post, "/api/reload", Json(new { session = open }), token: token, origin: server.Origin);

    private async Task<JsonDocument> Listed(string token) =>
        await Json(await Send(HttpMethod.Get, "/api/sessions", token: token).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>The session list once it says the reload waits as <paramref name="reload"/>; it fails after a wait.</summary>
    private async Task<JsonDocument> ListedWhen(string token, string reload)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            var listed = await Listed(token).ConfigureAwait(false);
            if (listed.RootElement.TryGetProperty("reload", out var waits) && waits.GetString() == reload)
            {
                return listed;
            }

            listed.Dispose();
            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new InvalidOperationException("the session list never said the reload is " + reload);
    }

    /// <summary>
    /// The session list of the successor at <paramref name="origin"/> as its window first reads it, once the window traded
    /// <paramref name="code"/>, the launch code it moved there with.
    /// </summary>
    private static async Task<JsonDocument> FirstList(HttpClient http, string origin, string code)
    {
        using var launch = new HttpRequestMessage(HttpMethod.Post, "/api/launch");
        launch.Content = new StringContent(Json(new { code }), Encoding.UTF8, "application/json");
        launch.Headers.Add("Origin", origin);
        using var traded = await Json(await http.SendAsync(launch).ConfigureAwait(false)).ConfigureAwait(false);
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/sessions");
        read.Headers.Add(WindowServer.SessionHeader, traded.RootElement.GetProperty("token").GetString());
        return await Json(await http.SendAsync(read).ConfigureAwait(false)).ConfigureAwait(false);
    }
}
