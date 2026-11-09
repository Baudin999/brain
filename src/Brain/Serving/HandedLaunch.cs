using System.Globalization;
using System.Security.Cryptography;

namespace Brain.Serving;

/// <summary>
/// The launch code a window server hands its successor, so the window already open moves onto the new build and no
/// second window opens. The old server writes it in <see cref="FileName"/> among its own files, which only the user's
/// account can read, and tells its window the successor's launch URL on that window's own event stream. The successor
/// takes the code once as it starts and admits it in place of opening a window. A code older than <see cref="Life"/>,
/// left by a successor that never started, is no handover: the server that finds it opens its own window.
/// </summary>
internal static class HandedLaunch
{
    public const string FileName = "handed-launch";
    public static readonly TimeSpan Life = TimeSpan.FromMinutes(1);
    private const int SecretBytes = 32;

    /// <summary>Writes a fresh launch code for the successor, with when it was handed, and returns it.</summary>
    public static string Hand(ServeFiles files, TimeProvider time)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var at = time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        files.Write(FileName, at + " " + code);
        return code;
    }

    /// <summary>The code a predecessor handed, which is gone from then on; null when none waits, or it is past its life.</summary>
    public static string? Take(ServeFiles files, TimeProvider time)
    {
        var text = files.Read(FileName);
        if (text is null)
        {
            return null;
        }

        File.Delete(Path.Combine(files.Dir, FileName));
        var parts = text.Split(' ');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var at))
        {
            return null;
        }

        var handed = DateTimeOffset.FromUnixTimeMilliseconds(at);
        if (time.GetUtcNow() - handed > Life)
        {
            return null;
        }

        return parts[1];
    }
}
