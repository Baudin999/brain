using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Serving;
using Brain.Windows;
using static Brain.Serving.WebCalls;
using static Brain.Windows.WindowServer;

namespace Brain.Workspace;

/// <summary>
/// A workspace's own server, one per project folder: a <see cref="WindowServer"/> that serves only its root, through
/// <see cref="ProjectFiles"/>, and ends once its window is gone. It opens no window of its own: brain code's server,
/// with the key, asks it for a launch through <c>/api/key/launch</c>, naming its own origin, and shows the page in a
/// frame of its window, which only that origin may frame. It asks git through <see cref="WorkspaceGit"/> what is
/// uncommitted, and stages, unstages, commits and pushes when the window asks; a POST that writes must come from the
/// window's origin. Its window hears on its event stream what changed on disk and whether git moved, which
/// <see cref="RootWatch"/> sees. It sends the window's records and its own to the <see cref="WindowLog"/> of
/// <c>perf/workspace</c>.
/// </summary>
// navarone: 0000164
internal sealed class WorkspaceServer
{
    public const string App = "brain-workspace";
    public const string LogDir = "workspace";

    private static readonly Dictionary<string, (string Resource, string Type)> Assets = WebAssets.With(
        ("/", "workspace/index.html", WebAssets.Page),
        ("/workspace.css", "workspace/workspace.css", WebAssets.Sheet),
        ("/core.js", "workspace/core.js", WebAssets.Script),
        ("/tree.js", "workspace/tree.js", WebAssets.Script),
        ("/reader.js", "workspace/reader.js", WebAssets.Script),
        ("/bar.js", "workspace/bar.js", WebAssets.Script),
        ("/help.js", "workspace/help.js", WebAssets.Script),
        ("/records.js", "workspace/records.js", WebAssets.Script),
        ("/git.js", "workspace/git.js", WebAssets.Script),
        ("/viewer.js", "workspace/viewer.js", WebAssets.Script));

    private readonly WindowLog log;
    private readonly Records records;
    private readonly ProjectFiles project;
    private readonly WorkspaceGit git;
    private readonly WindowServer window;
    private readonly TimeProvider time;

    public WorkspaceServer(string home, string userHome, string root, TextWriter err, TimeProvider time, OmarchyTheme theme)
    {
        var id = Id(root);
        log = new WindowLog(home, LogDir, Name(root) + "-" + id, time);
        records = new Records(log, time);
        project = new ProjectFiles(root, records);
        git = new WorkspaceGit(project, records);
        var kind = new WindowKind(App, "brain workspace", Folder(id), project.Root, Name(root), Title(project.Root, userHome), Assets);
        window = new WindowServer(home, kind, err, openWindow: null, time, theme, records, Keyed, Api);
        this.time = time;
    }

    public string Origin => window.Origin;

    public string Root => project.Root;

    /// <summary>The workspace's folder of cache, by the hash of its root: its key, its port, its launch files and its log.</summary>
    public static string Folder(string id) => Path.Combine("workspace", id);

    /// <summary>A short name for <paramref name="root"/> that stays the same for it: the first 16 hex digits of its hash.</summary>
    public static string Id(string root) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))))[..16];

    /// <summary>The folder's own name, as the window and the log name the workspace.</summary>
    public static string Name(string root) => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)));

    /// <summary>The page's title: the folder's name and its path, the home folder as ~.</summary>
    public static string Title(string root, string userHome)
    {
        var shown = root;
        if (root.StartsWith(userHome + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            shown = "~" + root[userHome.Length..];
        }

        return Name(root) + " · " + shown;
    }

    /// <summary>Listens until <paramref name="stop"/>, or until the window is gone; it watches its root while it serves.</summary>
    public async Task Serve(CancellationToken stop)
    {
        log.Prune();
        using var watch = new RootWatch(project, git.Watched, records, time, Changed);
        await window.Serve(stop).ConfigureAwait(false);
    }

    /// <summary>Answers one request.</summary>
    public void Handle(HttpListenerContext context) => window.Handle(context);

    /// <summary>
    /// The one keyed call, brain code's server's: a launch for a frame of its window, whose origin the body names. From
    /// then on only that origin may frame the page. A frame that names no origin of 127.0.0.1 is refused.
    /// </summary>
    private void Keyed(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        if ((request.HttpMethod, path) != (Post, "/api/key/launch"))
        {
            Refuse(response, HttpStatusCode.NotFound, "no such call");
            return;
        }

        var frame = Read(request, WorkspaceJson.Default.WorkspaceFrame)?.Frame;
        if (!IsLocalOrigin(frame))
        {
            Refuse(response, HttpStatusCode.BadRequest, "a launch names the origin on 127.0.0.1 that frames the workspace");
            return;
        }

        window.FramedBy(frame);
        Send(response, new WorkspaceLaunch(window.Launch()), WorkspaceJson.Default.WorkspaceLaunch);
    }

    /// <summary>True when <paramref name="origin"/> is an origin of 127.0.0.1 with a port, and nothing more.</summary>
    internal static bool IsLocalOrigin([NotNullWhen(true)] string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" || uri.IsDefaultPort)
        {
            return false;
        }

        return uri.GetLeftPart(UriPartial.Authority) == origin;
    }

    private void Changed(Changes changes) =>
        window.Publish("event: changed\ndata: " + JsonSerializer.Serialize(changes, WorkspaceJson.Default.Changes) + "\n\n");

    // navarone: 0000158
    // navarone: 0000161
    private void Api(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        var at = request.QueryString["path"] ?? string.Empty;
        switch (request.HttpMethod, path)
        {
            case (Get, "/api/tree"):
                Reading(response, () => project.List(at), WorkspaceJson.Default.TreeListing);
                break;
            case (Get, "/api/file"):
                Reading(response, () => project.Read(at), WorkspaceJson.Default.FileView);
                break;
            case (Get, "/api/files"):
                Send(response, project.All(), WorkspaceJson.Default.FileList);
                break;
            case (Get, "/api/git/status"):
                Send(response, git.Status(), WorkspaceJson.Default.GitStatus);
                break;
            case (Get, "/api/git/diff"):
                Reading(response, () => git.Diff(at), WorkspaceJson.Default.FileDiff);
                break;
            case (Get, "/api/git/patch"):
                Reading(response, () => git.Patch(at, request.QueryString["staged"] == "1"), WorkspaceJson.Default.GitPatch);
                break;
            case (Get, "/api/git/remote"):
                Reading(response, git.Remote, WorkspaceJson.Default.GitRemote);
                break;
            case (Post, "/api/git/stage") when request.Headers["Origin"] is not null:
                Acting(response, () => git.Stage(Read(request, WorkspaceJson.Default.GitPath)?.Path ?? string.Empty));
                break;
            case (Post, "/api/git/unstage") when request.Headers["Origin"] is not null:
                Acting(response, () => git.Unstage(Read(request, WorkspaceJson.Default.GitPath)?.Path ?? string.Empty));
                break;
            case (Post, "/api/git/commit") when request.Headers["Origin"] is not null:
                Acting(response, () => git.Commit(Read(request, WorkspaceJson.Default.GitMessage)?.Message));
                break;
            case (Post, "/api/git/push") when request.Headers["Origin"] is not null:
                Acting(response, git.Push);
                break;
            default:
                Refuse(response, HttpStatusCode.NotFound, "no such call");
                break;
        }
    }

    /// <summary>
    /// Does what the window asked of git and says what it did, or answers 409 with why git said no, which the window shows
    /// in its git pane. A failure is no refusal: it goes on to <see cref="WebCalls.Answer"/>.
    /// </summary>
    private static void Acting(HttpListenerResponse response, Func<GitDone> act)
    {
        try
        {
            Send(response, act(), WorkspaceJson.Default.GitDone);
        }
        catch (BrainException refusal) when (!refusal.IsFailure)
        {
            Refuse(response, HttpStatusCode.Conflict, refusal.Message);
        }
    }

    private static void Acting(HttpListenerResponse response, Action act) =>
        Acting(response, () =>
        {
            act();
            return new GitDone(string.Empty);
        });
}
