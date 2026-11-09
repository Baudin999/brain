using System.Text.Json;

namespace Brain.Code;

/// <summary>
/// What brain code's window remembers across restarts: which kinds of the second flow it draws. It is kept in the data
/// repo's cache, on this machine only, since the window's port changes with each start and the browser keeps nothing
/// for it. A missing or unreadable file is every kind on.
/// </summary>
internal sealed class CodeSettings(string home)
{
    private readonly Lock gate = new();
    private readonly string path = Path.Combine(home, Layout.CacheDir, CodeServer.Folder, "settings.json");

    /// <summary>The saved choice, or every kind on.</summary>
    public CodeSettingsView Read()
    {
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return new CodeSettingsView();
            }

            try
            {
                return JsonSerializer.Deserialize(File.ReadAllText(path), CodeJson.Default.CodeSettingsView) ?? new CodeSettingsView();
            }
            catch (JsonException)
            {
                return new CodeSettingsView();
            }
        }
    }

    /// <summary>Keeps <paramref name="choice"/> and returns it.</summary>
    public CodeSettingsView Save(CodeSettingsView choice)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicFile.Write(path, JsonSerializer.Serialize(choice, CodeJson.Default.CodeSettingsView));
            return choice;
        }
    }
}
