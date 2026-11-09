using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Brain.Serving;
using Brain.Windows;
using Brain.Workspace;

namespace Brain.Tests;

/// <summary>
/// A workspace's server, over real HTTP on 127.0.0.1, on a project folder that is a git checkout: it opens no window,
/// and hands a launch for a frame to the holder of its key; its API answers only its frame's session, which a launch
/// code buys once; it serves its folder and nothing outside it, without what git
/// ignores; a change on disk reaches the window's event stream; every read, listing and git call is a record in the
/// log, with the window's own; a new frame closes the one before; and the server ends once its frame is gone.
/// </summary>
[Collection(ReviewServerTests.StartsClaude)]
public sealed partial class WorkspaceServerTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>The origin of brain code's window, which frames the workspace.</summary>
    private const string Frame = "http://127.0.0.1:4711";

    private readonly TempBrain brain = new();
    private readonly CancellationTokenSource stop = new();
    private readonly string project;
    private readonly WorkspaceServer server;
    private readonly Task serving;
    private readonly HttpClient client;

    public WorkspaceServerTests()
    {
        project = Path.Combine(brain.Root, "lens");
        Directory.CreateDirectory(Path.Combine(project, "src"));
        Directory.CreateDirectory(Path.Combine(project, "bin"));
        File.WriteAllText(Path.Combine(project, ".gitignore"), "bin/\n");
        File.WriteAllText(Path.Combine(project, "README.md"), "# Lens\n\nRead [the code](src/Fold.cs).\n");
        File.WriteAllText(Path.Combine(project, "src", "Fold.cs"), "class Fold {}\n");
        File.WriteAllText(Path.Combine(project, "bin", "lens.dll"), "built");
        File.WriteAllBytes(Path.Combine(project, "src", "icon.png"), [137, 80, 78, 71, 0, 1, 2]);
        TempBrain.Git(project, "init", "-q");
        server = new WorkspaceServer(brain.Home, brain.Root, project, TextWriter.Null, TimeProvider.System, new OmarchyTheme(brain.Root, colorCommand: null));
        serving = server.Serve(stop.Token);
        client = new HttpClient { BaseAddress = new Uri(server.Origin) };
    }

    public void Dispose()
    {
        stop.Cancel();
        serving.Wait(Wait);
        stop.Dispose();
        client.Dispose();
        brain.Dispose();
    }

    [Fact]
    public async Task WithoutTheWindowsSessionTheApiAnswersNothing()
    {
        var code = Code(await Launch());
        var token = await Trade(code);
        var again = await Send(HttpMethod.Post, "/api/launch", Json(new { code }), origin: server.Origin);

        var refused = await Send(HttpMethod.Get, "/api/tree?path=");
        var answered = await Send(HttpMethod.Get, "/api/tree?path=", token: token);
        var stranger = await Send(HttpMethod.Get, "/api/tree?path=", token: token, host: "evil.example:" + new Uri(server.Origin).Port.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.True(stranger.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound, stranger.StatusCode.ToString());
        Assert.DoesNotContain("entries", await stranger.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Hello and a launch need the key; a launch names the origin on 127.0.0.1 that frames the page, which from then on
    /// only that origin may frame, and the server opens no window of its own.
    /// </summary>
    [Fact]
    public async Task HelloAndALaunchNeedTheKeyAndTheLaunchNamesWhoFramesThePage()
    {
        var key = Key();
        var before = await Send(HttpMethod.Get, "/");

        var without = await Send(HttpMethod.Get, "/api/hello");
        var hello = await Send(HttpMethod.Get, "/api/hello", key: key);
        var stranger = await Send(HttpMethod.Post, "/api/key/launch", Json(new { frame = Frame }));
        var elsewhere = await Send(HttpMethod.Post, "/api/key/launch", Json(new { frame = "http://evil.example:4000" }), key: key);
        var injected = await Send(HttpMethod.Post, "/api/key/launch", Json(new { frame = Frame + "; script-src *" }), key: key);
        var url = await Launch();
        var after = await Send(HttpMethod.Get, "/");

        Assert.Equal(HttpStatusCode.Forbidden, without.StatusCode);
        using var json = JsonDocument.Parse(await hello.Content.ReadAsStringAsync());
        Assert.Equal(WorkspaceServer.App, json.RootElement.GetProperty("app").GetString());
        Assert.Equal(project, json.RootElement.GetProperty("root").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        Assert.StartsWith(server.Origin + "/#launch=", url, StringComparison.Ordinal);
        Assert.EndsWith("frame-ancestors 'none'", before.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.EndsWith("frame-ancestors " + Frame, after.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(new ServeFiles(brain.Home, WorkspaceServer.Folder(WorkspaceServer.Id(project))).Dir, "launch-*"));
    }

    [Fact]
    public async Task TheTreeListsFoldersFirstWithoutGitOrWhatGitIgnores()
    {
        var token = await Trade(Code(await Launch()));

        var root = await Json(await Send(HttpMethod.Get, "/api/tree?path=", token: token));
        var src = await Json(await Send(HttpMethod.Get, "/api/tree?path=src", token: token));

        Assert.Equal(["src", ".gitignore", "README.md"], Names(root));
        Assert.Equal(["src/Fold.cs", "src/icon.png"], src.RootElement.GetProperty("entries").EnumerateArray().Select((entry) => entry.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task AFileReadsAsTextOrSaysWhyNot()
    {
        var token = await Trade(Code(await Launch()));

        var text = await Json(await Send(HttpMethod.Get, "/api/file?path=src/Fold.cs", token: token));
        var binary = await Json(await Send(HttpMethod.Get, "/api/file?path=src/icon.png", token: token));
        var gone = await Json(await Send(HttpMethod.Get, "/api/file?path=src/Gone.cs", token: token));

        Assert.Equal("class Fold {}\n", text.RootElement.GetProperty("text").GetString());
        Assert.Equal(ProjectFiles.Binary, binary.RootElement.GetProperty("problem").GetString());
        Assert.False(binary.RootElement.TryGetProperty("text", out _));
        Assert.Equal(ProjectFiles.Gone, gone.RootElement.GetProperty("problem").GetString());
    }

    [Theory]
    [InlineData("/api/file?path=../secret.txt")]
    [InlineData("/api/file?path=/etc/hostname")]
    [InlineData("/api/tree?path=..")]
    [InlineData("/api/tree?path=src/../..")]
    public async Task APathOutsideTheRootIsRefused(string call)
    {
        await File.WriteAllTextAsync(Path.Combine(brain.Root, "secret.txt"), "not the workspace's");
        var token = await Trade(Code(await Launch()));

        var refused = await Send(HttpMethod.Get, call, token: token);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.DoesNotContain("not the workspace's", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>A git that fails is a failure, never a folder that is not there: it answers 500 and is recorded as failed.</summary>
    [Fact]
    public async Task AListingWhoseGitFailsAnswersAFailureNotARefusal()
    {
        var token = await Trade(Code(await Launch()));
        await File.AppendAllTextAsync(Path.Combine(project, ".git", "config"), "[core\n");

        var failed = await Send(HttpMethod.Get, "/api/tree?path=src", token: token);
        var records = await Json(await Send(HttpMethod.Get, "/api/records?after=0", token: token));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var outcomes = records.RootElement.GetProperty("records").EnumerateArray()
            .Where((record) => record.GetProperty("name").GetString() is "git" or "list")
            .Select((record) => record.GetProperty("outcome").GetString());
        Assert.Contains(WindowRecord.Failed, outcomes);
    }

    [Fact]
    public async Task TheBarFindsEveryFileGitWouldAdd()
    {
        var token = await Trade(Code(await Launch()));

        var files = await Json(await Send(HttpMethod.Get, "/api/files", token: token));

        var paths = files.RootElement.GetProperty("paths").EnumerateArray().Select((path) => path.GetString()).Order(StringComparer.Ordinal);
        Assert.Equal([".gitignore", "README.md", "src/Fold.cs", "src/icon.png"], paths);
    }

    [Fact]
    public async Task AChangeOnDiskReachesTheWindowsEventStream()
    {
        var token = await Trade(Code(await Launch()));
        using var events = await OpenEvents(token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());

        await File.WriteAllTextAsync(Path.Combine(project, "src", "Lens.cs"), "class Lens {}\n");
        var changed = await NextEvent(reader, "changed");

        Assert.Contains("src/Lens.cs", changed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryReadListingAndGitCallIsARecordInTheLog()
    {
        var token = await Trade(Code(await Launch()));
        await Send(HttpMethod.Get, "/api/tree?path=", token: token);
        await Send(HttpMethod.Get, "/api/file?path=README.md", token: token);
        var batch = Json(new { records = new[] { new { at = 1, name = "file.open", args = "{\"path\":\"README.md\"}", source = "key", ms = 12.5, outcome = "ok", budget = 100, over = false } } });

        var kept = await Send(HttpMethod.Post, "/api/records", batch, token: token, origin: server.Origin);
        var records = await Json(await Send(HttpMethod.Get, "/api/records?after=0", token: token));

        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);
        var names = records.RootElement.GetProperty("records").EnumerateArray().Select((record) => record.GetProperty("name").GetString()).ToList();
        Assert.Contains("list", names);
        Assert.Contains("read", names);
        Assert.Contains("git", names);
        Assert.All(records.RootElement.GetProperty("records").EnumerateArray(), (record) => Assert.Equal("server", record.GetProperty("source").GetString()));
        var folder = Path.Combine(brain.Home, "perf", WorkspaceServer.LogDir);
        var log = string.Concat(Directory.EnumerateFiles(folder, "*.jsonl").Select(File.ReadAllText));
        Assert.Contains("\"name\":\"file.open\"", log, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"read\"", log, StringComparison.Ordinal);
        Assert.Equal("*\n", await File.ReadAllTextAsync(Path.Combine(folder, ".gitignore")));
    }

    [Fact]
    public async Task ANewFrameClosesTheOneBefore()
    {
        var old = await Trade(Code(await Launch()));
        using var events = await OpenEvents(old);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());

        var fresh = await Trade(Code(await Launch()));
        var closed = await NextEvent(reader, "close");

        Assert.Equal("close", closed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, "/api/tree?path=", token: old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/tree?path=", token: fresh)).StatusCode);
    }

    [Fact]
    public async Task TheServerEndsOnceItsWindowIsGone()
    {
        var token = await Trade(Code(await Launch()));
        using (var events = await OpenEvents(token))
        {
            await Send(HttpMethod.Post, "/api/bye", token: token, origin: server.Origin);
        }

        var ended = await Task.WhenAny(serving, Task.Delay(Wait));

        Assert.Same(serving, ended);
    }

    [Fact]
    public async Task ThePageAndTheStylebookComeFromBrainItself()
    {
        var page = await Send(HttpMethod.Get, "/");
        var tokens = await Send(HttpMethod.Get, "/tokens.css");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Contains("script-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Contains("<script src=\"/core.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/components.css\">", html, StringComparison.Ordinal);
        Assert.Equal(WindowResources.Read(WindowResources.Tokens), await tokens.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("lens · ~/Projects/lens", @"lens · ~/Projects/lens")]
    [InlineData("a.b (1) [x]", @"a\.b \(1\) \[x\]")]
    public void ATitleIsMatchedExactly(string title, string expected)
    {
        Assert.Equal(expected, Compositor.Escape(title));
    }

    [Fact]
    public void TheWorkspaceRefusesAFolderThatIsNotThere()
    {
        var (code, _, err) = brain.Run("workspace", "--serve", Path.Combine(brain.Root, "no-such"));

        Assert.Equal(1, code);
        Assert.Contains("is no folder", err, StringComparison.Ordinal);
    }

    /// <summary>The workspace has no window of its own to open: brain code shows it.</summary>
    [Fact]
    public void TheWorkspaceOpensNoWindowOfItsOwn()
    {
        var (code, _, err) = brain.Run("workspace", project);

        Assert.Equal(1, code);
        Assert.Contains("brain code shows a session's folder as its workspace", err, StringComparison.Ordinal);
    }

    /// <summary>The workspace's page, in a real browser: the tree shows, Enter opens a file, and its link opens the next.</summary>
    [WindowFact]
    public async Task TheWindowShowsTheTreeAndFollowsALinkFromTheKeys()
    {
        var window = await Headless.Open(await Launch().ConfigureAwait(false), Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelectorAll('.row.tree').length", "3").ConfigureAwait(false);
            await window.Until("unkeyed().length", "0").ConfigureAwait(false);

            await window.Press("j", "KeyJ").ConfigureAwait(false);
            await window.Press("j", "KeyJ").ConfigureAwait(false);
            await window.Until("workspace.tree.cursor", "README.md").ConfigureAwait(false);
            await window.Press("Enter", "Enter").ConfigureAwait(false);
            await window.Until("workspace.open?.path", "README.md").ConfigureAwait(false);
            await window.Eval("document.querySelector('#reader a[data-path]').click()").ConfigureAwait(false);
            await window.Until("workspace.open?.path", "src/Fold.cs").ConfigureAwait(false);
            await window.Until("document.querySelector('#reader .ws-text').textContent", "class Fold {}\n").ConfigureAwait(false);
            var sources = await window.Eval("workspace.records.filter((kept) => kept.name === 'file.open').map((kept) => kept.source).join()").ConfigureAwait(false);
            Assert.Equal("key,link", sources);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A diagram in a markdown file is drawn in the reader, a click on it opens the image viewer as a command that takes
    /// the keys, a wheel and a drag zoom and pan it as commands, Alt+R resets it and Esc closes it and gives the keys back.
    /// </summary>
    [WindowFact]
    public async Task TheWindowOpensADiagramInTheViewerThatTakesTheKeys()
    {
        await File.WriteAllTextAsync(Path.Combine(project, "diagram.md"), "# Flow\n\n```mermaid\nflowchart LR\n  a --> b\n```\n").ConfigureAwait(false);
        var window = await Headless.Open(await Launch().ConfigureAwait(false), Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelectorAll('.row.tree').length > 0", "true").ConfigureAwait(false);
            await window.Eval("execute('file.open', { path: 'diagram.md' })").ConfigureAwait(false);
            await window.Until("String(document.querySelectorAll('#reader figure.diagram svg').length)", "1").ConfigureAwait(false);

            await window.Eval("document.querySelector('#reader figure.diagram svg').dispatchEvent(new MouseEvent('click', { bubbles: true }))").ConfigureAwait(false);
            await window.Until("workspace.focus + ' ' + document.querySelectorAll('.image-viewer svg').length", "viewer 1").ConfigureAwait(false);
            await window.Eval("document.querySelector('.image-viewer').dispatchEvent(new WheelEvent('wheel', { deltaY: -400, clientX: 10, clientY: 10, bubbles: true, cancelable: true }))").ConfigureAwait(false);
            await window.Until("String(workspace.viewer.open.scale > 1)", "true").ConfigureAwait(false);
            await window.Eval("window.zoomedY = workspace.viewer.open.y").ConfigureAwait(false);
            await window.Eval(Drag("40", "20")).ConfigureAwait(false);
            await window.Until("String(Math.round(workspace.viewer.open.y - window.zoomedY))", "20").ConfigureAwait(false);
            await window.Until("String(document.querySelector('.image-viewer-stage').style.transform.includes('translate(' + Math.round(workspace.viewer.open.x)))", "true").ConfigureAwait(false);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'r', code: 'KeyR', altKey: true }))").ConfigureAwait(false);
            await window.Until("workspace.viewer.open.scale + ' ' + workspace.viewer.open.x + ' ' + workspace.viewer.open.y", "1 0 0").ConfigureAwait(false);
            await window.Press("Escape", "Escape").ConfigureAwait(false);
            await window.Until("workspace.focus + ' ' + document.querySelectorAll('.image-viewer').length", "tree 0").ConfigureAwait(false);

            await window.Until("String(workspace.records.some((kept) => kept.name === 'viewer.close'))", "true").ConfigureAwait(false);
            var names = await window.Eval("workspace.records.map((kept) => kept.name).filter((name) => name.startsWith('viewer.')).join()").ConfigureAwait(false);
            Assert.Equal("viewer.open,viewer.zoom,viewer.pan,viewer.reset,viewer.close", names);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A drag on the viewer: the pointer goes down at a point, moves by a distance and goes up.</summary>
    private static string Drag(string dx, string dy) =>
        """
        (() => {
          const frame = document.querySelector('.image-viewer');
          const at = (type, x, y) => frame.dispatchEvent(new PointerEvent(type, { button: 0, pointerId: 7, clientX: x, clientY: y, bubbles: true }));
          at('pointerdown', 100, 100);
          at('pointermove', 100 + DX, 100 + DY);
          at('pointerup', 100 + DX, 100 + DY);
        })()
        """.Replace("DX", dx, StringComparison.Ordinal).Replace("DY", dy, StringComparison.Ordinal);

    /// <summary>A stage moves git's index, which reaches the window's event stream as git, not as a file of the workspace.</summary>
    [Fact]
    public async Task AMoveOfGitsIndexReachesTheWindowsEventStream()
    {
        var token = await Trade(Code(await Launch()));
        using var events = await OpenEvents(token);
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync());

        var staged = await Send(HttpMethod.Post, "/api/git/stage", Json(new { path = "README.md" }), token: token, origin: server.Origin);
        var changed = await NextEvent(reader, "changed");

        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
        Assert.Contains("\"git\":true", changed, StringComparison.Ordinal);
        Assert.DoesNotContain(".git/", changed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStatusListsWhatIsUncommittedAndARefusedCommitSaysWhy()
    {
        var token = await Trade(Code(await Launch()));

        var status = await Json(await Send(HttpMethod.Get, "/api/git/status", token: token));
        var refused = await Send(HttpMethod.Post, "/api/git/commit", Json(new { message = "Nothing staged" }), token: token, origin: server.Origin);
        var stranger = await Send(HttpMethod.Post, "/api/git/push", "{}", token: token, origin: "http://evil.example");

        var files = status.RootElement.GetProperty("files").EnumerateArray().Select((file) => file.GetProperty("path").GetString() + " " + file.GetProperty("state").GetString());
        Assert.Equal([".gitignore untracked", "README.md untracked", "src/Fold.cs untracked", "src/icon.png untracked"], files.Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("did not commit", (await Json(refused)).RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
    }

    /// <summary>
    /// The workspace's page over a changed file, in a real browser: the tree colours it and the folder that holds it, the
    /// reader marks its changed line and the place of its removed one, and Alt+d opens the git view on its diff, the file in
    /// place with the removed lines and the added ones, and the tree and the reader out of sight. Ctrl+g gives them back,
    /// the view stages the file and commits it from the keys, and a markdown file changed on disk then joins the list, its
    /// changed paragraph coloured whole in the reader and its heading not. A file deleted from disk stays in the tree and
    /// its diff is its removed lines.
    /// </summary>
    [WindowFact]
    public async Task TheWindowColoursAChangedFileAndReadsItsDiff()
    {
        TempBrain.CommitAll(project);
        await File.WriteAllTextAsync(Path.Combine(project, "src", "Fold.cs"), "class Fold\n{\n    int depth;\n}\n").ConfigureAwait(false);
        TempBrain.Git(project, "commit", "-q", "-am", "four lines");
        await File.WriteAllTextAsync(Path.Combine(project, "src", "Fold.cs"), "class Lens\n{\n}\n").ConfigureAwait(false);
        var window = await Headless.Open(await Launch().ConfigureAwait(false), Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("document.querySelector('.row.tree[data-change]')?.textContent.trim()", "▸src").ConfigureAwait(false);
            await window.Eval("execute('tree.toggle', { path: 'src' })").ConfigureAwait(false);
            await window.Until("document.querySelector('.row.tree[data-change=\"changed\"] .tree-name')?.textContent", "src").ConfigureAwait(false);
            await window.Eval("execute('file.open', { path: 'src/Fold.cs' })").ConfigureAwait(false);
            await window.Until("document.querySelector('#reader .ws-text .git-change[data-change=\"changed\"]')?.textContent", "class Lens\n").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('#reader .ws-text .git-gap').length", "1").ConfigureAwait(false);
            await window.Until("document.querySelector('#reader .ws-text').textContent", "class Lens\n{\n}\n").ConfigureAwait(false);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'd', altKey: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('#git-diff .ws-text [data-change=\"added\"]')?.textContent", "class Lens\n").ConfigureAwait(false);
            await window.Until("[...document.querySelectorAll('#git-diff .ws-text [data-change=\"deleted\"]')].map((run) => run.textContent).join('|')", "class Fold\n|    int depth;\n").ConfigureAwait(false);
            await window.Until("document.querySelector('#git-diff .ws-text').textContent", "class Fold\nclass Lens\n{\n    int depth;\n}\n").ConfigureAwait(false);
            Assert.Equal("false false", await window.Eval("(!!document.querySelector('#reader')) + ' ' + (!!document.querySelector('.ws-tree'))").ConfigureAwait(false));
            var lines = await window.Eval("document.querySelector('#git-diff .ws-lines').lastElementChild.getBoundingClientRect().bottom === document.querySelector('#git-diff .ws-text').lastElementChild.getBoundingClientRect().bottom").ConfigureAwait(false);
            Assert.Equal("True", lines);

            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'g', ctrlKey: true }))").ConfigureAwait(false);
            await window.Until("(!!document.querySelector('#reader')) + ' ' + workspace.focus", "true tree").ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'g', ctrlKey: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('[data-side=\"unstaged\"] .row.git-file[aria-selected=\"true\"] .git-file-path')?.textContent", "src/Fold.cs").ConfigureAwait(false);
            await window.Press("s", "KeyS").ConfigureAwait(false);
            await window.Until("document.querySelector('[data-side=\"staged\"] .row.git-file .git-file-path')?.textContent", "src/Fold.cs").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('[data-side=\"unstaged\"] .row.git-file').length", "0").ConfigureAwait(false);
            await window.Press("c", "KeyC").ConfigureAwait(false);
            await window.Until("document.activeElement?.id", "git-message").ConfigureAwait(false);
            await window.Eval("(() => { const input = document.getElementById('git-message'); input.value = 'Lens'; input.dispatchEvent(new Event('input')); })()").ConfigureAwait(false);
            await window.Eval("document.getElementById('git-message').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.ws-git-said')?.textContent.replace(/^Committed \\w+ /, '')", "Lens").ConfigureAwait(false);
            await window.Until("document.querySelectorAll('.row.git-file').length", "0").ConfigureAwait(false);
            Assert.Equal("Lens\n", TempBrain.Git(project, "log", "-1", "--format=%s"));

            await File.WriteAllTextAsync(Path.Combine(project, "README.md"), "# Lens\n\nRead the code.\n").ConfigureAwait(false);
            await window.Until("document.querySelector('.row.git-file .git-file-path')?.textContent", "README.md").ConfigureAwait(false);
            await window.Press("Escape", "Escape").ConfigureAwait(false);
            await window.Until("(!!document.querySelector('#reader')) + ' ' + workspace.focus", "true tree").ConfigureAwait(false);
            await window.Eval("execute('file.open', { path: 'README.md' })").ConfigureAwait(false);
            await window.Until("document.querySelector('#reader .prose p.git-change[data-change=\"changed\"]')?.textContent", "Read the code.").ConfigureAwait(false);
            Assert.Equal("False", await window.Eval("document.querySelector('#reader .prose h3').classList.contains('git-change')").ConfigureAwait(false));

            File.Delete(Path.Combine(project, ".gitignore"));
            await window.Until("document.querySelector('.row.tree[data-change=\"deleted\"] .tree-name')?.textContent", ".gitignore").ConfigureAwait(false);
            await window.Eval("execute('file.open', { path: '.gitignore' })").ConfigureAwait(false);
            await window.Until("document.querySelector('#reader .ws-empty')?.textContent", "This file is gone from disk.").ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'd', altKey: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('#git-diff .ws-text [data-change=\"deleted\"]')?.textContent", "bin/\n").ConfigureAwait(false);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The workspace's page, in a real browser: the commit message is a textarea that keeps Enter for a new line, the bar
    /// finds the git view by "git" with its key, Alt+h opens the help page which lists the keymap and Esc closes it, the
    /// bar finds it by "help", no pane shows a key hint, and a markdown file's diff wraps its long line with its colour on
    /// the line as a whole.
    /// </summary>
    [WindowFact]
    public async Task TheWindowWrapsMarkdownTakesAMultiLineMessageAndHelpsWithTheKeys()
    {
        TempBrain.CommitAll(project);
        await File.WriteAllTextAsync(Path.Combine(project, "README.md"), "# Lens\n\n" + string.Join(' ', Enumerable.Repeat("a long line that wraps", 40)) + "\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "src", "git.js"), "// a file named like the view\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "tests.md"), "# Tests\n").ConfigureAwait(false);
        var window = await Headless.Open(await Launch().ConfigureAwait(false), Path.Combine(brain.Root, "chromium")).ConfigureAwait(false);
        try
        {
            await window.Until("[...document.querySelectorAll('.row.tree[data-change] .tree-name')].some((name) => name.textContent === 'README.md')", "true").ConfigureAwait(false);
            Assert.Equal("0", await window.Eval("document.querySelectorAll('.ws-keys, .ws-side kbd').length").ConfigureAwait(false));

            await window.Eval("execute('bar.open'); execute('bar.query', { query: 'test' })").ConfigureAwait(false);
            await window.Until("document.querySelector('.ws-bar-label')?.textContent", "tests.md").ConfigureAwait(false);

            await window.Eval("execute('bar.open'); execute('bar.query', { query: 'git' })").ConfigureAwait(false);
            var found = "[...document.querySelectorAll('.ws-bar-item')].find((item) => item.querySelector('.ws-bar-label').textContent === '%s')";
            await window.Until("[...document.querySelectorAll('.ws-bar-label')].map((label) => label.textContent).filter((label) => label === 'git.js' || label === '.gitignore').length", "2").ConfigureAwait(false);
            Assert.Equal("Git: show or leave the view", await window.Eval("document.querySelector('.ws-bar-label').textContent").ConfigureAwait(false));
            Assert.Equal("True", await window.Eval("(() => { const labels = [...document.querySelectorAll('.ws-bar-label')].map((label) => label.textContent); const last = labels.findLastIndex((label) => label.startsWith('Git:')); return last >= 3 && labels.indexOf('git.js') > last && labels.indexOf('.gitignore') > last; })()").ConfigureAwait(false));
            await window.Until(found.Replace("%s", "Git: show or leave the view") + "?.querySelector('kbd').textContent", "Ctrl+g").ConfigureAwait(false);
            await window.Eval(found.Replace("%s", "Git: show or leave the view") + ".click()").ConfigureAwait(false);
            await window.Until("workspace.screen", "git").ConfigureAwait(false);
            await window.Eval("(async () => { await execute('file.open', { path: 'README.md' }); await execute('git.diff'); })()").ConfigureAwait(false);
            await window.Until("document.querySelector('#git-diff .ws-text [data-change=\"added\"]')?.textContent.length > 100", "true").ConfigureAwait(false);
            Assert.Equal("True", await window.Eval("(() => { const run = document.querySelector('#git-diff .ws-text [data-change=\"added\"]'); const diff = document.getElementById('git-diff'); return diff.scrollWidth <= diff.clientWidth && run.getBoundingClientRect().height > 40; })()").ConfigureAwait(false));
            Assert.Equal("0", await window.Eval("document.querySelectorAll('.ws-git-view kbd, .ws-git-keys').length").ConfigureAwait(false));

            await window.Eval("execute('git.message', {})").ConfigureAwait(false);
            await window.Until("document.activeElement?.tagName", "TEXTAREA").ConfigureAwait(false);
            await window.Eval("(() => { const box = document.getElementById('git-message'); box.value = 'one'; box.dispatchEvent(new Event('input')); })()").ConfigureAwait(false);
            await window.Press("Enter", "Enter", "\r").ConfigureAwait(false);
            await window.Until("workspace.focus + ' ' + workspace.git.said + ' ' + JSON.stringify(workspace.git.message) + ' ' + JSON.stringify(document.getElementById('git-message').value)", "message null \"one\\n\" \"one\\n\"").ConfigureAwait(false);

            await window.Eval("document.getElementById('git-message').dispatchEvent(new KeyboardEvent('keydown', { key: 'h', altKey: true, bubbles: true }))").ConfigureAwait(false);
            await window.Until("document.querySelector('.ws-help .pane-title')?.textContent", "Help").ConfigureAwait(false);
            await window.Until("[...document.querySelectorAll('.ws-help-key')].some((row) => row.textContent === 'Ctrl+EnterGit: commit what is staged')", "true").ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: '/' }))").ConfigureAwait(false);
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 's', altKey: true }))").ConfigureAwait(false);
            Assert.Equal("false help 0", await window.Eval("workspace.bar.open + ' ' + workspace.focus + ' ' + workspace.git.files.filter((file) => file.staged).length").ConfigureAwait(false));
            await window.Eval("document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))").ConfigureAwait(false);
            await window.Until("(!!document.querySelector('.ws-help')) + ' ' + workspace.focus", "false message").ConfigureAwait(false);

            await window.Eval("execute('bar.open'); execute('bar.query', { query: 'help' })").ConfigureAwait(false);
            await window.Until(found.Replace("%s", "Help: show or leave the keys") + "?.querySelector('kbd').textContent", "Alt+h").ConfigureAwait(false);
        }
        finally
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A launch for a frame of <see cref="Frame"/>, as brain code's server asks the workspace for one.</summary>
    private async Task<string> Launch()
    {
        var reply = await Send(HttpMethod.Post, "/api/key/launch", Json(new { frame = Frame }), key: Key()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.GetProperty("url").GetString()!;
    }

    private string Key() => new ServeFiles(brain.Home, WorkspaceServer.Folder(WorkspaceServer.Id(project))).ReadKey()!;

    /// <summary>The launch code in a launch's URL.</summary>
    private static string Code(string url) => LaunchCode().Match(url).Groups[1].Value;

    private async Task<string> Trade(string code)
    {
        var reply = await Send(HttpMethod.Post, "/api/launch", Json(new { code }), origin: server.Origin).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        using var json = JsonDocument.Parse(await reply.Content.ReadAsStringAsync().ConfigureAwait(false));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> OpenEvents(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add(WindowServer.SessionHeader, token);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    }

    /// <summary>The data of the next event named <paramref name="name"/> on a window's event stream; it fails after a wait.</summary>
    private static async Task<string> NextEvent(StreamReader events, string name)
    {
        using var limit = new CancellationTokenSource(Wait);
        var named = false;
        while (await events.ReadLineAsync(limit.Token).ConfigureAwait(false) is { } line)
        {
            if (line == "event: " + name)
            {
                named = true;
            }
            else if (named && line.StartsWith("data: ", StringComparison.Ordinal))
            {
                return line["data: ".Length..];
            }
        }

        throw new InvalidOperationException("the stream ended before " + name);
    }

    private static List<string?> Names(JsonDocument listing) =>
        [.. listing.RootElement.GetProperty("entries").EnumerateArray().Select((entry) => entry.GetProperty("name").GetString())];

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string? token = null, string? origin = null, string? host = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Add(WindowServer.SessionHeader, token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (key is not null)
        {
            request.Headers.Add(ServeFiles.KeyHeader, key);
        }

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    [GeneratedRegex("#launch=([0-9a-f]+)")]
    private static partial Regex LaunchCode();
}
