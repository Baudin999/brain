using System.Net;
using System.Text.Json;
using Brain.Serving;
using Brain.Workspace;

namespace Brain.Tests;

/// <summary>
/// The workspace of a session's folder in brain code: brain code's server starts the folder's own workspace server the
/// first time the window asks for it, and the sessions in that folder share it; it hands the window a launch for a
/// frame, which only brain code's origin may frame, and refuses a folder no session runs in. In a real browser, the
/// strip's tabs and its toggle are keys, the code shows in a frame of the workspace's own server, and side by side is
/// kept in the settings.
/// </summary>
public sealed partial class CodeServerTests
{
    [Fact]
    public async Task ASessionsFolderHasOneWorkspaceTheSessionsInItShareAndOnlyBrainCodeFramesIt()
    {
        var token = await Trade(Code(windows[0]));
        await StartSession();
        await StartSession();
        var elsewhere = Path.Combine(brain.Root, "elsewhere");
        Directory.CreateDirectory(elsewhere);

        var first = await Json(await Send(HttpMethod.Post, "/api/workspace", Json(new { folder = project }), token: token, origin: server.Origin));
        var second = await Json(await Send(HttpMethod.Post, "/api/workspace", Json(new { folder = project }), token: token, origin: server.Origin));
        var refused = await Send(HttpMethod.Post, "/api/workspace", Json(new { folder = elsewhere }), token: token, origin: server.Origin);
        var page = await Send(HttpMethod.Get, "/");

        var started = Assert.Single(workspaces);
        var url = first.RootElement.GetProperty("url").GetString()!;
        Assert.StartsWith(started.Server.Origin + "/#launch=", url, StringComparison.Ordinal);
        Assert.StartsWith(started.Server.Origin + "/#launch=", second.RootElement.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.NotEqual(url, second.RootElement.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("frame-src http://127.0.0.1:*", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        using var workspace = new HttpClient { BaseAddress = new Uri(started.Server.Origin) };
        using var framed = await workspace.GetAsync(new Uri("/", UriKind.Relative));
        Assert.EndsWith("frame-ancestors " + server.Origin, framed.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The window in a real browser: Alt+W shows the session's folder as code, in a frame of the workspace's server,
    /// Alt+V puts it beside the conversation and the settings keep that, and Alt+C gives the conversation back. The
    /// strip's tabs say what shows.
    /// </summary>
    [WindowFact]
    public async Task TheStripShowsTheFoldersCodeInATabAndSideBySideFromTheKeys()
    {
        var window = await Headless.Open(windows[0], Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.code-new') !== null", "true").ConfigureAwait(false);
            await Send(HttpMethod.Post, "/api/key/start", Json(new { agent = "brain", cwd = project }), key: Key()).ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.strip .row.vertical').length", "3").ConfigureAwait(false);

            await window.Eval(AltKey("w", "KeyW")).ConfigureAwait(false);
            await window.Until("document.querySelector('.code').dataset.view", "code").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-workspace iframe')?.dataset.folder", project).ConfigureAwait(false);
            var origin = Assert.Single(workspaces).Server.Origin;
            await window.Until($"document.querySelector('.code-workspace iframe').src.startsWith('{origin}/')", "true").ConfigureAwait(false);
            Assert.True(await WorkspaceListed().ConfigureAwait(false), "the workspace in the frame listed no folder");
            await window.Until("[...document.querySelectorAll('.strip .row.vertical')].map((tab) => tab.getAttribute('aria-current')).join()", "false,true,").ConfigureAwait(false);

            await window.Eval(AltKey("v", "KeyV")).ConfigureAwait(false);
            await window.Until("document.querySelector('.code').dataset.view", "beside").ConfigureAwait(false);
            await window.Until("document.querySelector('.strip [aria-pressed]').getAttribute('aria-pressed')", "true").ConfigureAwait(false);
            var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(brain.Home, "cache", "code", "settings.json")).ConfigureAwait(false));
            Assert.True(saved.RootElement.GetProperty("beside").GetBoolean());

            await window.Eval(AltKey("v", "KeyV")).ConfigureAwait(false);
            await window.Eval(AltKey("c", "KeyC")).ConfigureAwait(false);
            await window.Until("document.querySelector('.code').dataset.view", "conversation").ConfigureAwait(false);
            await window.Until("document.querySelector('.code-workspace iframe').hidden", "true").ConfigureAwait(false);

            Assert.True(window.Exceptions.Count == 0, string.Join('\n', window.Exceptions));
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether the workspace's server listed a folder for its frame, which it does only once the frame traded its launch
    /// code; it waits a while for it.
    /// </summary>
    private async Task<bool> WorkspaceListed()
    {
        var folder = Path.Combine(brain.Home, "perf", WorkspaceServer.LogDir);
        var until = DateTime.UtcNow + Headless.Wait;
        while (DateTime.UtcNow < until)
        {
            foreach (var log in Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : [])
            {
                if ((await File.ReadAllTextAsync(log).ConfigureAwait(false)).Contains("\"name\":\"list\"", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>A key with Alt as the window's keys see it, by the key's place.</summary>
    private static string AltKey(string key, string code) =>
        $"document.dispatchEvent(new KeyboardEvent('keydown', {{ key: '{key}', code: '{code}', altKey: true, bubbles: true, cancelable: true }}))";

    /// <summary>Starts the workspace server of <paramref name="root"/> in this process, as brain code starts one in its own.</summary>
    private string StartWorkspace(string root)
    {
        var started = new WorkspaceServer(brain.Home, brain.Root, root, TextWriter.Null, TimeProvider.System, new OmarchyTheme(brain.Root, colorCommand: null));
        lock (workspaces)
        {
            workspaces.Add((started, started.Serve(stop.Token)));
        }

        return Path.Combine(brain.Root, "workspace.log");
    }
}
