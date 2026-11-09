using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Brain.Children;

/// <summary>
/// The processes alive on this machine at one moment: each one's parent, its process group, when it started and, on
/// Linux, the CPU time it used. On
/// Linux it reads <c>/proc</c>; elsewhere, as on macOS, it asks <c>ps</c>, and leaves that ps out. A zombie is not alive
/// and is left out of every list but <see cref="Zombies"/>. The start is an opaque key that tells a process from a later
/// one with the same pid: the clock ticks since boot on Linux, the start to the second on macOS.
/// </summary>
internal sealed class ProcessTree
{
    private const string Proc = "/proc";

    /// <summary>The lock files of this machine's two build slots, which the cpu-guard rule names: cargo-guard.lock and cargo-guard-2.lock.</summary>
    private const string SlotPrefix = "cargo-guard";
    private const string SlotSuffix = ".lock";

    /// <summary>Fields after the command's closing parenthesis in <c>/proc/pid/stat</c>: the state, the parent, the group and the start time.</summary>
    private const int StateField = 0;
    private const int ParentField = 1;
    private const int GroupField = 2;
    private const int UserTimeField = 11;
    private const int SystemTimeField = 12;
    private const int StartField = 19;

    /// <summary>The columns <c>ps</c> prints, the start last since it holds spaces.</summary>
    private const int PsColumns = 4;

    /// <summary>The start <c>ps</c> prints as <c>lstart</c>, once its runs of spaces are one.</summary>
    private const string StartFormat = "ddd MMM d HH:mm:ss yyyy";

    private readonly IReadOnlyDictionary<int, Entry> processes;

    internal ProcessTree(IReadOnlyDictionary<int, Entry> processes) => this.processes = processes;

    // navarone: 0000074
    public static ProcessTree Read() => Directory.Exists(Proc) ? FromProc() : FromPs();

    /// <summary>The live children of <paramref name="pid"/>, the oldest first.</summary>
    public IReadOnlyList<int> Children(int pid) =>
        [.. processes.Where(process => process.Value.Parent == pid && !process.Value.Zombie).OrderBy(process => process.Value.Ticks).ThenBy(process => process.Key).Select(process => process.Key)];

    /// <summary>The children of <paramref name="pid"/> that ended and wait to be reaped.</summary>
    public IReadOnlyList<int> Zombies(int pid) =>
        [.. processes.Where(process => process.Value.Parent == pid && process.Value.Zombie).Select(process => process.Key)];

    /// <summary>Every live process below <paramref name="pid"/>, parents before their children.</summary>
    public IReadOnlyList<int> Below(int pid)
    {
        var below = new List<int>();
        var next = new Queue<int>([pid]);
        while (next.TryDequeue(out var parent))
        {
            foreach (var child in Children(parent))
            {
                below.Add(child);
                next.Enqueue(child);
            }
        }

        return below;
    }

    /// <summary>Every live process in the process group <paramref name="group"/>.</summary>
    public IReadOnlyList<int> InGroup(int group) =>
        [.. processes.Where(process => process.Value.Group == group && !process.Value.Zombie).Select(process => process.Key)];

    /// <summary>When the live process <paramref name="pid"/> started, or null when no live process has that pid.</summary>
    public string? Start(int pid) => processes.TryGetValue(pid, out var entry) && !entry.Zombie ? entry.Start : null;

    /// <summary>
    /// The CPU time, in clock ticks, that the live process <paramref name="pid"/> and every live process below it used,
    /// user and system; null when one of them is not alive here or where the table holds no CPU time, as from ps.
    /// </summary>
    public long? CpuOf(int pid)
    {
        if (!processes.TryGetValue(pid, out var root))
        {
            return null;
        }

        if (root.Zombie)
        {
            return null;
        }

        long used = 0;
        foreach (var each in Below(pid).Prepend(pid))
        {
            var cpu = processes[each].Cpu;
            if (cpu is null)
            {
                return null;
            }

            used += cpu.Value;
        }

        return used;
    }

    /// <summary>The command line of <paramref name="pid"/>, its arguments joined by spaces; empty once it is gone.</summary>
    public static string Command(int pid)
    {
        if (!Directory.Exists(Proc))
        {
            return Ps(["-o", "command=", "-p", pid.ToString(CultureInfo.InvariantCulture)])?.Trim() ?? string.Empty;
        }

        try
        {
            return File.ReadAllText(Path.Combine(Proc, pid.ToString(CultureInfo.InvariantCulture), "cmdline")).TrimEnd('\0').Replace('\0', ' ');
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when <paramref name="pid"/> holds one of this machine's build slots open, as a guard does while its build runs.
    /// It reads the process's open files in <c>/proc</c>, so where there is none, as on macOS, no process holds a slot.
    /// </summary>
    public static bool HoldsSlot(int pid)
    {
        try
        {
            return Directory.Exists(Proc) && Directory.EnumerateFileSystemEntries(Path.Combine(Proc, pid.ToString(CultureInfo.InvariantCulture), "fd"))
                .Select(fd => Path.GetFileName(new FileInfo(fd).LinkTarget ?? string.Empty))
                .Any(name => name.StartsWith(SlotPrefix, StringComparison.Ordinal) && name.EndsWith(SlotSuffix, StringComparison.Ordinal));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ProcessTree FromProc()
    {
        var processes = new Dictionary<int, Entry>();
        foreach (var dir in Directory.EnumerateDirectories(Proc))
        {
            if (int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && Stat(pid) is { } stat)
            {
                processes[pid] = stat;
            }
        }

        return new ProcessTree(processes);
    }

    private static Entry? Stat(int pid)
    {
        try
        {
            var stat = File.ReadAllText(Path.Combine(Proc, pid.ToString(CultureInfo.InvariantCulture), "stat"));
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            var start = long.Parse(fields[StartField], CultureInfo.InvariantCulture);
            var cpu = long.Parse(fields[UserTimeField], CultureInfo.InvariantCulture) + long.Parse(fields[SystemTimeField], CultureInfo.InvariantCulture);
            return new Entry(
                int.Parse(fields[ParentField], CultureInfo.InvariantCulture),
                int.Parse(fields[GroupField], CultureInfo.InvariantCulture),
                start.ToString(CultureInfo.InvariantCulture),
                start,
                fields[StateField] is "Z" or "X",
                cpu);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ProcessTree FromPs()
    {
        var said = Ps(["-axo", "pid=,ppid=,pgid=,stat=,lstart="], out var self) ?? string.Empty;
        return FromPs(said, self);
    }

    /// <summary>
    /// The table as <c>ps</c> prints it, <paramref name="self"/>, the ps that printed it, left out: pid, parent, group,
    /// state and start, one process a line, the start as <c>Mon Nov  2 18:43:48 2026</c>. Processes are ordered by that
    /// start, to the second, never by pid, which wraps. Only the keeper's children need the order: claude is its oldest.
    /// Its other children are its own reads of the table, and the first comes more than a second after claude starts,
    /// since the keeper reads only once a second passed, so at one second's resolution each starts strictly later.
    /// </summary>
    internal static ProcessTree FromPs(string said, int self)
    {
        var processes = new Dictionary<int, Entry>();
        foreach (var line in said.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ', PsColumns + 1, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length == PsColumns + 1
                && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid != self
                && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent)
                && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var group)
                && DateTime.TryParseExact(string.Join(' ', fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)), StartFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var started))
            {
                processes[pid] = new Entry(parent, group, fields[4], started.Ticks, fields[3].StartsWith('Z'));
            }
        }

        return new ProcessTree(processes);
    }

    private static string? Ps(IReadOnlyList<string> arguments) => Ps(arguments, out _);

    private static string? Ps(IReadOnlyList<string> arguments, out int self)
    {
        self = 0;
        var start = new ProcessStartInfo("ps") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        // ps prints lstart in the user's LC_TIME, and a Dutch or German day or month would parse as no start, leaving the
        // tree empty and the keeper nothing to end. LC_ALL=C overrides LC_TIME and LANG, so the start is always English.
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var ps = Process.Start(start)!;
            self = ps.Id;
            var said = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
            return said;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// One process: its parent, its group, its start as a key, its start as a number to order by, whether it is a zombie,
    /// and the CPU time it used in clock ticks, or null where the table holds none.
    /// </summary>
    internal sealed record Entry(int Parent, int Group, string Start, long Ticks, bool Zombie, long? Cpu = null);
}
