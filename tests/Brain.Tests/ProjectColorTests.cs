using System.Globalization;
using System.Text.RegularExpressions;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// A project's color in brain's window: its hue comes from its name alone, and the theme sets its saturation and
/// lightness so that every hue reads as text against the window's backgrounds, in an Omarchy theme and in brain's own.
/// </summary>
public sealed partial class ProjectColorTests
{
    private const double TextContrast = 4.5;

    [Fact]
    public void AHueComesFromTheNameAlone()
    {
        Assert.Equal(ReviewViews.Hue("CkLang-Bootstrap"), ReviewViews.Hue("CkLang-Bootstrap"));
        Assert.InRange(ReviewViews.Hue("brain"), 0, 359);
        Assert.NotEqual(ReviewViews.Hue("brain"), ReviewViews.Hue("afterlife"));
    }

    [Theory]
    [InlineData("#1e1e2e", "#161622", "#f38ba8", "#a6e3a1", "#89b4fa")]
    [InlineData("#fafafa", "#bcbcbc", "#c03030", "#207020", "#3050c0")]
    [InlineData("#000000", "#000000", "#ff0000", "#00ff00", "#0000ff")]
    [InlineData("#ffffff", "#ffffff", "#ffff00", "#00ffff", "#ff00ff")]
    public void EveryHueReadsAgainstEveryBackground(string paper, string canvas, string red, string green, string accent)
    {
        var (saturation, lightness) = OmarchyTheme.OriginTone([paper, canvas], [red, green, accent]);

        AssertReadable([paper, canvas], saturation, lightness);
    }

    [Fact]
    public void AGreyThemeStillGivesProjectsTheirHues()
    {
        var (saturation, _) = OmarchyTheme.OriginTone(["#202020"], ["#808080", "#909090"]);

        Assert.Equal(25, saturation);
    }

    [Fact]
    public void AnOmarchyThemeSetsTheToneForBothModes()
    {
        var css = OmarchyTheme.Stylesheet(OmarchyTheme.Resolve(OmarchyTheme.Parse(OmarchyThemeTests.Catppuccin), lightModeFile: false));
        var (saturation, lightness) = OmarchyTheme.OriginTone(["#1e1e2e", "#161622", "#45475a"], ["#f38ba8", "#a6e3a1", "#89b4fa"]);

        AssertReadable(["#1e1e2e", "#161622", "#45475a"], saturation, lightness);
        Assert.Contains($"--origin-light: {saturation}% {lightness}%;", css, StringComparison.Ordinal);
        Assert.Contains($"--origin-dark: {saturation}% {lightness}%;", css, StringComparison.Ordinal);
    }

    /// <summary>brain's own light and dark tone in tokens.css is what OriginTone makes of brain's own colors.</summary>
    [Theory]
    [InlineData("light", 0)]
    [InlineData("dark", 1)]
    public void BrainsOwnThemeTakesItsToneTheSameWay(string mode, int side)
    {
        var css = WindowResources.Read(WindowResources.Tokens);
        string Own(string token) => LightDark().Match(Token(css, token)).Groups[side + 1].Value;
        var expected = OmarchyTheme.OriginTone([Own("paper"), Own("canvas"), Own("select")], [Own("yes"), Own("no"), Own("focus"), Own("stale")]);

        Assert.Equal($"{expected.Saturation}% {expected.Lightness}%", Token(css, "origin-" + mode));
    }

    private static void AssertReadable(IReadOnlyList<string> backgrounds, int saturation, int lightness)
    {
        for (var hue = 0; hue < 360; hue++)
        {
            var (r, g, b) = OmarchyTheme.FromHsl(hue, saturation, lightness);
            var color = string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}");
            foreach (var background in backgrounds)
            {
                Assert.True(OmarchyTheme.Contrast(color, background) >= TextContrast, $"hue {hue} at {saturation}% {lightness}% is {color}, under 4.5:1 on {background}");
            }
        }
    }

    private static string Token(string css, string name) =>
        Regex.Match(css, $@"--{Regex.Escape(name)}:\s*([^;]+);", RegexOptions.CultureInvariant).Groups[1].Value.Trim();

    [GeneratedRegex(@"light-dark\((#[0-9a-f]{6}),\s*(#[0-9a-f]{6})\)", RegexOptions.CultureInvariant)]
    private static partial Regex LightDark();
}
