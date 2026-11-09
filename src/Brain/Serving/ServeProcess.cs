using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using Brain.Commands;

namespace Brain.Serving;

/// <summary>
/// Starts <c>brain serve</c>, or a workspace's server, as a process of its own, in the background, brain serve's output in
/// the data repo's <c>cache/serve.log</c>. It runs the published binary at its path beside this one, <c>cache/publish/brain</c>, so a
/// brain serve that restarts itself after <c>./build.sh</c> starts the new build and never the image it runs from.
/// </summary>
internal static class ServeProcess
{
    private const string LogFile = "serve.log";
    private const string Binary = "brain";
    private const string Sockets = "/proc/net/tcp";
    private const string Listening = "0A";

    /// <summary>The log brain serve writes in the data repo.</summary>
    public static string Log(string home) => Path.Combine(home, Layout.CacheDir, LogFile);

    /// <summary>Starts brain serve where <see cref="Where"/> says.</summary>
    public static void Start(BrainEnv env)
    {
        var (cwd, arguments) = Where(env);
        Start(Log(env.Home), cwd, arguments, "brain serve");
    }

    /// <summary>
    /// Starts the brain beside this one with <paramref name="arguments"/> in <paramref name="cwd"/>, in the background,
    /// its output appended to <paramref name="log"/>; <paramref name="what"/> names it when it will not start.
    /// </summary>
    public static void Start(string log, string cwd, IReadOnlyList<string> arguments, string what)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = cwd };
        foreach (var argument in new[] { "-c", "log=$1; shift; exec \"$@\" </dev/null >>\"$log\" 2>&1", "sh", log, Path.Combine(AppContext.BaseDirectory, Binary) })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using (Process.Start(start))
            {
            }
        }
        catch (Win32Exception error)
        {
            throw new BrainException($"{what} would not start in {cwd}: {error.Message}");
        }
    }

    /// <summary>
    /// The directory brain serve starts in and its arguments: the caller's directory and project. A directory or project
    /// removed since falls back to the data repo and to no project, so the new process can always start.
    /// </summary>
    internal static (string Cwd, IReadOnlyList<string> Arguments) Where(BrainEnv env) =>
        (Directory.Exists(env.Cwd) ? env.Cwd : env.Home,
         env.ProjectRepo is { } repo && Directory.Exists(repo) ? [ProjectFlag.Flag, repo, ServeCommand.Name] : [ServeCommand.Name]);

    /// <summary>
    /// The process of this account that listens on <paramref name="port"/> over IPv4, or null when none does or this
    /// account cannot see it. On Linux and WSL it reads <c>/proc</c>; elsewhere, as on macOS, it asks lsof.
    /// </summary>
    public static int? Listener(int port) => File.Exists(Sockets) ? FromProc(port) : FromLsof(port);

    /// <summary>Reads the socket's inode from <c>/proc/net/tcp</c> and finds the process that holds it among the account's own.</summary>
    private static int? FromProc(int port)
    {
        var suffix = ":" + port.ToString("X4", CultureInfo.InvariantCulture);
        var inode = File.ReadLines(Sockets).Skip(1)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(fields => fields.Length > 9 && fields[1].EndsWith(suffix, StringComparison.Ordinal) && fields[3] == Listening)?[9];
        if (inode is null)
        {
            return null;
        }

        var socket = "socket:[" + inode + "]";
        foreach (var process in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(process), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                continue;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(Path.Combine(process, "fd")).Any(fd => new FileInfo(fd).LinkTarget == socket))
                {
                    return pid;
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                // Another account's process, or one that ended while we looked.
            }
        }

        return null;
    }

    /// <summary>Asks <c>lsof</c> which process listens on the port; null when it names none or will not run.</summary>
    private static int? FromLsof(int port)
    {
        var start = new ProcessStartInfo("lsof") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-t", "-i", "tcp:" + port.ToString(CultureInfo.InvariantCulture), "-sTCP:LISTEN" })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var lsof = Process.Start(start)!;
            var said = lsof.StandardOutput.ReadToEnd();
            lsof.WaitForExit();
            var first = said.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
