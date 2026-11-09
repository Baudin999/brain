using System.Diagnostics;
using System.Text;
using Brain.Commands;

namespace Brain.Usage;

/// <summary>
/// Runs one CLI call and writes its usage line: when, the session and whether an AI session or the user's own terminal
/// called, the command and its arguments, the characters it printed, how long it ran, its exit status, and the refs it
/// returned or opened. <c>brain record</c>, which a hook runs to log an event of its own, is not a call of its own, and
/// neither is <c>brain install</c> or <c>brain uninstall</c>, which run before the data repo is there or after it is
/// let go: logging them would make the data repo the install asks about.
/// </summary>
internal static class Metering
{
    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var (command, after) = Split(arguments);
        if (command is RecordCommand.Name or InstallCommand.Name or UninstallCommand.Name)
        {
            return Cli.Run(env, arguments);
        }

        var refs = new CallRefs(env.Home, env.UserHome);
        using var output = new CountingWriter(env.Out);
        using var errors = new CountingWriter(env.Err);
        var clock = Stopwatch.StartNew();
        // A call that throws is logged as exit 1, the exit the boundary around it gives it.
        var code = ExitCodes.Failure;
        try
        {
            code = Cli.Run(env with { Out = output, Err = errors, Refs = refs }, arguments);
        }
        finally
        {
            Log(env, command, after, output.Count + errors.Count, clock.ElapsedMilliseconds, code, refs);
        }

        return code;
    }

    private static void Log(BrainEnv env, string command, IReadOnlyList<string> after, long chars, long ms, int code, CallRefs refs) =>
        UsageLog.Append(env.Home, new UsageEvent(UsageLog.Now(), UsageEvent.Call, env.Session)
        {
            Caller = env.Session is null ? UsageEvent.User : UsageEvent.Ai,
            Command = command,
            Args = after,
            Chars = chars,
            Ms = ms,
            Exit = code,
            Returned = refs.Returned.Count > 0 ? refs.Returned : null,
            Opened = refs.Opened.Count > 0 ? refs.Opened : null,
        }, env.Err);

    /// <summary>The command and its arguments, past a leading <c>--project</c> and its value.</summary>
    public static (string Command, IReadOnlyList<string> Arguments) Split(IReadOnlyList<string> arguments)
    {
        var skip = arguments.Count >= 2 && string.Equals(arguments[0], ProjectFlag.Flag, StringComparison.Ordinal) ? 2 : 0;
        return arguments.Count > skip ? (arguments[skip], [.. arguments.Skip(skip + 1)]) : (string.Empty, []);
    }

    /// <summary>A writer that passes everything on to the writer it wraps and counts the characters, never closing it.</summary>
    private sealed class CountingWriter(TextWriter inner) : TextWriter(inner.FormatProvider)
    {
        public long Count { get; private set; }

        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            Count++;
            inner.Write(value);
        }

        public override void Write(string? value)
        {
            Count += value?.Length ?? 0;
            inner.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Count += count;
            inner.Write(buffer, index, count);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            Count += buffer.Length;
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();
    }
}
