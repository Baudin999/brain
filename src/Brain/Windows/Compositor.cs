using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Brain.Windows;

/// <summary>
/// Brings a window forward through the compositor, by its exact title. Hyprland does it with hyprctl; on any other
/// desktop it says no, and the server opens a fresh window in place of the old one.
/// </summary>
internal static class Compositor
{
    private const string Signature = "HYPRLAND_INSTANCE_SIGNATURE";
    private const string Metacharacters = @"\^$.|?*+()[]{}";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    /// <summary>True when the compositor raised the window titled <paramref name="title"/>.</summary>
    public static bool Raise(string title)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Signature)))
        {
            return false;
        }

        var start = new ProcessStartInfo("hyprctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "dispatch", "focuswindow", "title:^" + Escape(title) + "$" })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var hyprctl = Process.Start(start);
            if (hyprctl is null)
            {
                return false;
            }

            var said = hyprctl.StandardOutput.ReadToEndAsync();
            if (!hyprctl.WaitForExit(Limit))
            {
                hyprctl.Kill();
                return false;
            }

            return hyprctl.ExitCode == ExitCodes.Ok && said.Result.Trim() == "ok";
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary><paramref name="title"/> as a regex that matches it exactly.</summary>
    public static string Escape(string title)
    {
        var escaped = new StringBuilder();
        foreach (var c in title)
        {
            if (Metacharacters.Contains(c, StringComparison.Ordinal))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
