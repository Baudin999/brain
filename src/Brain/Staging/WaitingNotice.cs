using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Brain.Staging;

/// <summary>
/// The one desktop notification that says brain waits on the user's yes and how many writes wait. Every waiting
/// <c>brain land</c> calls <see cref="Tick"/>; the one that holds <c>cache/pending/notice.lock</c> owns the notification
/// and shows it through <c>notify-send</c>, replacing its own id so it updates in place. When the owner is answered and
/// exits, another waiting <c>brain land</c> takes the lock and carries on. The notification closes once no write waits:
/// the owner closes it as it leaves, and <see cref="Settle"/>, which <c>brain serve</c> calls after an answer, closes it
/// when the owner was killed. A click runs <c>brain ui</c>, or the command in <c>BRAIN_NOTIFY_CLICK</c> through
/// <c>sh -c</c>, with no stream of its own. Without <c>notify-send</c>, or with <c>BRAIN_NOTIFY_SEND=off</c>, nothing
/// shows and nothing fails. The text says how many writes wait and never what they say.
/// </summary>
internal sealed class WaitingNotice : IDisposable
{
    public const string SendVariable = "BRAIN_NOTIFY_SEND";
    public const string ClickVariable = "BRAIN_NOTIFY_CLICK";
    public const string Off = "off";
    public const string CloseTool = "gdbus";
    private const string DefaultSend = "notify-send";
    private const string LockFile = "notice.lock";
    private const string IdFile = "notice.id";
    private const string ClickAction = "default";
    private const string Silent = "{ %s\n} </dev/null >/dev/null 2>&1";
    private const int Tidy = 1;
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdWait = TimeSpan.FromSeconds(3);

    private readonly PendingStore store;
    private readonly string send;
    private readonly string? click;
    private readonly string closer;
    private readonly TimeSpan every;
    private readonly object gate = new();
    private readonly List<PosixSignalRegistration> signals = [];
    private FileStream? held;
    private Process? shown;
    private int shownCount = -1;
    private bool available;
    private DateTime lastTick = DateTime.MinValue;

    public WaitingNotice(PendingStore store, string send, string? click, string closer = CloseTool, TimeSpan? every = null)
    {
        this.store = store;
        this.send = send;
        this.click = click;
        this.closer = closer;
        this.every = every ?? Every;
        available = !string.Equals(send, Off, StringComparison.Ordinal);
    }

    /// <summary>The notice the process environment asks for.</summary>
    public static WaitingNotice FromEnvironment(PendingStore store) => new(
        store,
        Environment.GetEnvironmentVariable(SendVariable) is { Length: > 0 } send ? send : DefaultSend,
        Environment.GetEnvironmentVariable(ClickVariable) is { Length: > 0 } click ? click : null);

    /// <summary>Brings the notification in line with the writes before the user, at most once a second.</summary>
    public void Tick()
    {
        lock (gate)
        {
            if (!available || DateTime.UtcNow - lastTick < every)
            {
                return;
            }

            lastTick = DateTime.UtcNow;
            try
            {
                if (Own())
                {
                    Show(store.Asked().Count);
                }
            }
            catch (Win32Exception)
            {
                available = false;
                Release();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                shownCount = -1;
            }
        }
    }

    /// <summary>
    /// Closes the notification when no write waits and no <c>brain land</c> owns it any more, which is what a killed owner
    /// leaves behind. Does nothing while an owner lives: it closes the notification itself.
    /// </summary>
    public void Settle()
    {
        lock (gate)
        {
            if (!available)
            {
                return;
            }

            try
            {
                if (Own() && store.Asked().Count == 0)
                {
                    Close();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            {
                // The notification is a courtesy; a failure leaves nothing to fix.
            }
            finally
            {
                Release();
            }
        }
    }

    /// <summary>The owner leaves: it stops listening for the click, and closes the notification when no write waits.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (held is null)
            {
                return;
            }

            try
            {
                Retire();
                if (store.Asked().Count == 0)
                {
                    Close();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
            {
                // The notification is a courtesy; a failure to close it leaves nothing to fix.
            }
            finally
            {
                Release();
            }
        }
    }

    private bool Own()
    {
        if (held is not null)
        {
            return true;
        }

        Directory.CreateDirectory(store.Root);
        try
        {
            held = new FileStream(Path.Combine(store.Root, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return false;
        }

        shownCount = -1;
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP })
        {
            try
            {
                signals.Add(PosixSignalRegistration.Create(signal, _ => Dispose()));
            }
            catch (PlatformNotSupportedException)
            {
                // The platform has no such signal; the others still clean up.
            }
        }

        return true;
    }

    private void Release()
    {
        foreach (var signal in signals)
        {
            signal.Dispose();
        }

        signals.Clear();
        held?.Dispose();
        held = null;
    }

    private void Show(int count)
    {
        if (count == shownCount)
        {
            return;
        }

        if (count == 0)
        {
            Retire();
            Close();
            shownCount = 0;
            return;
        }

        var start = new ProcessStartInfo(send) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var argument in new[] { "--app-name=brain", "--print-id", "--action=" + ClickAction + "=Show brain", "--wait" })
        {
            start.ArgumentList.Add(argument);
        }

        if (Id() is { } id)
        {
            start.ArgumentList.Add("--replace-id=" + id);
        }

        start.ArgumentList.Add("brain is waiting on your yes");
        start.ArgumentList.Add(count == 1 ? "1 write waits for you in brain's window" : $"{count.ToString(CultureInfo.InvariantCulture)} writes wait for you in brain's window");
        var next = Process.Start(start);
        if (next is null)
        {
            return;
        }

        var output = next.StandardOutput;
        var first = output.ReadLineAsync();
        if (!first.Wait(IdWait) || !uint.TryParse(first.Result, CultureInfo.InvariantCulture, out _))
        {
            // No daemon answers: count this as shown, so the next tick does not start another notify-send.
            shownCount = count;
            Kill(next);
            return;
        }

        File.WriteAllText(Path.Combine(store.Root, IdFile), first.Result);
        Retire();
        shown = next;
        shownCount = count;
        _ = Task.Run(() => Listen(output));
    }

    /// <summary>Reads what the notification reports after its id, until it ends: the click.</summary>
    private void Listen(StreamReader output)
    {
        try
        {
            while (output.ReadLine() is { } line)
            {
                if (string.Equals(line, ClickAction, StringComparison.Ordinal))
                {
                    Click();
                }
            }
        }
        catch (Exception error) when (error is ObjectDisposedException or IOException)
        {
            // The notification was replaced or closed and its process is gone: nobody listens to it any more.
        }
    }

    private void Click()
    {
        var start = new ProcessStartInfo("sh") { UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(click is null ? Silent.Replace("%s", "\"$1\" ui", StringComparison.Ordinal) : Silent.Replace("%s", click, StringComparison.Ordinal));
        start.ArgumentList.Add("sh");
        start.ArgumentList.Add(Environment.ProcessPath ?? "brain");
        try
        {
            Process.Start(start)?.Dispose();
        }
        catch (Win32Exception)
        {
            // No shell, no click: the window stays where it is.
        }
    }

    private void Retire()
    {
        if (shown is not null)
        {
            Kill(shown);
            shown = null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // It already exited.
        }

        process.Dispose();
    }

    /// <summary>Closes the notification by its id over D-Bus; without gdbus, replaces it with one that expires at once.</summary>
    private void Close()
    {
        if (Id() is not { } id)
        {
            return;
        }

        File.Delete(Path.Combine(store.Root, IdFile));
        try
        {
            using var closing = Process.Start(new ProcessStartInfo(closer, [
                "call", "--session", "--dest", "org.freedesktop.Notifications", "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.CloseNotification", id])
            { UseShellExecute = false, RedirectStandardOutput = true });
            closing?.WaitForExit();
        }
        catch (Win32Exception)
        {
            using var expire = Process.Start(new ProcessStartInfo(send, ["--transient", $"--expire-time={Tidy}", "--replace-id=" + id, "brain", " "]) { UseShellExecute = false });
            expire?.WaitForExit();
        }
    }

    private string? Id()
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(store.Root, IdFile)).Trim();
            return uint.TryParse(text, CultureInfo.InvariantCulture, out _) ? text : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
