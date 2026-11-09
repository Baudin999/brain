using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The Omarchy theme over brain's window tokens: which palette key becomes which token, the fallbacks for text that
/// does not read and for a red and a green that do not tell yes from no, the older palette names, and that nothing but a
/// <c>#rrggbb</c> color reaches the stylesheet.
/// </summary>
public sealed class OmarchyThemeTests : IDisposable
{
    internal const string Catppuccin = """
        mode = "dark"
        accent = "#89b4fa"
        selection = "#45475a"
        muted = "#585b70"
        background = "#1e1e2e"
        dark_background = "#161622"
        lighter_background = "#313244"
        foreground = "#cdd6f4"
        bright_foreground = "#cdd6f4"
        red = "#f38ba8"
        green = "#a6e3a1"
        """;

    private readonly TempBrain brain = new();

    public void Dispose() => brain.Dispose();

    [Fact]
    public void ADarkThemeMapsOntoBrainsTokens()
    {
        var css = Css(Catppuccin);

        Assert.Contains("color-scheme: dark;", css, StringComparison.Ordinal);
        Assert.Contains("--paper: #1e1e2e;", css, StringComparison.Ordinal);
        Assert.Contains("--canvas: #161622;", css, StringComparison.Ordinal);
        Assert.Contains("--raised: #313244;", css, StringComparison.Ordinal);
        Assert.Contains("--rule-strong: #585b70;", css, StringComparison.Ordinal);
        Assert.Contains("--select: #45475a;", css, StringComparison.Ordinal);
        Assert.Contains("--ink: #cdd6f4;", css, StringComparison.Ordinal);
        Assert.Contains("--muted: color-mix(in srgb, #cdd6f4 55%, #1e1e2e);", css, StringComparison.Ordinal);
        Assert.Contains("--accent: #89b4fa;", css, StringComparison.Ordinal);
        Assert.Contains("--yes: #a6e3a1;", css, StringComparison.Ordinal);
        Assert.Contains("--no: #f38ba8;", css, StringComparison.Ordinal);
        Assert.Contains("--sans: monospace, monospace;", css, StringComparison.Ordinal);
        Assert.Contains("border-radius: 0 !important", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderThemeWithShortNamesResolvesAndItsLightBackgroundMakesItLight()
    {
        var css = Css("color0 = \"#fafafa\"\ncolor7 = \"#202020\"\ncolor1 = \"#c03030\"\ncolor2 = \"#207020\"\ncolor4 = \"#3050c0\"\n");

        Assert.Contains("color-scheme: light;", css, StringComparison.Ordinal);
        Assert.Contains("--paper: #fafafa;", css, StringComparison.Ordinal);
        Assert.Contains("--canvas: #bcbcbc;", css, StringComparison.Ordinal);
        Assert.Contains("--raised: #fafafa;", css, StringComparison.Ordinal);
        Assert.Contains("--rule: #d9d9d9;", css, StringComparison.Ordinal);
        Assert.Contains("--rule-strong: #b9b9b9;", css, StringComparison.Ordinal);
        Assert.Contains("--accent: #3050c0;", css, StringComparison.Ordinal);
        Assert.Contains("--yes: #207020;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AGreyRedAndGreenLeaveYesAndNoToBrain()
    {
        var css = Css(Catppuccin.Replace("#f38ba8", "#a4a4a4", StringComparison.Ordinal).Replace("#a6e3a1", "#b6b6b6", StringComparison.Ordinal));

        Assert.DoesNotContain("--yes:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--no:", css, StringComparison.Ordinal);
        Assert.Contains("--yes-wash: color-mix(in srgb, var(--yes) 16%, var(--paper));", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AYesOrANoBelowTextContrastLeavesBothToBrain()
    {
        var css = Css(Catppuccin.Replace("green = \"#a6e3a1\"", "green = \"#3c8c3c\"", StringComparison.Ordinal));

        Assert.InRange(OmarchyTheme.Contrast("#3c8c3c", "#1e1e2e"), 3, 4.5);
        Assert.DoesNotContain("--yes:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--no:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAccentTheUserCannotSeeLeavesFocusToBrain()
    {
        var css = Css(Catppuccin.Replace("accent = \"#89b4fa\"", "accent = \"#303046\"", StringComparison.Ordinal));

        Assert.True(OmarchyTheme.Contrast("#303046", "#1e1e2e") < 3);
        Assert.DoesNotContain("--focus:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--accent:", css, StringComparison.Ordinal);
        Assert.Contains("--focus: #89b4fa;", Css(Catppuccin), StringComparison.Ordinal);
    }

    [Fact]
    public void TextThatDoesNotReadFallsBackToBrightForegroundThenToBrainsOwnColors()
    {
        var dim = Catppuccin.Replace("\nforeground = \"#cdd6f4\"", "\nforeground = \"#2a2a3a\"", StringComparison.Ordinal);
        var unreadable = dim.Replace("bright_foreground = \"#cdd6f4\"", "bright_foreground = \"#2a2a3a\"", StringComparison.Ordinal);

        Assert.Contains("--ink: #cdd6f4;", Css(dim), StringComparison.Ordinal);
        Assert.DoesNotContain("--paper:", Css(unreadable), StringComparison.Ordinal);
        Assert.Contains("--sans: monospace, monospace;", Css(unreadable), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingButAColorReachesTheStylesheet()
    {
        var css = Css(Catppuccin.Replace("accent = \"#89b4fa\"", "accent = \"#000; } body { display: none\"", StringComparison.Ordinal)
            .Replace("mode = \"dark\"", "mode = \"dark; } * { color: red\"", StringComparison.Ordinal));

        Assert.DoesNotContain("display", css, StringComparison.Ordinal);
        Assert.DoesNotContain("color: red", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--accent:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void AThemeWithOnlyNumberedColorsGivesCodeItsSyntaxColors()
    {
        var palette = OmarchyTheme.Resolve(OmarchyTheme.Parse("background = \"#1e1e2e\"\nforeground = \"#cdd6f4\"\ncolor3 = \"#f9e2af\"\ncolor4 = \"#89b4fa\"\ncolor5 = \"#cba6f7\"\ncolor6 = \"#94e2d5\"\n"), lightModeFile: false);

        Assert.Equal("#f9e2af", palette["yellow"]);
        Assert.Equal("#89b4fa", palette["blue"]);
        Assert.Equal("#cba6f7", palette["magenta"]);
        Assert.Equal("#94e2d5", palette["cyan"]);
    }

    [Fact]
    public void AThemeColorThatReadsBecomesACodeToken()
    {
        var css = Css(Catppuccin + "\nmagenta = \"#cba6f7\"\n");

        Assert.Contains("--code-keyword: #cba6f7;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ASyntaxColorThatDoesNotReadOnTheBackgroundKeepsTheStylebooksOwn()
    {
        var css = Css(Catppuccin + "\nmagenta = \"#2a2a3a\"\n");

        Assert.DoesNotContain("--code-keyword:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutOmarchyTheStylesheetIsEmpty()
    {
        var theme = new OmarchyTheme(brain.Root, colorCommand: null);
        using var watching = theme.Watch(TimeProvider.System);

        Assert.False(theme.Present);
        Assert.Equal(string.Empty, theme.Css);
    }

    [Fact]
    public void OmarchyThemeColorResolvesThePaletteWhenItRuns()
    {
        WriteTheme(brain.Root, Catppuccin);
        var command = FakeCommand("printf 'background\\t#101010\\nforeground\\t#f0f0f0\\nmode\\tdark\\n'");
        var theme = new OmarchyTheme(brain.Root, command);
        using var watching = theme.Watch(TimeProvider.System);

        Assert.Contains("--paper: #101010;", theme.Css, StringComparison.Ordinal);
        Assert.Contains("--ink: #f0f0f0;", theme.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailingOmarchyThemeColorFallsBackToColorsToml()
    {
        WriteTheme(brain.Root, Catppuccin);
        var command = FakeCommand("exit 1");
        var theme = new OmarchyTheme(brain.Root, command);
        using var watching = theme.Watch(TimeProvider.System);

        Assert.Contains("--paper: #1e1e2e;", theme.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void TheThemeIsReadFromTheCurrentThemeDirectory()
    {
        WriteTheme(brain.Root, Catppuccin);
        var theme = new OmarchyTheme(brain.Root, colorCommand: null);
        using var watching = theme.Watch(TimeProvider.System);

        Assert.True(theme.Present);
        Assert.Contains("--paper: #1e1e2e;", theme.Css, StringComparison.Ordinal);
    }

    /// <summary>Puts a theme in place as omarchy-theme-set does: the theme directory swapped whole, then theme.name.</summary>
    internal static void WriteTheme(string userHome, string colors, string name = "catppuccin")
    {
        var current = Path.Combine(userHome, ".local", "state", "omarchy", "current");
        var next = Path.Combine(current, "next-theme");
        Directory.CreateDirectory(next);
        File.WriteAllText(Path.Combine(next, "colors.toml"), colors);
        var theme = Path.Combine(current, "theme");
        if (Directory.Exists(theme))
        {
            Directory.Delete(theme, recursive: true);
        }

        Directory.Move(next, theme);
        File.WriteAllText(Path.Combine(current, OmarchyTheme.NameFile), name + "\n");
    }

    /// <summary>A stand-in for omarchy-theme-color that runs <paramref name="body"/> as a shell script.</summary>
    private string FakeCommand(string body)
    {
        var command = Path.Combine(brain.Root, "omarchy-theme-color");
        File.WriteAllText(command, "#!/bin/sh\n" + body + "\n");
        Chmod(command, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return command;
    }

    /// <summary>Sets a file's mode; the window runs on Linux, macOS and WSL only.</summary>
    internal static void Chmod(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("the window runs on Linux, macOS and WSL");
        }

        File.SetUnixFileMode(path, mode);
    }

    private static string Css(string colors) => OmarchyTheme.Stylesheet(OmarchyTheme.Resolve(OmarchyTheme.Parse(colors), lightModeFile: false));
}
