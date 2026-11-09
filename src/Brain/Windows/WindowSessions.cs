using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Brain.Windows;

/// <summary>
/// Who may use a small window server's API, and what its windows hear. The server opens a window with a launch code,
/// which only a launch file of the user's holds; the window trades it once, within a minute, for a session token it
/// sends with every call. A server has one window: a window that trades its code closes the one before it. Each window
/// listens on an event stream, which hears what its server publishes, a theme switch, and close.
/// </summary>
internal sealed class WindowSessions(TimeProvider time)
{
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What a stream hears last, which is no event, since an event ends in a blank line: the stream ends, for its window
    /// said goodbye or was closed.
    /// </summary>
    public const string End = "end";

    public const string Close = "event: close\ndata: close\n\n";

    private const int SecretBytes = 32;

    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BlockingCollection<string>>> streams = new(StringComparer.Ordinal);

    /// <summary>A fresh launch code for a new window.</summary>
    public string Issue()
    {
        var code = Secret();
        lock (gate)
        {
            codes[Hash(code)] = time.GetUtcNow();
        }

        return code;
    }

    /// <summary>Trades a launch code for a session token, once and within its life, and closes every window before it; null refuses.</summary>
    public string? Exchange(string code)
    {
        lock (gate)
        {
            if (!codes.Remove(Hash(code), out var issued) || time.GetUtcNow() - issued > CodeLife)
            {
                return null;
            }

            foreach (var old in sessions)
            {
                Tell(old, Close);
                Tell(old, End);
            }

            sessions.Clear();
            var token = Secret();
            sessions.Add(Hash(token));
            return token;
        }
    }

    public bool Knows([NotNullWhen(true)] string? token)
    {
        if (token is null)
        {
            return false;
        }

        lock (gate)
        {
            return sessions.Contains(Hash(token));
        }
    }

    /// <summary>A stream for the window of <paramref name="token"/> to hear its events on.</summary>
    public BlockingCollection<string> Listen(string token)
    {
        var stream = new BlockingCollection<string>();
        lock (gate)
        {
            var hash = Hash(token);
            if (!streams.TryGetValue(hash, out var own))
            {
                own = [];
                streams[hash] = own;
            }

            own.Add(stream);
        }

        return stream;
    }

    /// <summary>Ends <paramref name="stream"/> and returns how many streams are still open, of every window.</summary>
    public int Leave(string token, BlockingCollection<string> stream)
    {
        lock (gate)
        {
            var hash = Hash(token);
            if (streams.TryGetValue(hash, out var own))
            {
                own.Remove(stream);
                if (own.Count == 0)
                {
                    streams.Remove(hash);
                }
            }

            stream.Dispose();
            return streams.Values.Sum((one) => one.Count);
        }
    }

    /// <summary>Says <paramref name="text"/> on every stream.</summary>
    public void Publish(string text)
    {
        lock (gate)
        {
            foreach (var stream in streams.Values.SelectMany((own) => own))
            {
                stream.Add(text);
            }
        }
    }

    /// <summary>Ends the streams of the window of <paramref name="token"/>, which said goodbye.</summary>
    public void Goodbye(string token)
    {
        lock (gate)
        {
            Tell(Hash(token), End);
        }
    }

    /// <summary>Says <paramref name="text"/> on each stream of the session whose hash is <paramref name="hash"/>.</summary>
    private void Tell(string hash, string text)
    {
        if (!streams.TryGetValue(hash, out var own))
        {
            return;
        }

        foreach (var stream in own)
        {
            stream.Add(text);
        }
    }

    private static string Secret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));

    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
