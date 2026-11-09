using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Brain.Children;

namespace Brain.Serving;

/// <summary>
/// The GPU's state for the perf log and the window's perf overlay: VRAM used in MB and load in percent, once a second.
/// It reads them from one <c>nvidia-smi</c> that runs as long as brain serve does and prints a line a second, never a
/// process per sample. That <c>nvidia-smi</c> starts under an <see cref="AgentKeeper"/>, so it is a descendant of brain
/// serve and stops with it however brain serve stops, a kill or a crash included. It runs at nice 19 and, on a machine
/// that has CPUs 16 to 31, pinned to them, so it never loads the CPUs below. Without <c>nvidia-smi</c> every sample is
/// null.
/// </summary>
internal sealed class GpuWatch : IDisposable
{
    public const string Program = "nvidia-smi";

    /// <summary>The first GPU, its memory used and its load, as bare numbers, a line a second.</summary>
    public static readonly IReadOnlyList<string> Arguments =
        ["--id=0", "--query-gpu=memory.used,utilization.gpu", "--format=csv,noheader,nounits", "--loop=1"];

    /// <summary>The CPUs it is pinned to: the ones that sustained load does not wear out on the user's machine.</summary>
    private const string PinnedCpus = "16-31";
    private const string HighestPinned = "/sys/devices/system/cpu/cpu31";

    /// <summary>How many seconds of samples it keeps, more than the ten a window's batch of rows spans.</summary>
    private const int Kept = 120;

    /// <summary>How old a sample may be and still stand for a second without one of its own.</summary>
    private const long Stale = 3;

    private static readonly TimeSpan Exit = TimeSpan.FromSeconds(5);

    private readonly Lock gate = new();
    private readonly SortedList<long, GpuSample> samples = [];
    private readonly TimeProvider time;
    private readonly Process? process;

    private GpuWatch(TimeProvider time, Process? process)
    {
        this.time = time;
        this.process = process;
    }

    /// <summary>A watch that reads nothing, as on a machine without <c>nvidia-smi</c>.</summary>
    public static GpuWatch None(TimeProvider time) => new(time, null);

    /// <summary>Starts <c>nvidia-smi</c> when it is on PATH; otherwise a watch that reads nothing.</summary>
    public static GpuWatch Start(TimeProvider time) => Start(time, Program, Arguments);

    /// <summary>Starts <paramref name="program"/> under the keeper, niced and pinned, and reads every line it prints.</summary>
    internal static GpuWatch Start(TimeProvider time, string program, IReadOnlyList<string> arguments)
    {
        if (!ClaudeRuns.Findable(program))
        {
            return None(time);
        }

        var info = Pinned(AgentKeeper.Start(program, arguments));
        info.RedirectStandardOutput = true;
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new Win32Exception();
        }
        catch (Win32Exception)
        {
            return None(time);
        }

        var watch = new GpuWatch(time, process);
        var reader = new Thread(watch.Read) { IsBackground = true, Name = "gpu watch" };
        reader.Start();
        return watch;
    }

    /// <summary>The newest sample, or null when there is none from the last few seconds.</summary>
    public GpuSample? Now() => At(time.GetUtcNow().ToUnixTimeSeconds());

    /// <summary>The sample of the second <paramref name="second"/>, since the epoch, or of the latest second shortly before it.</summary>
    public GpuSample? At(long second)
    {
        lock (gate)
        {
            for (var at = samples.Count - 1; at >= 0; at--)
            {
                var sampled = samples.Keys[at];
                if (sampled > second)
                {
                    continue;
                }

                return second - sampled <= Stale ? samples.Values[at] : null;
            }

            return null;
        }
    }

    /// <summary>A line <c>nvidia-smi</c> prints, such as <c>23494, 35</c>; null when it does not read.</summary>
    internal static GpuSample? Parse(string line)
    {
        var fields = line.Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length != 2)
        {
            return null;
        }

        if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var vram))
        {
            return null;
        }

        if (!int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var load))
        {
            return null;
        }

        return new GpuSample(vram, load);
    }

    /// <summary>Stops <c>nvidia-smi</c>: the keeper ends it and then itself, and what is still there after a while is killed.</summary>
    public void Dispose()
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited && (!Posix.Signal(process.Id, Posix.SigTerm) || !process.WaitForExit(Exit)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(Exit);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // It ended already.
        }

        process.Dispose();
    }

    /// <summary>The keeper's command behind <c>nice</c> and <c>taskset</c>, each when it is there; both exec, so the keeper's parent is still brain serve.</summary>
    private static ProcessStartInfo Pinned(ProcessStartInfo keeper)
    {
        var command = new List<string>();
        if (ClaudeRuns.Findable("nice"))
        {
            command.AddRange(["nice", "-n", "19"]);
        }

        if (ClaudeRuns.Findable("taskset") && Directory.Exists(HighestPinned))
        {
            command.AddRange(["taskset", "-c", PinnedCpus]);
        }

        command.Add(keeper.FileName);
        command.AddRange(keeper.ArgumentList);
        var info = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var argument in command.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>Reads each line until <c>nvidia-smi</c> ends, keeping the last <see cref="Kept"/> seconds of samples.</summary>
    private void Read()
    {
        try
        {
            while (process!.StandardOutput.ReadLine() is { } line)
            {
                if (Parse(line) is not { } sample)
                {
                    continue;
                }

                var second = time.GetUtcNow().ToUnixTimeSeconds();
                lock (gate)
                {
                    samples[second] = sample;
                    while (samples.Count > Kept)
                    {
                        samples.RemoveAt(0);
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // brain serve stopped it while it read.
        }
    }
}

/// <summary>The GPU at one second: its VRAM used in MB and its load in percent.</summary>
internal sealed record GpuSample(int VramMb, int LoadPercent);
