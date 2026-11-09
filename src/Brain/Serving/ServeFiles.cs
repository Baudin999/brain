using System.Globalization;
using System.Security.Cryptography;

namespace Brain.Serving;

/// <summary>
/// The files only the user's own account may read, in <c>cache/serve</c>, a directory of mode 0700, or in the folder of
/// <c>cache</c> that <c>folder</c> names, as a workspace's. <c>key</c>, mode 0600, holds the secret brain ui, brain code
/// or brain code's server sends to reach the server that runs. A launch file, mode 0600, holds one window's launch
/// code: the browser opens the file, which sends it on to the window's URL, so the code never stands on a command line,
/// where every account on the machine could read it. The file goes once the window traded the code or a new one opens.
/// </summary>
internal sealed class ServeFiles(string home, string folder = ServeFiles.ServeDir)
{
    public const string KeyHeader = "X-Brain-Key";
    public const string ServeDir = "serve";
    private const string KeyFile = "key";
    private const string LaunchPrefix = "launch-";
    private const string LaunchExtension = ".html";
    private const int SecretBytes = 32;
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode PrivateDir = Private | UnixFileMode.UserExecute;

    public string Dir { get; } = Path.Combine(home, Layout.CacheDir, folder);

    /// <summary>The key the running server wrote, or null when there is none.</summary>
    public string? ReadKey() => Read(KeyFile);

    /// <summary>Writes a fresh key for this server and returns it.</summary>
    public string WriteKey()
    {
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        Write(KeyFile, key);
        return key;
    }

    /// <summary>The text the file <paramref name="name"/> holds, trimmed, or null when there is none.</summary>
    public string? Read(string name)
    {
        try
        {
            return File.ReadAllText(Path.Combine(Dir, name)).Trim();
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="text"/> to the file <paramref name="name"/>, mode 0600, in place of what it held.</summary>
    public void Write(string name, string text)
    {
        var path = Path.Combine(Dir, name);
        var temp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        WritePrivate(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Writes a launch file that sends the browser on to <paramref name="url"/>, removes every older one, and returns its path.</summary>
    public string WriteLaunch(string url)
    {
        RemoveLaunches();
        var path = Path.Combine(Dir, LaunchPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes / 2)) + LaunchExtension);
        WritePrivate(path, $"""
            <!doctype html>
            <meta charset="utf-8">
            <title>brain</title>
            <meta http-equiv="refresh" content="0;url={url}">
            <script>location.replace("{url}")</script>

            """);
        return path;
    }

    public void RemoveLaunches()
    {
        if (Directory.Exists(Dir))
        {
            foreach (var launch in Directory.EnumerateFiles(Dir, LaunchPrefix + "*" + LaunchExtension))
            {
                File.Delete(launch);
            }
        }
    }

    private void WritePrivate(string path, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new BrainException("brain's windows run on Linux, macOS and WSL");
        }

        Directory.CreateDirectory(Dir, PrivateDir);
        File.SetUnixFileMode(Dir, PrivateDir);
        using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = Private });
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }
}
