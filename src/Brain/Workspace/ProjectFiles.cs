using System.Text;
using Brain.Windows;

namespace Brain.Workspace;

/// <summary>
/// The folder a workspace serves, and nothing outside it: a path the window names is one from the root, with / between
/// its parts, and a path that leads out of the root is refused. A folder lists its entries without <c>.git</c> and,
/// in a git checkout, without what git ignores. A file reads as text, or says it is binary, too large or gone. Each
/// read, listing and git call is a record, with its budget: a listing shows within 200 ms, a file within 100 ms.
/// </summary>
internal sealed class ProjectFiles
{
    public const double ListBudget = 200;
    public const double ReadBudget = 100;
    public const string Binary = "binary";
    public const string TooLarge = "too large";
    public const string Gone = "gone";

    /// <summary>The largest file the window shows as text.</summary>
    public const long MaxText = 2 * 1024 * 1024;

    /// <summary>The most files the bar finds among.</summary>
    public const int MaxFiles = 50_000;

    /// <summary>A file with a zero byte in its first this many bytes is binary.</summary>
    private const int Sniff = 8000;

    private const string GitDir = ".git";

    private readonly Records records;
    private readonly bool checkout;

    public ProjectFiles(string root, Records records)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.records = records;
        checkout = records.Time("git", "rev-parse --show-toplevel", null, () => Git.TopLevel(Root)) is not null;
    }

    public string Root { get; }

    /// <summary>The entries of the folder at <paramref name="path"/>: folders first, each group by name.</summary>
    public TreeListing List(string path) => records.Time("list", path, ListBudget, () => Listing(path));

    /// <summary>The file at <paramref name="path"/>, as the window reads it.</summary>
    public FileView Read(string path) => records.Time("read", path, ReadBudget, () => Reading(path));

    /// <summary>Every file the bar finds among: in a checkout what git tracks or does not ignore, else every file not hidden.</summary>
    public FileList All() => records.Time("files", string.Empty, ListBudget, Files);

    /// <summary>The full path of <paramref name="path"/>, refused when it leads out of the root.</summary>
    public string Full(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Root, path)));
        if (full != Root && !full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new BrainException($"{path} is outside the workspace");
        }

        return full;
    }

    /// <summary>The path of <paramref name="full"/> from the root, with / between its parts.</summary>
    public string Relative(string full) => Path.GetRelativePath(Root, full).Replace(Path.DirectorySeparatorChar, '/');

    private TreeListing Listing(string path)
    {
        var full = Full(path);
        if (!Directory.Exists(full))
        {
            throw new BrainException($"{path} is no folder in the workspace");
        }

        var found = new DirectoryInfo(full).EnumerateFileSystemInfos()
            .Where((entry) => entry.Name != GitDir)
            .Select((entry) => new TreeEntry(entry.Name, Relative(entry.FullName), entry.Attributes.HasFlag(FileAttributes.Directory)))
            .ToList();
        var ignored = Ignored(found);
        var entries = found
            .Where((entry) => !ignored.Contains(entry.Path))
            .OrderBy((entry) => !entry.Dir)
            .ThenBy((entry) => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new TreeListing(path, entries);
    }

    /// <summary>The paths among <paramref name="entries"/> git ignores; none outside a checkout.</summary>
    private IReadOnlySet<string> Ignored(List<TreeEntry> entries)
    {
        if (!checkout || entries.Count == 0)
        {
            return new HashSet<string>();
        }

        var paths = entries.Select((entry) => entry.Path).ToList();
        return records.Time("git", $"check-ignore {paths.Count} paths", null, () => Git.Ignored(Root, paths));
    }

    private FileView Reading(string path)
    {
        var file = new FileInfo(Full(path));
        if (!file.Exists)
        {
            return new FileView(path, null, 0, Gone);
        }

        if (file.Length > MaxText)
        {
            return new FileView(path, null, file.Length, TooLarge);
        }

        var bytes = File.ReadAllBytes(file.FullName);
        if (bytes.AsSpan(0, Math.Min(bytes.Length, Sniff)).Contains((byte)0))
        {
            return new FileView(path, null, bytes.Length, Binary);
        }

        return new FileView(path, Encoding.UTF8.GetString(bytes), bytes.Length, null);
    }

    private FileList Files()
    {
        var all = checkout ? Git.Files(Root) : Walk();
        var paths = all.Take(MaxFiles).ToList();
        return new FileList(paths, all.Count > MaxFiles);
    }

    /// <summary>Every file under the root outside a folder whose name starts with a dot, up to one past the limit.</summary>
    private List<string> Walk()
    {
        var found = new List<string>();
        var folders = new Stack<DirectoryInfo>([new DirectoryInfo(Root)]);
        while (folders.Count > 0 && found.Count <= MaxFiles)
        {
            foreach (var entry in folders.Pop().EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo folder && !folder.Name.StartsWith('.'))
                {
                    folders.Push(folder);
                }
                else if (entry is FileInfo)
                {
                    found.Add(Relative(entry.FullName));
                }
            }
        }

        return found;
    }
}
