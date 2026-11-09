using System.Security.Cryptography;
using System.Text;

namespace Brain.Serving;

/// <summary>
/// Who may use brain's window API. <c>brain serve</c> opens each window with a launch code, which only a launch file of
/// the user's holds. The window trades the code once for a session token it keeps in memory only, and sends the token in
/// a header with every call. A code works once, within a minute. A second try at a spent code closes every session and
/// the window says so, and a new session can answer only once it is <see cref="Arming"/> old. That catches a careless
/// race for a code; a process of the user's own account can read the launch file, so it is no protection against one.
/// There is one window: a window that trades its code closes every other, and <see cref="CloseAll"/> closes them all.
/// </summary>
internal sealed class WindowKeys(TimeProvider time)
{
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Arming = TimeSpan.FromSeconds(2);

    private const int SecretBytes = 32;

    private readonly Lock gate = new();
    private readonly Dictionary<string, DateTimeOffset> codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> spent = new(StringComparer.Ordinal);

    /// <summary>Each open session's token hash, and when it may answer.</summary>
    private readonly Dictionary<string, DateTimeOffset> sessions = new(StringComparer.Ordinal);

    /// <summary>The token hashes of sessions closed so their windows close, as against those an alarm ended.</summary>
    private readonly HashSet<string> closed = new(StringComparer.Ordinal);

    private bool alarmed;

    /// <summary>True since a spent code was tried, until a window opens with a fresh one.</summary>
    public bool Alarmed
    {
        get
        {
            lock (gate)
            {
                return alarmed;
            }
        }
    }

    /// <summary>A fresh launch code for a new window. Every code not yet used is retired: one window opens at a time.</summary>
    public string Issue()
    {
        lock (gate)
        {
            spent.UnionWith(codes.Keys.Select(Hash));
            codes.Clear();
            var code = Secret();
            codes[Hash(code)] = time.GetUtcNow();
            return code;
        }
    }

    /// <summary>
    /// Trades a launch code for a session token, once, and closes every other session, so the window it opens is the only
    /// one. A spent code closes every session with an alarm; null refuses.
    /// </summary>
    public string? Exchange(string code)
    {
        var key = Hash(code);
        lock (gate)
        {
            if (codes.Remove(key, out var issued))
            {
                spent.Add(key);
                if (time.GetUtcNow() - issued > CodeLife)
                {
                    return null;
                }

                CloseSessions();
                var token = Secret();
                sessions[Hash(token)] = time.GetUtcNow() + Arming;
                alarmed = false;
                return token;
            }

            if (spent.Contains(key))
            {
                alarmed = true;
                sessions.Clear();
            }

            return null;
        }
    }

    /// <summary>Closes every session, so each window closes itself; brain serve does this before it stops.</summary>
    public void CloseAll()
    {
        lock (gate)
        {
            CloseSessions();
        }
    }

    /// <summary>True for a token of an open session.</summary>
    public bool Knows(string? token)
    {
        if (token is null)
        {
            return false;
        }

        lock (gate)
        {
            return sessions.ContainsKey(Hash(token));
        }
    }

    /// <summary>True for a token whose session was closed so that its window closes.</summary>
    public bool Closed(string token)
    {
        lock (gate)
        {
            return closed.Contains(Hash(token));
        }
    }

    /// <summary>True once the token's session may answer a write.</summary>
    public bool Armed(string token)
    {
        lock (gate)
        {
            return sessions.TryGetValue(Hash(token), out var armed) && time.GetUtcNow() >= armed;
        }
    }

    private void CloseSessions()
    {
        closed.UnionWith(sessions.Keys);
        sessions.Clear();
    }

    private static string Secret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>Codes and tokens are kept by hash, so a lookup takes no time that depends on how much of a guess matched.</summary>
    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
