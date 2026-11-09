using System.Globalization;

namespace Brain;

/// <summary>
/// Writes a file whole: the text goes to a temp file beside it, named for this process and this write, which then
/// replaces the file in one rename. A reader sees the old file or the new one, never half of either, and two writers
/// never share a temp file.
/// </summary>
internal static class AtomicFile
{
    private const string TempSuffix = ".tmp";

    public static void Write(string path, string text)
    {
        var temp = TempFor(path);
        try
        {
            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>A temp file beside <paramref name="path"/>, named for this process and this write, for a rename into place.</summary>
    public static string TempFor(string path) =>
        string.Join('.', path, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)) + TempSuffix;
}
