using System.Runtime.InteropServices;

namespace Brain.Serving;

/// <summary>
/// Runs one of brain's servers in the foreground, brain serve or a small window's, until it ends by itself or an
/// interrupt or a terminate signal stops it. A hangup does not: a server started from a terminal outlives the terminal.
/// </summary>
internal static class Foreground
{
    public static void Run(Func<CancellationToken, Task> serve)
    {
        using var stop = new CancellationTokenSource();
        using var hangup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => context.Cancel = true);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => Stop(context, stop));
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => Stop(context, stop));
        serve(stop.Token).GetAwaiter().GetResult();
    }

    private static void Stop(PosixSignalContext context, CancellationTokenSource stop)
    {
        context.Cancel = true;
        stop.Cancel();
    }
}
