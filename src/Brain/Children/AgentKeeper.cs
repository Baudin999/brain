using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Brain.Children;

/// <summary>
/// The keeper brain puts between itself and every claude it starts, through <see cref="ClaudeRuns"/>:
/// <c>brain keep-agent &lt;brain's pid&gt; &lt;program&gt; &lt;arguments&gt;</c>. It runs claude with its own input and
/// output, and ends everything claude started once claude ends, once it is told to stop, or once the brain process that
/// started it is gone however it went, a kill or a crash included: it watches its parent, and when that is no longer
/// the brain process that started it, it ends what it keeps and then itself. What it keeps is its <see cref="Lineage"/>.
/// On Linux it is the subreaper of everything below it, so a process claude starts stays below the keeper even once it
/// runs in the background or detaches from its parent, and nothing escapes. Without a subreaper, as on macOS, it leads
/// a process group of its own, which claude and what claude starts join, and it reads the process table every second
/// and remembers every process it saw below it. A process escapes only when, within one second of its start, its parent
/// ends and it leaves the keeper's group. brain runs it, never the user, so the command is in no help.
/// </summary>
internal static class AgentKeeper
{
    public const string Name = "keep-agent";

    /// <summary>The brain binary beside this one, which serves as the keeper.</summary>
    private const string Binary = "brain";

    /// <summary>How often the keeper looks whether the brain process that started it is still its parent.</summary>
    private static readonly TimeSpan Watch = TimeSpan.FromMilliseconds(200);

    /// <summary>How often the keeper reads the process table, to remember what is below it and reap the orphans that came to it and ended.</summary>
    private static readonly TimeSpan Reading = TimeSpan.FromSeconds(1);

    /// <summary>How long the keeper goes on ending what is below it while new processes still appear.</summary>
    private static readonly TimeSpan Ending = TimeSpan.FromSeconds(5);

    /// <summary>How the keeper starts <paramref name="program"/> under itself, with this process as the parent it watches.</summary>
    public static ProcessStartInfo Start(string program, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, Binary)) { UseShellExecute = false };
        info.ArgumentList.Add(Name);
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(program);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>The keeper's own run: the agent's exit code, or 1 when it could not start one.</summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2 || !int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var brain) || Posix.Parent() != brain)
        {
            // The brain process that called is gone already, or this was no call of brain's: nothing starts.
            return ExitCodes.Failure;
        }

        Posix.BecomeSubreaper();
        Posix.LeadOwnGroup();
        var lineage = new Lineage(Environment.ProcessId);
        using var stop = new ManualResetEventSlim();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Stopped(context, stop));
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Stopped(context, stop));
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => Stopped(context, stop));
        var info = new ProcessStartInfo(arguments[1]) { UseShellExecute = false };
        foreach (var argument in arguments.Skip(2))
        {
            info.ArgumentList.Add(argument);
        }

        Process agent;
        try
        {
            agent = Process.Start(info) ?? throw new Win32Exception();
        }
        catch (Win32Exception error)
        {
            Console.Error.WriteLine($"brain: {arguments[1]} would not start: {error.Message}");
            return ExitCodes.Failure;
        }

        using (agent)
        {
            var read = Stopwatch.StartNew();
            while (!stop.Wait(Watch) && !agent.HasExited && Posix.Parent() == brain)
            {
                if (read.Elapsed > Reading)
                {
                    var tree = ProcessTree.Read();
                    lineage.Remember(tree);
                    ReapOrphans(tree, agent.Id);
                    read.Restart();
                }
            }

            End(lineage);
            return agent.HasExited ? agent.ExitCode : ExitCodes.Failure;
        }
    }

    private static void Stopped(PosixSignalContext context, ManualResetEventSlim stop)
    {
        context.Cancel = true;
        stop.Set();
    }

    /// <summary>Reaps the orphans that came to the keeper and ended, so they wait as no zombie; the agent .NET reaps itself.</summary>
    private static void ReapOrphans(ProcessTree tree, int agent)
    {
        foreach (var zombie in tree.Zombies(Environment.ProcessId).Where(pid => pid != agent))
        {
            Posix.Reap(zombie);
        }
    }

    /// <summary>
    /// Kills everything the keeper keeps. A process killed lets its children go to the keeper, the subreaper, or on macOS
    /// to launchd, and a process may start another while it goes, so the keeper remembers what is below it before each
    /// round and looks again until nothing it keeps is left.
    /// </summary>
    private static void End(Lineage lineage)
    {
        var until = Stopwatch.StartNew();
        while (until.Elapsed < Ending)
        {
            var tree = ProcessTree.Read();
            lineage.Remember(tree);
            var left = lineage.Left(tree);
            if (left.Count == 0)
            {
                return;
            }

            foreach (var pid in left)
            {
                Posix.Signal(pid, Posix.SigKill);
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }
    }
}
