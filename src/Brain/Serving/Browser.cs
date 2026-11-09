using System.ComponentModel;
using System.Diagnostics;

namespace Brain.Serving;

/// <summary>
/// Opens brain's window, or a workspace's: a Chromium-family browser in app mode, with its own profile under the data
/// repo's cache, so the window runs as a process of its own with the window class <c>brain</c>, or the one a workspace
/// names, and shares nothing with the user's own browsing. <c>BRAIN_BROWSER</c> names the browser; otherwise the first one found on PATH, or in /Applications on macOS.
/// </summary>
internal static class Browser
{
    public const string Variable = "BRAIN_BROWSER";
    public const string WindowClass = "brain";
    private const string ProfileDir = "window";

    private static readonly string[] Names =
    [
        "chromium", "chromium-browser", "google-chrome-stable", "google-chrome", "brave-browser", "brave",
        "microsoft-edge-stable", "microsoft-edge", "vivaldi",
    ];

    private static readonly string[] MacApps =
    [
        "/Applications/Chromium.app/Contents/MacOS/Chromium",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
    ];

    /// <summary>The browser to open, or null when there is none.</summary>
    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } chosen)
        {
            return chosen;
        }

        var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return Names.SelectMany(name => path.Select(dir => Path.Combine(dir, name))).FirstOrDefault(File.Exists)
            ?? MacApps.FirstOrDefault(File.Exists);
    }

    /// <summary>Starts the browser on <paramref name="url"/>, its window of <paramref name="windowClass"/>; false when it would not start.</summary>
    public static bool Open(string browser, string url, string home, string windowClass = WindowClass)
    {
        var start = new ProcessStartInfo(browser) { UseShellExecute = false };
        foreach (var argument in new[]
                 {
                     "--app=" + url, "--class=" + windowClass, "--user-data-dir=" + Path.Combine(home, Layout.CacheDir, ProfileDir),
                     "--no-first-run", "--no-default-browser-check",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
