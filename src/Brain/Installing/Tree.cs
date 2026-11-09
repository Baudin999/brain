using System.Security.Cryptography;
using System.Text;

namespace Brain.Installing;

/// <summary>
/// The files at one target, keyed by their path under it with / between parts: the empty key when the target is a file
/// itself. It reads, hashes, writes, moves and deletes what stands at a target, following a link to read through it but
/// moving or deleting the link itself.
/// </summary>
internal static class Tree
{
    /// <summary>The key of a target that is one file.</summary>
    public const string Self = "";
    private const string Shebang = "#!";
    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>True when anything stands at <paramref name="path"/>, a link to nothing included.</summary>
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path) || IsLink(path);

    public static bool IsLink(string path) => new FileInfo(path).LinkTarget is not null;

    /// <summary>Where the link at <paramref name="path"/> points, as it was written; null when it is no link.</summary>
    public static string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    /// <summary>The files at <paramref name="path"/>, through a link; none when nothing stands there or the link leads nowhere.</summary>
    public static SortedDictionary<string, byte[]> Read(string path)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        if (Directory.Exists(path))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                files[System.IO.Path.GetRelativePath(path, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            }
        }
        else if (File.Exists(path) && File.ResolveLinkTarget(path, returnFinalTarget: true) is not { Exists: false })
        {
            files[Self] = File.ReadAllBytes(path);
        }

        return files;
    }

    /// <summary>One SHA-256 over every file's key and content, so two trees hash alike only when they hold the same files.</summary>
    public static string Hash(IReadOnlyDictionary<string, byte[]> files)
    {
        var text = new StringBuilder();
        foreach (var (key, bytes) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('\n').Append(Convert.ToHexStringLower(SHA256.HashData(bytes))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The SHA-256 of one file's bytes, as the pinned hashes of the model are written.</summary>
    public static string HashFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Writes <paramref name="files"/> at <paramref name="target"/>, where nothing stands. A script that starts with #! is made executable.</summary>
    public static void Write(string target, IReadOnlyDictionary<string, byte[]> files)
    {
        foreach (var (key, bytes) in files)
        {
            var path = key.Length == 0 ? target : System.IO.Path.Combine(target, key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            if (bytes.AsSpan().StartsWith(Encoding.ASCII.GetBytes(Shebang)) && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, Executable);
            }
        }
    }

    /// <summary>Moves what stands at <paramref name="from"/> to <paramref name="to"/>, a link as the link itself, wherever it leads.</summary>
    public static void Move(string from, string to)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
        if (LinkTarget(from) is { } source)
        {
            File.CreateSymbolicLink(to, source);
            File.Delete(from);
        }
        else if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
        }
    }

    /// <summary>Deletes what stands at <paramref name="path"/>: a link as the link itself, a directory with all it holds.</summary>
    public static void Delete(string path)
    {
        if (Directory.Exists(path) && !IsLink(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }
}
