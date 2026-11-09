using Brain.Serving;

namespace Brain.Code;

/// <summary>
/// The local file system as brain code's file explorer reads it: a folder's folders and images, nothing else, and an
/// image file's bytes; and the folders the user attached images from, the newest first, kept as state in the data
/// repo's <c>cache/code</c>. A folder is remembered once an image is attached from it, never for being looked into.
/// The explorer opens in the folder last attached from, and in <c>~/Pictures</c> the first time.
/// </summary>
internal sealed class ImageFolders(string userHome, ServeFiles cache)
{
    /// <summary>How many folders attached from the explorer offers.</summary>
    public const int Remembered = 5;

    /// <summary>The most bytes of an image file the explorer reads, to show it; one it attaches holds at most <see cref="AttachedImages.Largest"/>.</summary>
    public const int Largest = 32 * 1024 * 1024;

    private const string RecentFile = "attached-from";

    /// <summary>What the explorer leaves out: what a dot hides, and what the system keeps.</summary>
    private static readonly EnumerationOptions Shown = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private readonly Lock gate = new();

    private string Pictures => Path.Combine(userHome, "Pictures");

    /// <summary>Where the explorer opens, ~/Pictures, and the folders attached from that are still there.</summary>
    public CodePlaces Places()
    {
        var recent = Recent().Where(Directory.Exists).ToList();
        var start = userHome;
        if (Directory.Exists(Pictures))
        {
            start = Pictures;
        }

        if (recent.Count > 0)
        {
            start = recent[0];
        }

        return new CodePlaces(start, Pictures, recent);
    }

    /// <summary>The folders and images of the folder <paramref name="path"/>, each sorted by name; refused for a path that is no folder.</summary>
    public static CodeFolder List(string? path)
    {
        var folder = Full(path);
        if (!Directory.Exists(folder))
        {
            throw new BrainException($"there is no folder {folder}");
        }

        var info = new DirectoryInfo(folder);
        List<string> folders;
        List<string> images;
        try
        {
            folders = [.. info.EnumerateDirectories("*", Shown).Select((one) => one.Name).Order(StringComparer.OrdinalIgnoreCase)];
            images = [.. info.EnumerateFiles("*", Shown).Select((one) => one.Name).Where(IsImage).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (UnauthorizedAccessException)
        {
            throw new BrainException($"brain code may not read {folder}");
        }

        return new CodeFolder(folder, info.Parent?.FullName, folders, images);
    }

    /// <summary>
    /// The image file <paramref name="path"/>, in base64. Refused for a path whose name is no image's, for what its links
    /// end in when that is no file with a size, as a folder, a FIFO or a device, for a file over <see cref="Largest"/>
    /// bytes, and for a file whose first bytes are not its media type's, so nothing but an image is ever read whole.
    /// </summary>
    public static CodeImage Image(string? path)
    {
        var file = Full(path);
        var none = $"there is no image {file}";
        var type = AttachedImages.MediaTypeOf(file);
        if (type is null)
        {
            throw new BrainException(none);
        }

        var target = Target(file);
        if (target is null || target.Length == 0)
        {
            throw new BrainException(none);
        }

        if (target.Length > Largest)
        {
            throw new BrainException($"{Path.GetFileName(file)} is larger than the {Largest / (1024 * 1024)} MB the explorer reads");
        }

        var bytes = Read(target);
        if (AttachedImages.Sniffed(bytes) != type)
        {
            throw new BrainException($"{Path.GetFileName(file)} is no {type} image");
        }

        return new CodeImage(Path.GetFileName(file), type, Convert.ToBase64String(bytes));
    }

    /// <summary>
    /// The file <paramref name="file"/> is, or the file its links end in; null when that is missing or no file. A FIFO, a
    /// socket or a device reads as a file of length 0, which the caller refuses before it opens one.
    /// </summary>
    private static FileInfo? Target(string file)
    {
        var info = new FileInfo(file);
        if (info.LinkTarget is not null)
        {
            info = info.ResolveLinkTarget(returnFinalTarget: true) as FileInfo;
        }

        if (info is null || !info.Exists)
        {
            return null;
        }

        return info;
    }

    private static byte[] Read(FileInfo file)
    {
        try
        {
            return File.ReadAllBytes(file.FullName);
        }
        catch (UnauthorizedAccessException)
        {
            throw new BrainException($"brain code may not read {file.FullName}");
        }
    }

    /// <summary>Remembers <paramref name="path"/> as the folder last attached from; refused for a path that is no folder.</summary>
    public void Attached(string? path)
    {
        var folder = Full(path);
        if (!Directory.Exists(folder))
        {
            throw new BrainException($"there is no folder {folder}");
        }

        lock (gate)
        {
            var recent = Recent().Where((one) => one != folder).Prepend(folder).Take(Remembered);
            cache.Write(RecentFile, string.Join('\n', recent));
        }
    }

    private List<string> Recent()
    {
        var text = cache.Read(RecentFile) ?? string.Empty;
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static bool IsImage(string name) => AttachedImages.MediaTypeOf(name) is not null;

    /// <summary>A full path, as the explorer names every folder and file; refused for one that is not.</summary>
    private static string Full(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
        {
            throw new BrainException($"the explorer names a folder or a file by its full path, not {path}");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
