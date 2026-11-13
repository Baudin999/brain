using Brain.Serving;
using Brain.Workspace;

namespace Brain.Code;

/// <summary>
/// Where the server of a project's brain code window keeps its files: a folder of cache of its own under
/// <see cref="CodeServer.Folder"/>, by the id the project's workspace has, which holds its key, its port, its launch
/// files, its handover and its log. The window's class carries that folder, so the project opener tells one project's
/// window from another's. <c>brain code</c>, its permission hook and <c>brain ask</c> find the server there.
/// </summary>
internal static class CodeProjectFiles
{
    /// <summary>The folder of cache of <paramref name="project"/>'s server: <c>code/&lt;id&gt;</c>.</summary>
    public static string Folder(string project) => Path.Combine(CodeServer.Folder, WorkspaceServer.Id(project));

    /// <summary>The files of <paramref name="project"/>'s server in the data repo at <paramref name="home"/>.</summary>
    public static ServeFiles Of(string home, string project) => new(home, Folder(project));
}
