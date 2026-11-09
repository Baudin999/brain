using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Brain.Serving;

/// <summary>
/// The active Omarchy theme as a stylesheet over brain's window tokens, and a watch that follows a theme switch. Omarchy
/// keeps its active theme in <c>~/.local/state/omarchy/current</c>; brain reads only <c>theme/colors.toml</c> there, or
/// asks <c>omarchy-theme-color</c> about that file, and never writes into Omarchy. Without Omarchy the stylesheet is empty
/// and brain's own light and dark theme applies. Only <c>#rrggbb</c> colors and a light or dark mode reach the CSS.
/// </summary>
internal sealed class OmarchyTheme(string userHome, string? colorCommand)
{
    /// <summary>omarchy-theme-set replaces <c>theme/</c>, then writes this file, so its change means the theme is in place.</summary>
    public const string NameFile = "theme.name";

    public const string ColorCommand = "omarchy-theme-color";

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CommandLimit = TimeSpan.FromSeconds(2);

    /// <summary>Below this contrast with the background the theme's foreground is not read as text.</summary>
    private const double TextContrast = 4.5;

    /// <summary>Below this contrast with the background a focus outline is not seen, as WCAG 1.4.11 says of a non-text mark.</summary>
    private const double MarkContrast = 3;

    /// <summary>Below this contrast with the background a panel or a rule does not stand apart from it.</summary>
    private const double ApartContrast = 1.1;

    /// <summary>A red and a green closer than this in hue, or greyer than <see cref="MinSaturation"/>, are not told apart.</summary>
    private const double MinHueApart = 60;

    private const double MinSaturation = 0.25;

    /// <summary>Below this luminance a background gives more contrast to white than to black, so a project color lightens on it.</summary>
    private const double DarkGround = 0.179;

    private const int Wheel = 360;

    /// <summary>The window's syntax tokens and the theme color each takes, where that color reads on the background.</summary>
    private static readonly (string Token, string Color)[] SyntaxColors =
    [
        ("code-keyword", "magenta"),
        ("code-string", "green"),
        ("code-number", "yellow"),
        ("code-function", "blue"),
        ("code-type", "cyan"),
    ];

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>A monitor, not a <see cref="Lock"/>: <see cref="WaitPast"/> waits on it.</summary>
    private readonly object gate = new();
    private string css = string.Empty;
    private long version;

    public string Dir { get; } = Path.Combine(userHome, ".local", "state", "omarchy", "current");

    /// <summary>True when Omarchy keeps a current theme on this machine.</summary>
    public bool Present => Directory.Exists(Dir);

    /// <summary>The stylesheet for the active theme; empty without Omarchy.</summary>
    public string Css
    {
        get
        {
            lock (gate)
            {
                return css;
            }
        }
    }

    /// <summary>Counts the theme switches seen since <see cref="Watch"/> began.</summary>
    public long Version
    {
        get
        {
            lock (gate)
            {
                return version;
            }
        }
    }

    /// <summary><c>omarchy-theme-color</c> when it is on PATH, else null.</summary>
    public static string? FindCommand() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, ColorCommand))
            .FirstOrDefault(File.Exists);

    /// <summary>Reads the active theme and follows every switch until the returned watch is disposed.</summary>
    // navarone: 0000107
    public IDisposable Watch(TimeProvider time)
    {
        Reload();
        if (!Present)
        {
            return new Unwatched();
        }

        var debounce = time.CreateTimer(_ => Switched(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            return Follow(debounce);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // The machine is out of inotify watches, or Omarchy's directory went or shut: the window keeps the theme read
            // above and follows no switch, rather than the window server failing to start. No test reaches this: the
            // watch limit is the machine's, and a directory the user cannot read still takes a watch.
            debounce.Dispose();
            return new Unwatched();
        }
    }

    /// <summary>Watches <see cref="NameFile"/> and restarts the debounce on every change of it.</summary>
    private Watching Follow(ITimer debounce)
    {
        var watcher = new FileSystemWatcher(Dir, NameFile) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        try
        {
            watcher.Changed += (_, _) => debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            watcher.Created += (_, _) => debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            watcher.Renamed += (_, _) => debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            watcher.EnableRaisingEvents = true;
            return new Watching(watcher, debounce);
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    /// <summary>Waits until the theme switched past <paramref name="seen"/>, or <paramref name="timeout"/> passes; true on a switch.</summary>
    public bool WaitPast(long seen, TimeSpan timeout)
    {
        lock (gate)
        {
            return version != seen || (Monitor.Wait(gate, timeout) && version != seen);
        }
    }

    /// <summary>Ends every <see cref="WaitPast"/> now, with no switch, so each waiting stream looks again at its window.</summary>
    public void Wake()
    {
        lock (gate)
        {
            Monitor.PulseAll(gate);
        }
    }

    private void Switched()
    {
        Reload();
        lock (gate)
        {
            version++;
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>Reads the theme again. A theme caught halfway through its swap keeps the stylesheet it had.</summary>
    private void Reload()
    {
        try
        {
            var next = Present ? Stylesheet(Read()) : string.Empty;
            lock (gate)
            {
                css = next;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The next theme.name change reads it again.
        }
    }

    /// <summary>The theme's palette, resolved by omarchy-theme-color when it runs, else parsed from colors.toml here.</summary>
    private Dictionary<string, string> Read()
    {
        var file = Path.Combine(Dir, "theme", "colors.toml");
        if (!File.Exists(file))
        {
            return [];
        }

        if (colorCommand is not null && RunCommand(colorCommand, file) is { } resolved)
        {
            return Resolve(resolved, lightModeFile: false);
        }

        return Resolve(Parse(File.ReadAllText(file)), File.Exists(Path.Combine(Dir, "theme", "light.mode")));
    }

    private static Dictionary<string, string>? RunCommand(string command, string file)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("--file");
        start.ArgumentList.Add(file);
        start.ArgumentList.Add("--all");
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(CommandLimit))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? ParseLines(output.Result, '\t') : null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>A flat colors.toml: <c>key = "value"</c> lines; comments and anything else are skipped.</summary>
    public static Dictionary<string, string> Parse(string toml) => ParseLines(toml, '=');

    private static Dictionary<string, string> ParseLines(string text, char separator)
    {
        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var at = line.IndexOf(separator, StringComparison.Ordinal);
            if (at <= 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var value = line[(at + 1)..].Trim();
            if (value.Length > 0 && value[0] is '"' or '\'')
            {
                var end = value.IndexOf(value[0], 1);
                value = end > 0 ? value[1..end] : value[1..];
            }

            palette[line[..at].Trim().Trim('"', '\'')] = value.Trim();
        }

        return palette;
    }

    /// <summary>
    /// The keys brain uses, as omarchy-theme-color resolves them: older themes name <c>bg</c> and <c>fg</c> or only
    /// <c>color0..15</c>, and a theme without a mode is light when <c>light.mode</c> sits beside it or its background is light.
    /// </summary>
    // navarone: 0000136
    public static Dictionary<string, string> Resolve(Dictionary<string, string> raw, bool lightModeFile)
    {
        string? Get(params string[] keys) => keys.Select(key => raw.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrEmpty(value));
        var palette = new Dictionary<string, string>(StringComparer.Ordinal);
        void Put(string key, string? value)
        {
            if (value is not null)
            {
                palette[key] = value;
            }
        }

        Put("background", Get("background", "bg", "color0"));
        Put("foreground", Get("foreground", "fg", "color7"));
        Put("bright_foreground", Get("bright_foreground", "bright_fg", "color15") ?? palette.GetValueOrDefault("foreground"));
        Put("lighter_background", Get("lighter_background", "lighter_bg"));
        Put("dark_background", Get("dark_background", "dark_bg") ?? Mix(palette.GetValueOrDefault("background"), "#000000", 0.25));
        Put("muted", Get("muted", "color8", "dark_foreground", "dark_fg"));
        Put("selection", Get("selection", "selection_background", "color8", "color0") ?? palette.GetValueOrDefault("background"));
        Put("red", Get("red", "color1"));
        Put("green", Get("green", "color2"));
        Put("accent", Get("accent", "blue", "color4"));
        Put("blue", Get("blue", "color4"));
        Put("yellow", Get("yellow", "color3"));
        Put("magenta", Get("magenta", "color5"));
        Put("cyan", Get("cyan", "color6"));
        var mode = Get("mode", "theme_type");
        if (mode is not ("light" or "dark"))
        {
            mode = lightModeFile || (Rgb(palette.GetValueOrDefault("background")) is { } bg && bg.R + bg.G + bg.B > 382) ? "light" : "dark";
        }

        palette["mode"] = mode;
        return palette;
    }

    /// <summary>
    /// The stylesheet over brain's tokens. Background is the reading surface, dark_background the chrome, lighter_background
    /// raised panels and rules, muted strong rules only; a theme without them, or with them too close to the background, gets
    /// rules mixed from mostly background and a little text. Muted text is mixed from text and background, never the
    /// theme's muted. Text falls back to bright_foreground below 4.5:1, then to brain's own colors. Yes and no, and accent
    /// as text, fall back to brain's own below 4.5:1, and yes and no too when red and green are not told apart; the focus
    /// outline falls back below 3:1. Project colors take the theme's saturation and lightness from <see cref="OriginTone"/>,
    /// readable on paper, canvas and the selection a queue row is highlighted with.
    /// The native look holds either way: monospace chrome, square, no shadows.
    /// </summary>
    public static string Stylesheet(IReadOnlyDictionary<string, string> palette)
    {
        var dark = !string.Equals(palette.GetValueOrDefault("mode"), "light", StringComparison.Ordinal);
        var scheme = dark ? "dark" : "light";
        var bg = Color(palette, "background");
        var ink = new[] { Color(palette, "foreground"), Color(palette, "bright_foreground") }.FirstOrDefault(text => Contrast(text, bg) >= TextContrast);
        if (bg is null || ink is null)
        {
            return Sheet(scheme, []);
        }

        var canvas = Color(palette, "dark_background") ?? bg;
        var select = Color(palette, "selection") ?? Apart(palette, bg, "lighter_background") ?? Mix(bg, ink, 0.1)!;
        var tokens = SurfaceTokens(palette, bg, ink, canvas, select);
        tokens.AddRange(AccentTokens(palette, bg));
        tokens.AddRange(SyntaxTokens(palette, bg));
        tokens.AddRange(YesNoTokens(palette, bg));
        tokens.AddRange(OriginTokens(palette, [bg, canvas, select]));
        tokens.AddRange(Washes);
        return Sheet(scheme, tokens);
    }

    private static readonly (string Name, string Value)[] Washes =
    [
        ("yes-wash", "color-mix(in srgb, var(--yes) 16%, var(--paper))"),
        ("no-wash", "color-mix(in srgb, var(--no) 16%, var(--paper))"),
        ("stale-wash", "color-mix(in srgb, var(--stale) 16%, var(--paper))"),
    ];

    private static string Sheet(string scheme, List<(string Name, string Value)> themed)
    {
        var css = new StringBuilder("/* brain's window in the active Omarchy theme. */\n\n:root {\n");
        void Token(string name, string value) => css.Append(CultureInfo.InvariantCulture, $"  --{name}: {value};\n");
        Token("sans", "monospace, monospace");
        Token("mono", "monospace, monospace");
        Token("shadow", "none");
        css.Append(CultureInfo.InvariantCulture, $"  color-scheme: {scheme};\n");
        foreach (var (name, value) in themed)
        {
            Token(name, value);
        }

        css.Append("}\n\n*, *::before, *::after { border-radius: 0 !important; box-shadow: none !important; }\n\nkbd { border-bottom-width: 1px; }\n");
        return css.ToString();
    }

    private static string? Color(IReadOnlyDictionary<string, string> palette, string key) =>
        palette.TryGetValue(key, out var value) && Rgb(value) is not null ? value.ToLowerInvariant() : null;

    /// <summary>A palette color that stands apart from the background, or null.</summary>
    private static string? Apart(IReadOnlyDictionary<string, string> palette, string bg, string key) =>
        Color(palette, key) is { } color && Contrast(color, bg) >= ApartContrast ? color : null;

    /// <summary>The surfaces and the text: paper, canvas, raised panels, rules, the selection, ink and its softer mixes.</summary>
    private static List<(string Name, string Value)> SurfaceTokens(IReadOnlyDictionary<string, string> palette, string bg, string ink, string canvas, string select)
    {
        var lighter = Apart(palette, bg, "lighter_background");
        return
        [
            ("paper", bg),
            ("canvas", canvas),
            ("raised", lighter ?? bg),
            ("rule", lighter ?? Mix(bg, ink, 0.15)!),
            ("rule-strong", Apart(palette, bg, "muted") ?? Mix(bg, ink, 0.3)!),
            ("select", select),
            ("ink", ink),
            ("ink-soft", $"color-mix(in srgb, {ink} 70%, {bg})"),
            ("muted", $"color-mix(in srgb, {ink} 55%, {bg})"),
        ];
    }

    /// <summary>The focus outline, once the accent reads as a non-text mark, and the accent as text, once it reads as text.</summary>
    private static List<(string Name, string Value)> AccentTokens(IReadOnlyDictionary<string, string> palette, string bg)
    {
        var tokens = new List<(string Name, string Value)>();
        if (Color(palette, "accent") is not { } accent)
        {
            return tokens;
        }

        if (Contrast(accent, bg) >= MarkContrast)
        {
            tokens.Add(("focus", accent));
        }

        if (Contrast(accent, bg) >= TextContrast)
        {
            tokens.Add(("accent", accent));
        }

        return tokens;
    }

    /// <summary>Yes and no from the theme's green and red, when both read as text and are told apart.</summary>
    private static List<(string Name, string Value)> YesNoTokens(IReadOnlyDictionary<string, string> palette, string bg)
    {
        if (Color(palette, "red") is not { } red || Color(palette, "green") is not { } green)
        {
            return [];
        }

        var toldApart = ToldApart(red, green);
        var redReads = Contrast(red, bg) >= TextContrast;
        var greenReads = Contrast(green, bg) >= TextContrast;
        if (!toldApart || !redReads || !greenReads)
        {
            return [];
        }

        return [("yes", green), ("no", red)];
    }

    /// <summary>The project chips' saturation and lightness, readable on every ground.</summary>
    private static List<(string Name, string Value)> OriginTokens(IReadOnlyDictionary<string, string> palette, List<string> grounds)
    {
        var (saturation, lightness) = OriginTone(grounds, [.. new[] { Color(palette, "red"), Color(palette, "green"), Color(palette, "accent") }.OfType<string>()]);
        var tone = string.Create(CultureInfo.InvariantCulture, $"{saturation}% {lightness}%");
        return [("origin-light", tone), ("origin-dark", tone)];
    }

    /// <summary>The syntax tokens whose theme color reads on <paramref name="bg"/>; a token whose color does not keeps the stylebook's own.</summary>
    private static List<(string Name, string Value)> SyntaxTokens(IReadOnlyDictionary<string, string> palette, string bg)
    {
        var tokens = new List<(string Name, string Value)>();
        foreach (var (token, name) in SyntaxColors)
        {
            if (Color(palette, name) is { } color && Contrast(color, bg) >= TextContrast)
            {
                tokens.Add((token, color));
            }
        }

        return tokens;
    }

    /// <summary>
    /// The saturation and lightness, in whole percent, of every project's color, whose hue comes from the project's name:
    /// the mean of <paramref name="colors"/>, at least <see cref="MinSaturation"/> saturated, its lightness moved away from
    /// the backgrounds until every whole hue reads as text at 4.5:1 against each of them. Without colors it starts at 50%.
    /// </summary>
    public static (int Saturation, int Lightness) OriginTone(IReadOnlyList<string> backgrounds, IReadOnlyList<string> colors)
    {
        const int Half = 50;
        var grounds = backgrounds.Select(Rgb).OfType<(int R, int G, int B)>().ToList();
        var tones = colors.Select(Rgb).OfType<(int R, int G, int B)>().Select(Hsl).ToList();
        var saturation = Math.Max(tones.Count == 0 ? Half : Percent(tones.Average(tone => tone.Saturation)), Percent(MinSaturation));
        var lightness = tones.Count == 0 ? Half : Percent(tones.Average(tone => tone.Lightness));
        var step = grounds.Count > 0 && grounds.Average(Luminance) < DarkGround ? 1 : -1;
        bool Readable(int light) => Enumerable.Range(0, Wheel).All(hue => grounds.All(ground => Contrast(FromHsl(hue, saturation, light), ground) >= TextContrast));
        while (lightness is > 0 and < 100 && !Readable(lightness))
        {
            lightness += step;
        }

        return (saturation, lightness);
    }

    /// <summary>The color at <paramref name="hue"/> degrees and the given saturation and lightness in percent, as a browser renders <c>hsl()</c>.</summary>
    public static (int R, int G, int B) FromHsl(int hue, int saturation, int lightness)
    {
        double s = saturation / 100.0, l = lightness / 100.0;
        double Channel(int offset)
        {
            var k = (offset + (hue / 30.0)) % 12;
            return l - (s * Math.Min(l, 1 - l) * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1)));
        }

        int Byte(double channel) => (int)Math.Round(channel * 255, MidpointRounding.AwayFromZero);
        return (Byte(Channel(0)), Byte(Channel(8)), Byte(Channel(4)));
    }

    private static int Percent(double fraction) => (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);

    private static bool ToldApart(string red, string green)
    {
        var (redHue, redSaturation, _) = Hsl(Rgb(red)!.Value);
        var (greenHue, greenSaturation, _) = Hsl(Rgb(green)!.Value);
        var apart = Math.Abs(redHue - greenHue);
        return Math.Min(redSaturation, greenSaturation) >= MinSaturation && Math.Min(apart, 360 - apart) >= MinHueApart;
    }

    private static (double Hue, double Saturation, double Lightness) Hsl((int R, int G, int B) color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), span = max - min;
        var lightness = (max + min) / 2;
        if (span == 0)
        {
            return (0, 0, lightness);
        }

        var saturation = span / (1 - Math.Abs((2 * lightness) - 1));
        var hue = max == r ? 60 * (((g - b) / span) % 6) : max == g ? 60 * (((b - r) / span) + 2) : 60 * (((r - g) / span) + 4);
        return (hue < 0 ? hue + 360 : hue, saturation, lightness);
    }

    /// <summary>The WCAG contrast ratio of two colors; 0 when either is missing.</summary>
    public static double Contrast(string? one, string? other) =>
        Rgb(one) is { } a && Rgb(other) is { } b ? Contrast(a, b) : 0;

    private static double Contrast((int R, int G, int B) one, (int R, int G, int B) other)
    {
        double light = Luminance(one), dark = Luminance(other);
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    private static double Luminance((int R, int G, int B) color)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static string? Mix(string? from, string to, double amount)
    {
        if (Rgb(from) is not { } a || Rgb(to) is not { } b)
        {
            return null;
        }

        int Blend(int x, int y) => (int)Math.Round((x * (1 - amount)) + (y * amount), MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"#{Blend(a.R, b.R):x2}{Blend(a.G, b.G):x2}{Blend(a.B, b.B):x2}");
    }

    /// <summary>The channels of a <c>#rrggbb</c> color, or null for anything else.</summary>
    private static (int R, int G, int B)? Rgb(string? value)
    {
        if (value is not { Length: 7 } || value[0] != '#' || value.AsSpan(1).ContainsAnyExcept(HexDigits))
        {
            return null;
        }

        var hex = Convert.FromHexString(value.AsSpan(1));
        return (hex[0], hex[1], hex[2]);
    }

    private sealed class Unwatched : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class Watching(FileSystemWatcher watcher, ITimer debounce) : IDisposable
    {
        public void Dispose()
        {
            watcher.Dispose();
            debounce.Dispose();
        }
    }
}
