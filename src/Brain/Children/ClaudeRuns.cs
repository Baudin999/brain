using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Brain.Children;

/// <summary>
/// The one door through which brain starts claude: the open agent of a chat or a sitting, and every ask with no tools,
/// such as the judge's and a dream's. Each claude starts under an <see cref="AgentKeeper"/>, so it and everything it
/// starts are descendants of this brain process and stop with it however it stops. The door keeps every claude that
/// runs in this process's memory, and nothing of it on disk, with the place it runs for, so brain serve can list them.
/// </summary>
// navarone: 0000058
internal static class ClaudeRuns
{
    private static readonly Lock Gate = new();
    private static readonly List<ClaudeRun> Running = [];
    private static readonly AsyncLocal<RunPlace?> Ambient = new();

    /// <summary>
    /// Starts <paramref name="info"/>'s program, claude or a stand-in for it, under the keeper, on
    /// <paramref name="model"/>, for <paramref name="place"/> or, when that is null, for the place
    /// <see cref="For"/> set around the caller. The keeper, and so claude, runs with <paramref name="info"/>'s environment.
    /// </summary>
    public static ClaudeRun Start(ProcessStartInfo info, string model, RunPlace? place = null)
    {
        BrainException.ThrowIf(!Findable(info.FileName) ? $"{info.FileName} is not on PATH" : null);
        var kept = AgentKeeper.Start(info.FileName, [.. info.ArgumentList]);
        kept.WorkingDirectory = info.WorkingDirectory;
        kept.RedirectStandardInput = info.RedirectStandardInput;
        kept.RedirectStandardOutput = info.RedirectStandardOutput;
        kept.RedirectStandardError = info.RedirectStandardError;
        kept.StandardInputEncoding = info.StandardInputEncoding;
        kept.StandardOutputEncoding = info.StandardOutputEncoding;
        kept.StandardErrorEncoding = info.StandardErrorEncoding;
        foreach (var (name, value) in info.Environment)
        {
            kept.Environment[name] = value;
        }

        Process process;
        try
        {
            process = Process.Start(kept) ?? throw new BrainException($"{info.FileName} did not start");
        }
        catch (Win32Exception error)
        {
            throw new BrainException($"brain could not start its agent keeper for {info.FileName}: {error.Message}");
        }

        var run = new ClaudeRun(process, model, place ?? Ambient.Value, DateTimeOffset.UtcNow);
        lock (Gate)
        {
            Running.Add(run);
        }

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Forget(run);
        if (process.HasExited)
        {
            Forget(run);
        }

        return run;
    }

    /// <summary>Every claude that runs now, the oldest first.</summary>
    public static IReadOnlyList<ClaudeRun> Now()
    {
        lock (Gate)
        {
            return [.. Running];
        }
    }

    /// <summary>Sets the place every claude started from here on, in this flow and the tasks it starts, runs for, until disposed.</summary>
    public static IDisposable For(RunPlace place)
    {
        var before = Ambient.Value;
        Ambient.Value = place;
        return new Restore(before);
    }

    /// <summary>Stops every claude that runs, with everything each started: brain serve stops.</summary>
    public static void StopAll()
    {
        foreach (var run in Now())
        {
            run.Stop();
        }
    }

    /// <summary>True when <paramref name="program"/> is a path that exists or a name found on PATH, as the keeper will look for it.</summary>
    internal static bool Findable(string program) =>
        program.Contains('/', StringComparison.Ordinal)
            ? File.Exists(program)
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, program)));

    internal static void Forget(ClaudeRun run)
    {
        lock (Gate)
        {
            Running.Remove(run);
        }
    }

    private sealed class Restore(RunPlace? before) : IDisposable
    {
        public void Dispose() => Ambient.Value = before;
    }
}

/// <summary>What a claude runs for, in brain serve: the window's tab, what kind of work, which one, and how the window names it.</summary>
internal sealed record RunPlace(string Tab, string Kind, string Id, string Name);

/// <summary>
/// Something that runs under a claude and stops with it: an agent it sent, as a builder or a validator, or a process it
/// started, with whether that holds one of the machine's build slots.
/// </summary>
internal sealed record AgentWork(string Kind, string What, bool Slot)
{
    public const string AnAgent = "agent";
    public const string AProcess = "process";
}

/// <summary>
/// A process a claude started that still runs: its pid, its whole command, whether it or anything below it holds a
/// build slot, and since when its tree used no CPU time, once that is <see cref="JobActivity.Quiet"/> or longer.
/// </summary>
internal sealed record RunningJob(int Pid, string Command, bool Slot, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? Idle);

/// <summary>One claude that runs under its keeper: the keeper's process, which carries claude's input and output, its model, its place and its start.</summary>
internal sealed class ClaudeRun(Process process, string model, RunPlace? place, DateTimeOffset started) : IDisposable
{
    private const int CommandLength = 160;

    /// <summary>Claude Code runs a Bash call as <c>bash -c '... eval 'the command' &lt; /dev/null ...'</c>; the window names the command.</summary>
    private const string Eval = "eval '";
    private const string EvalEnd = "' < /dev/null";

    private static readonly TimeSpan Exit = TimeSpan.FromSeconds(5);

    /// <summary>How long a job is given to end once asked, and how often it is looked for meanwhile.</summary>
    private static readonly TimeSpan JobExit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JobLook = TimeSpan.FromMilliseconds(50);

    private readonly JobActivity activity = new();

    public Process Process { get; } = process;

    public string Model { get; } = model;

    public RunPlace? Place { get; } = place;

    public DateTimeOffset Started { get; } = started;

    /// <summary>
    /// Each process claude started that still runs, claude's own child or, on Linux, an orphan that came to its keeper,
    /// as brain's window names it: its command cut to one short line, and whether anything below it holds a build slot.
    /// </summary>
    public IReadOnlyList<AgentWork> Processes() => [.. Running().Select(job => new AgentWork(AgentWork.AProcess, Cut(job.Command), job.Slot))];

    /// <summary>
    /// Each process claude started that still runs, by its pid, with its whole command, whether anything below it holds
    /// a build slot, and since when its tree is idle, or null while it runs. Without a subreaper, as on macOS, no orphan
    /// comes to the keeper, so its only child is claude and any other is the keeper's own read of the process table.
    /// </summary>
    public IReadOnlyList<RunningJob> Running()
    {
        var tree = ProcessTree.Read();
        var jobs = Jobs(tree, Process.Id, OperatingSystem.IsLinux());
        var uses = new List<JobUse>();
        foreach (var job in jobs)
        {
            uses.Add(new JobUse(job, tree.Start(job) ?? string.Empty, tree.CpuOf(job)));
        }

        var idle = activity.Read(uses, DateTimeOffset.UtcNow);
        var running = new List<RunningJob>();
        for (var at = 0; at < jobs.Count; at++)
        {
            var job = jobs[at];
            var slot = tree.Below(job).Prepend(job).Any(ProcessTree.HoldsSlot);
            running.Add(new RunningJob(job, Called(ProcessTree.Command(job)), slot, idle[at]));
        }

        return running;
    }

    /// <summary>
    /// Ends the process <paramref name="pid"/> claude started and everything below it, as <see cref="Stop"/> ends claude:
    /// each is asked to end, and what is still there after a short wait is killed. Only a process that is one of claude's
    /// jobs in a fresh read of the process table is signalled, with what was seen below it while it ran, each by its
    /// start, so a later process given the same pid is never touched. False when <paramref name="pid"/> runs no more; refused when it runs but claude did not start it.
    /// </summary>
    // navarone: 0000210
    public bool End(int pid)
    {
        var tree = ProcessTree.Read();
        var start = tree.Start(pid);
        if (start is null)
        {
            return false;
        }

        if (!Jobs(tree, Process.Id, OperatingSystem.IsLinux()).Contains(pid))
        {
            throw new BrainException($"process {pid} is not one this session started");
        }

        var family = new Dictionary<int, string>();
        Remember(tree, pid, start, family);
        Signal(Alive(tree, family), Posix.SigTerm);
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < JobExit)
        {
            Thread.Sleep(JobLook);
            tree = ProcessTree.Read();
            Remember(tree, pid, start, family);
            if (Alive(tree, family).Count == 0)
            {
                return true;
            }
        }

        tree = ProcessTree.Read();
        var left = Alive(tree, family);
        Signal(left, Posix.SigKill);
        return true;
    }

    /// <summary>
    /// The processes claude started under <paramref name="keeper"/>: claude is the keeper's oldest child, so its children,
    /// and with <paramref name="orphansComeToKeeper"/>, as on Linux, the keeper's other children too.
    /// </summary>
    internal static IReadOnlyList<int> Jobs(ProcessTree tree, int keeper, bool orphansComeToKeeper)
    {
        var below = tree.Children(keeper);
        return below.Count == 0 ? [] : [.. tree.Children(below[0]).Concat(orphansComeToKeeper ? below.Skip(1) : [])];
    }

    /// <summary>
    /// Stops claude and every process it started: the keeper ends all of them and then itself, and what is still there
    /// after a while is killed by its tree.
    /// </summary>
    public void Stop()
    {
        try
        {
            if (!Process.HasExited && (!Posix.Signal(Process.Id, Posix.SigTerm) || !Process.WaitForExit(Exit)))
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(Exit);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // It ended already.
        }

        ClaudeRuns.Forget(this);
    }

    public void Dispose()
    {
        ClaudeRuns.Forget(this);
        Process.Dispose();
    }

    /// <summary>
    /// Adds the job <paramref name="pid"/> and every process below it to <paramref name="family"/>, each with its start,
    /// while the job is still the process that started at <paramref name="start"/>. A process the job leaves behind as it
    /// ends stays in the family by its start, and once the job is gone nothing new joins.
    /// </summary>
    private static void Remember(ProcessTree tree, int pid, string start, Dictionary<int, string> family)
    {
        if (tree.Start(pid) != start)
        {
            return;
        }

        family[pid] = start;
        foreach (var below in tree.Below(pid))
        {
            if (tree.Start(below) is { } started)
            {
                family[below] = started;
            }
        }
    }

    /// <summary>Each process of <paramref name="family"/> that still runs as the process it was remembered as.</summary>
    private static List<int> Alive(ProcessTree tree, Dictionary<int, string> family)
    {
        var alive = new List<int>();
        foreach (var (pid, start) in family)
        {
            if (tree.Start(pid) == start)
            {
                alive.Add(pid);
            }
        }

        return alive;
    }

    private static void Signal(IReadOnlyList<int> pids, int signal)
    {
        foreach (var pid in pids)
        {
            Posix.Signal(pid, signal);
        }
    }

    /// <summary>A process's command line as the window names it: the command of a Bash call, whole.</summary>
    private static string Called(string command)
    {
        var start = command.IndexOf(Eval, StringComparison.Ordinal);
        var end = command.LastIndexOf(EvalEnd, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return command;
        }

        return command[(start + Eval.Length)..end].Replace("'\\''", "'", StringComparison.Ordinal);
    }

    /// <summary>A command cut to its first line, at most <see cref="CommandLength"/> long.</summary>
    private static string Cut(string command)
    {
        var shown = command.Split('\n')[0].Trim();
        return shown.Length > CommandLength ? shown[..CommandLength] + "..." : shown;
    }
}
