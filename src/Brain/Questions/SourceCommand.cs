using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Brain.Questions;

/// <summary>
/// Runs a source's command through <c>/bin/sh -c</c> in the data repo, with nothing on its stdin, and returns what it
/// printed. Words given after the command reach it as arguments of their own through <c>"$@"</c>, so the shell never
/// parses them. It throws a refusal that says why when the command cannot start, runs past its timeout, prints more
/// than <see cref="MaxOutput"/> characters or exits with another code than 0, with what the command said on stderr. A
/// late or chatty command is killed with every process it started.
/// </summary>
internal static class SourceCommand
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const int MaxOutput = 16 * 1024 * 1024;
    private const string Shell = "/bin/sh";
    private const string Words = " \"$@\"";
    private const int Chunk = 8192;
    private const int MaxSaid = 8192;
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(1);

    public static string Run(string command, string directory) => Run(command, directory, [], Timeout);

    public static string Run(string command, string directory, IReadOnlyList<string> words, TimeSpan timeout)
    {
        using var process = Start(command, directory, words);
        var clock = Stopwatch.StartNew();
        var output = ReadCapped(process.StandardOutput);
        var errors = ReadCapped(process.StandardError);
        if (!output.Wait(timeout))
        {
            Stop(process);
            throw Late(timeout, errors);
        }

        if (output.Result is not { } printed)
        {
            Stop(process);
            throw new BrainException($"it printed more than {MaxOutput / (1024 * 1024)} MB");
        }

        var left = timeout - clock.Elapsed;
        if (!process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero))
        {
            Stop(process);
            throw Late(timeout, errors);
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new BrainException($"it exited {process.ExitCode.ToString(CultureInfo.InvariantCulture)}" + Said(errors));
        }

        return printed;
    }

    /// <summary>
    /// The shell runs the command with the words after it as "$@": the words are its positional parameters, never text
    /// it parses. $0 names brain, so an error the shell itself prints says who ran it.
    /// </summary>
    private static Process Start(string command, string directory, IReadOnlyList<string> words)
    {
        var start = new ProcessStartInfo(Shell)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(words.Count == 0 ? command : command + Words);
        start.ArgumentList.Add("brain");
        foreach (var word in words)
        {
            start.ArgumentList.Add(word);
        }

        try
        {
            var process = Process.Start(start) ?? throw new BrainException($"{Shell} did not start");
            process.StandardInput.Close();
            return process;
        }
        catch (Win32Exception error)
        {
            throw new BrainException($"{Shell} did not start: {error.Message}");
        }
    }

    /// <summary>Everything the stream holds, or null as soon as it holds more than <see cref="MaxOutput"/> characters.</summary>
    private static async Task<string?> ReadCapped(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[Chunk];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (text.Length + read > MaxOutput)
            {
                return null;
            }

            text.Append(buffer, 0, read);
        }

        return text.ToString();
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited on its own in the meantime.
        }
    }

    /// <summary>What the command said on stderr after a colon, its end when it said much; nothing when it said nothing.</summary>
    private static string Said(Task<string?> errors)
    {
        if (!errors.Wait(Drain) || errors.Result?.Trim() is not { Length: > 0 } said)
        {
            return string.Empty;
        }

        return ": " + (said.Length > MaxSaid ? "..." + said[^MaxSaid..] : said);
    }

    private static BrainException Late(TimeSpan timeout, Task<string?> errors) =>
        new($"it did not finish within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds" + Said(errors));
}
