using System.Runtime.InteropServices;

namespace Brain.Children;

/// <summary>The few calls into libc that the agent keeper needs and .NET does not offer: the subreaper, a group of its own, the parent's pid, a signal and a reap.</summary>
internal static partial class Posix
{
    public const int SigKill = 9;
    public const int SigTerm = 15;
    private const string Libc = "libc";
    private const int SetChildSubreaper = 36;
    private const int NoHang = 1;

    /// <summary>Makes this process the subreaper of everything below it, Linux only: an orphan below it becomes its child, not init's.</summary>
    public static bool BecomeSubreaper() => OperatingSystem.IsLinux() && Prctl(SetChildSubreaper, 1, 0, 0, 0) == 0;

    /// <summary>Makes this process the leader of a process group of its own, which every process it starts joins unless it leaves.</summary>
    public static bool LeadOwnGroup() => SetGroup(0, 0) == 0;

    public static int Parent() => GetParentId();

    /// <summary>Sends <paramref name="signal"/> to <paramref name="pid"/>; false when it is gone.</summary>
    public static bool Signal(int pid, int signal) => Kill(pid, signal) == 0;

    /// <summary>Reaps <paramref name="pid"/> when it is a child of this process that ended, so no zombie stays behind.</summary>
    public static void Reap(int pid) => _ = WaitPid(pid, out _, NoHang);

    [LibraryImport(Libc, EntryPoint = "prctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [LibraryImport(Libc, EntryPoint = "setpgid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int SetGroup(int pid, int group);

    [LibraryImport(Libc, EntryPoint = "getppid")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int GetParentId();

    [LibraryImport(Libc, EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Kill(int pid, int signal);

    [LibraryImport(Libc, EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int WaitPid(int pid, out int status, int options);
}
