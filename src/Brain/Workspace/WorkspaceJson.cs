using System.Text.Json.Serialization;

namespace Brain.Workspace;

/// <summary>One entry of a folder: its name, its path from the root with / between its parts, and whether it is a folder.</summary>
internal sealed record TreeEntry(string Name, string Path, bool Dir);

/// <summary>A folder's entries, folders first, each group by name; <c>Path</c> is empty for the root.</summary>
internal sealed record TreeListing(string Path, IReadOnlyList<TreeEntry> Entries);

/// <summary>
/// A file as the window reads it: its text, or null with the reason it shows none, binary or too large, or gone when it
/// is not there.
/// </summary>
internal sealed record FileView(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    long Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Problem);

/// <summary>Every file of the folder the bar finds in, and whether the list stopped at its limit.</summary>
internal sealed record FileList(IReadOnlyList<string> Paths, bool Cut);

/// <summary>
/// The paths that changed on disk, or all when the watch lost track of which, and whether git's index or a ref moved,
/// as a stage, a commit or a push moves them.
/// </summary>
internal sealed record Changes(IReadOnlyList<string> Paths, bool All, bool Git);

/// <summary>
/// One file uncommitted against HEAD, its path from the root: added, changed, deleted or untracked, and whether it has
/// changes staged and changes not staged.
/// </summary>
internal sealed record GitFile(string Path, string State, bool Staged, bool Unstaged);

/// <summary>
/// What is uncommitted under the root, with the branch, its upstream and how far ahead and behind it is; not a repo
/// when the root lies in none. Cut says the list stopped at its limit.
/// </summary>
internal sealed record GitStatus(bool Repo, string? Branch, string? Upstream, int Ahead, int Behind, IReadOnlyList<GitFile> Files, bool Cut);

/// <summary>
/// One hunk of a file's changes against HEAD: where it starts and how many lines it spans on each side, numbered from 1.
/// A side of no lines starts at the line it follows.
/// </summary>
internal sealed record GitHunk(int OldStart, int OldCount, int NewStart, int NewCount);

/// <summary>A file's changes against HEAD.</summary>
internal sealed record FileDiff(string Path, IReadOnlyList<GitHunk> Hunks);

/// <summary>One line of a patch: same, added or removed, and its text.</summary>
internal sealed record GitPatchLine(string Kind, string Text);

/// <summary>
/// A file's patch with every line of the file in it and the removed ones among them, in place: what staging it would
/// change when it is not staged, what it changed when it is. A file the window shows no text of, as a binary one, one
/// over the reader's limit or one gone, has no lines and says why in <c>Problem</c>.
/// </summary>
internal sealed record GitPatch(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Problem,
    IReadOnlyList<GitPatchLine> Lines);

/// <summary>One commit: its short hash and its subject.</summary>
internal sealed record GitCommit(string Hash, string Subject);

/// <summary>The commits a push would send, ahead of the upstream, and those the upstream has that the branch lacks.</summary>
internal sealed record GitRemote(IReadOnlyList<GitCommit> Ahead, IReadOnlyList<GitCommit> Behind);

/// <summary>The file a window stages or unstages.</summary>
internal sealed record GitPath(string? Path);

/// <summary>The message a window commits with.</summary>
internal sealed record GitMessage(string? Message);

/// <summary>What a commit or a push did, in a line the window shows.</summary>
internal sealed record GitDone(string Said);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TreeListing))]
[JsonSerializable(typeof(FileView))]
[JsonSerializable(typeof(FileList))]
[JsonSerializable(typeof(Changes))]
[JsonSerializable(typeof(GitStatus))]
[JsonSerializable(typeof(FileDiff))]
[JsonSerializable(typeof(GitPatch))]
[JsonSerializable(typeof(GitRemote))]
[JsonSerializable(typeof(GitPath))]
[JsonSerializable(typeof(GitMessage))]
[JsonSerializable(typeof(GitDone))]
internal sealed partial class WorkspaceJson : JsonSerializerContext;
