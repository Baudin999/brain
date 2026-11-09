using System.Net.Http.Json;
using Brain.Serving;
using Brain.Windows;
using Brain.Workspace;

namespace Brain.Code;

/// <summary>
/// The workspaces of brain code's sessions, one per folder, which the sessions in that folder share. Each is a
/// workspace's own server, apart from brain code's, with its own key and its own launch codes: brain code only shows
/// it. The first time the window shows a folder's workspace, the folder's server starts unless one runs, and is asked,
/// with the key in the workspace's cache, for a launch of a frame of brain code's window, whose origin it names. The
/// server ends as it does, a few seconds after no frame shows it.
/// </summary>
/// <param name="start">Starts the server of a folder's workspace in a process of its own, and gives the log it writes.</param>
internal sealed class CodeWorkspaces(string home, Func<string, string> start)
{
    private readonly Lock gate = new();

    /// <summary>
    /// The URL a frame of <paramref name="frame"/>, an origin, opens the workspace of <paramref name="folder"/> on: its
    /// page with a fresh launch code. The folder's server starts first when none runs.
    /// </summary>
    public string Launch(string folder, string frame)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var files = new ServeFiles(home, WorkspaceServer.Folder(WorkspaceServer.Id(root)));
        using var client = Running(files, root);
        using var request = WindowClient.Keyed(HttpMethod.Post, "/api/key/launch", files);
        request.Content = JsonContent.Create(new WorkspaceFrame(frame), WorkspaceJson.Default.WorkspaceFrame);
        using var response = client.Send(request);
        response.EnsureSuccessStatusCode();
        var launch = response.Content.ReadFromJsonAsync(WorkspaceJson.Default.WorkspaceLaunch).GetAwaiter().GetResult();
        return launch?.Url ?? throw BrainException.Failed($"the workspace of {root} gave no launch");
    }

    /// <summary>A client of the workspace's server over <paramref name="root"/>, started and waited for when none runs; one starts at a time.</summary>
    private HttpClient Running(ServeFiles files, string root)
    {
        lock (gate)
        {
            return WindowClient.Running(files, WorkspaceServer.App, root) ?? Started(files, root);
        }
    }

    private HttpClient Started(ServeFiles files, string root)
    {
        var log = start(root);
        return WindowClient.Await(files, WorkspaceServer.App, root, log);
    }
}
