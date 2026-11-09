using System.Text.Json;
using Brain.Serving;

namespace Brain.Code;

/// <summary>
/// What a reload hands brain code's successor, in <see cref="FileName"/> among the server's own files, which only the
/// user's account can read: the old server writes it before it stops, and the server that starts next takes it, once.
/// </summary>
internal static class Handover
{
    public const string FileName = "handover.json";

    public static void Write(ServeFiles files, CodeHandover handover) =>
        files.Write(FileName, JsonSerializer.Serialize(handover, CodeJson.Default.CodeHandover));

    /// <summary>The handover a reload left, which is gone from then on; null when none waits or it cannot be read.</summary>
    public static CodeHandover? Take(ServeFiles files)
    {
        var text = files.Read(FileName);
        if (text is null)
        {
            return null;
        }

        File.Delete(Path.Combine(files.Dir, FileName));
        try
        {
            return JsonSerializer.Deserialize(text, CodeJson.Default.CodeHandover);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
