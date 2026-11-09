using System.Text.RegularExpressions;

namespace Brain.Tests;

/// <summary>
/// The stylebook as a gate: tokens.css is the one place a color, a font size or a radius is written as a value. Every
/// other sheet brain's window or a workspace's serves names a token for each, and so does every style their scripts set. A lit-html
/// template sets no style attribute at all. Home's sky, drawn on a canvas, sets no style and is not read here.
/// </summary>
public sealed partial class StyleTokenTests
{
    [Fact]
    public void EverySheetButTheTokensNamesATokenForEachColorSizeAndRadius()
    {
        var sheets = WindowResources.Named(".css").Where((name) => name != WindowResources.Tokens).ToList();

        var problems = sheets.SelectMany((name) => SheetProblems(name, WindowResources.Read(name))).ToList();

        Assert.True(sheets.Count >= 5, $"only {sheets.Count} sheets were read");
        Assert.Contains("workspace/workspace.css", sheets);
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void EveryStyleAScriptSetsNamesATokenForEachColorSizeAndRadius()
    {
        var scripts = WindowResources.Named(".js").ToList();

        var problems = scripts.SelectMany((name) => ScriptProblems(name, WindowResources.Read(name))).ToList();

        Assert.True(scripts.Count >= 5, $"only {scripts.Count} scripts were read");
        Assert.Contains("workspace/core.js", scripts);
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void TheTokensDefineTheScales()
    {
        var css = WindowResources.Read(WindowResources.Tokens);

        foreach (var token in new[] { "--text-1", "--text-7", "--radius-1", "--radius-3", "--radius-pill", "--sp-1", "--ink", "--git-added", "--git-added-wash", "--git-changed", "--git-changed-wash", "--git-deleted", "--git-deleted-wash", "--git-untracked", "--git-untracked-wash" })
        {
            Assert.Contains(token + ":", css, StringComparison.Ordinal);
        }
    }

    /// <summary>The gate fails on each kind of raw value, so a rule that matches nothing cannot pass it unseen.</summary>
    [Theory]
    [InlineData(".a { font-size: 0.857rem; }")]
    [InlineData(".a { font-size: .93em; }")]
    [InlineData(".a { font-size: 14px; }")]
    [InlineData(".a { font: 600 0.9rem/1.3 var(--sans); }")]
    [InlineData(".a { font: 600 var(--text-2)/1.3 var(--sans); font-size: var(--size); }")]
    [InlineData(".a { border-radius: 8px; }")]
    [InlineData(".a { border-radius: 0 8px 8px 0; }")]
    [InlineData(".a { border-top-left-radius: 50%; }")]
    [InlineData(".a { color: #fff; }")]
    [InlineData(".a { background: rgba(0, 0, 0, .1); }")]
    [InlineData(".a { box-shadow: 0 1px 2px hsl(200 50% 50%); }")]
    [InlineData(".a { border-color: white; }")]
    [InlineData(".a { color: crimson; }")]
    [InlineData(".a { background: RebeccaPurple; }")]
    [InlineData(".a { box-shadow: 0 0 0 1px lightgoldenrodyellow; }")]
    [InlineData(".a { color: CanvasText; }")]
    [InlineData(".a { --text-9: 3rem; }")]
    [InlineData("@media (max-width: 760px) {\n  .a { font-size: 1rem; }\n}")]
    public void ARawValueInASheetFails(string css)
    {
        Assert.NotEmpty(SheetProblems("web/x.css", css));
    }

    [Theory]
    [InlineData(".a { font-size: var(--text-2); font: 600 var(--text-1)/1 var(--mono); }")]
    [InlineData(".a { font: inherit; border-radius: 0 var(--radius-2) var(--radius-2) 0; }")]
    [InlineData(".a { color: light-dark(hsl(var(--hue) var(--origin-light)), hsl(var(--hue) var(--origin-dark))); }")]
    [InlineData(".a { background: color-mix(in srgb, var(--yes) 88%, transparent); }")]
    [InlineData("#add-on { color: var(--ink); }\n/* font-size: 13px */")]
    [InlineData(".a:hover { border-color: currentColor; }")]
    [InlineData(".a { color: transparent; background: inherit; border-color: currentcolor; }")]
    [InlineData(".a { color: var(--tan); animation: tan-in 1s; }")]
    public void ATokenInASheetPasses(string css)
    {
        Assert.Empty(SheetProblems("web/x.css", css));
    }

    [Theory]
    [InlineData("el.style.fontSize = '13px';")]
    [InlineData("el.style.borderRadius = `${r}px`;")]
    [InlineData("el.style.color = '#fff';")]
    [InlineData("el.style.background = 'rgb(0, 0, 0)';")]
    [InlineData("el.style.setProperty('font-size', '1rem');")]
    [InlineData("el.style.setProperty('--tint', 'white');")]
    [InlineData("el.style.color = 'tomato';")]
    [InlineData("h('span', { style: 'color: var(--ink)' });")]
    [InlineData("el.setAttribute('style', 'color: var(--ink)');")]
    [InlineData("el.style.cssText = 'font-size: 13px; border-radius: 8px';")]
    [InlineData("el.style.cssText = `color: ${ink}`;")]
    [InlineData("const theme = { fontSize: '13px', background: paper };")]
    [InlineData("const look = { borderRadius: `${r}px` };")]
    [InlineData("html`<span style=\"color: var(--ink)\"></span>`")]
    [InlineData("html`<span class=\"note\" style=${look}></span>`")]
    [InlineData("html`<ul>${items.map((item) => html`<li .style=${item.look}>${item.text}</li>`)}</ul>`")]
    public void ARawValueInAScriptFails(string js)
    {
        Assert.NotEmpty(ScriptProblems("web/x.js", js));
    }

    [Theory]
    [InlineData("el.style.fontSize = `var(${token.name})`;")]
    [InlineData("el.style.top = `${at}px`;")]
    [InlineData("el.style.setProperty('--hue', String(hue));")]
    [InlineData("probe.style.color = `var(${name})`;")]
    [InlineData("g.font = '11px ' + sky.font;")]
    [InlineData("el.style.cssText = looks[Number(el.getAttribute('data-look'))];")]
    [InlineData("const theme = { fontSize: resolvedSize('--text-2'), background: paper };")]
    [InlineData("html`<p class=\"set-note\" data-style=${look}>${note}</p>`")]
    public void ATokenInAScriptPasses(string js)
    {
        Assert.Empty(ScriptProblems("web/x.js", js));
    }

    private static IEnumerable<string> SheetProblems(string name, string css)
    {
        var text = WindowResources.WithoutComments(css);
        foreach (Match declaration in Declaration().Matches(text))
        {
            var property = declaration.Groups["property"].Value;
            var value = declaration.Groups["value"].Value.Trim();
            var problem = DeclarationProblem(property, value);
            if (problem is null)
            {
                continue;
            }

            yield return $"{name}:{WindowResources.LineOf(text, declaration.Index)}: {property}: {value}: {problem}";
        }
    }

    private static string? DeclarationProblem(string property, string value)
    {
        if (property.StartsWith("--text-", StringComparison.Ordinal) || property.StartsWith("--radius-", StringComparison.Ordinal))
        {
            return "a scale's step is defined in tokens.css only";
        }

        if (property == "font-size" && value != "inherit" && !TextToken().IsMatch(value))
        {
            return "a font size names a --text- token";
        }

        if (property == "font" && value != "inherit" && (Length().IsMatch(value) || !value.Contains("var(--text-", StringComparison.Ordinal)))
        {
            return "a font's size names a --text- token";
        }

        if (RadiusProperty().IsMatch(property) && value.Split(' ', '/').Any((part) => part.Length > 0 && part != "0" && !RadiusToken().IsMatch(part)))
        {
            return "a radius names a --radius- token";
        }

        if (property != "content" && ColorLiteral().IsMatch(value))
        {
            return "a color names a token";
        }

        return null;
    }

    private static IEnumerable<string> ScriptProblems(string name, string js)
    {
        foreach (Match write in StyleWrite().Matches(js))
        {
            var property = write.Groups["property"].Value;
            var value = write.Groups["value"].Value;
            var sized = SizedStyle().IsMatch(property);
            if (sized && Length().IsMatch(value))
            {
                yield return $"{name}:{WindowResources.LineOf(js, write.Index)}: {write.Value.Trim()}: a font size or radius names a token";
            }

            if (ColorLiteral().IsMatch(value))
            {
                yield return $"{name}:{WindowResources.LineOf(js, write.Index)}: {write.Value.Trim()}: a color names a token";
            }
        }

        foreach (Match option in SizedOption().Matches(js))
        {
            if (Length().IsMatch(option.Groups["value"].Value))
            {
                yield return $"{name}:{WindowResources.LineOf(js, option.Index)}: {option.Value.Trim()}: a font size or radius names a token";
            }
        }

        foreach (Match attribute in StyleAttribute().Matches(js))
        {
            yield return $"{name}:{WindowResources.LineOf(js, attribute.Index)}: {attribute.Value.Trim()}: a style is set through el.style, naming tokens";
        }

        foreach (var tag in LitTemplates.Tags(js))
        {
            foreach (var attribute in tag.Attributes.Where((attribute) => attribute.Name.TrimStart('.', '?') == "style"))
            {
                yield return $"{name}:{WindowResources.LineOf(js, tag.Index)}: <{tag.Name} {attribute.Name}=...>: a style is set through el.style, naming tokens";
            }
        }
    }

    /// <summary>A declaration: a property right after the { or ; that opens it, and its value up to the ; or } that ends it.</summary>
    [GeneratedRegex(@"(?<=[{;])\s*(?<property>--[\w-]+|[a-z][a-z-]*)\s*:(?<value>[^;{}]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^var\(--text-[\w-]+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex TextToken();

    [GeneratedRegex(@"^var\(--radius-[\w-]+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex RadiusToken();

    [GeneratedRegex(@"^border(-[a-z]+)*-radius$", RegexOptions.CultureInvariant)]
    private static partial Regex RadiusProperty();

    /// <summary>A length written as a number: 13px, .86em, 1rem, 50%, or a template that adds a unit to a number.</summary>
    [GeneratedRegex(@"((?<![\w-])\d*\.?\d+|\})(px|rem|em|pt|%)", RegexOptions.CultureInvariant)]
    private static partial Regex Length();

    /// <summary>Every named color of CSS Color 4, all 148. transparent and currentColor name no color of their own and pass.</summary>
    private const string NamedColors =
        "aliceblue|antiquewhite|aqua|aquamarine|azure|beige|bisque|black|blanchedalmond|blue|blueviolet|" +
        "brown|burlywood|cadetblue|chartreuse|chocolate|coral|cornflowerblue|cornsilk|crimson|cyan|" +
        "darkblue|darkcyan|darkgoldenrod|darkgray|darkgreen|darkgrey|darkkhaki|darkmagenta|darkolivegreen|" +
        "darkorange|darkorchid|darkred|darksalmon|darkseagreen|darkslateblue|darkslategray|darkslategrey|" +
        "darkturquoise|darkviolet|deeppink|deepskyblue|dimgray|dimgrey|dodgerblue|firebrick|floralwhite|" +
        "forestgreen|fuchsia|gainsboro|ghostwhite|gold|goldenrod|gray|green|greenyellow|grey|honeydew|" +
        "hotpink|indianred|indigo|ivory|khaki|lavender|lavenderblush|lawngreen|lemonchiffon|lightblue|" +
        "lightcoral|lightcyan|lightgoldenrodyellow|lightgray|lightgreen|lightgrey|lightpink|lightsalmon|" +
        "lightseagreen|lightskyblue|lightslategray|lightslategrey|lightsteelblue|lightyellow|lime|" +
        "limegreen|linen|magenta|maroon|mediumaquamarine|mediumblue|mediumorchid|mediumpurple|" +
        "mediumseagreen|mediumslateblue|mediumspringgreen|mediumturquoise|mediumvioletred|midnightblue|" +
        "mintcream|mistyrose|moccasin|navajowhite|navy|oldlace|olive|olivedrab|orange|orangered|orchid|" +
        "palegoldenrod|palegreen|paleturquoise|palevioletred|papayawhip|peachpuff|peru|pink|plum|" +
        "powderblue|purple|rebeccapurple|red|rosybrown|royalblue|saddlebrown|salmon|sandybrown|seagreen|" +
        "seashell|sienna|silver|skyblue|slateblue|slategray|slategrey|snow|springgreen|steelblue|tan|teal|" +
        "thistle|tomato|turquoise|violet|wheat|white|whitesmoke|yellow|yellowgreen";

    /// <summary>The system colors of CSS Color 4: the platform's colors, never the theme's.</summary>
    private const string SystemColors =
        "accentcolor|accentcolortext|activetext|buttonborder|buttonface|buttontext|canvas|canvastext|" +
        "field|fieldtext|graytext|highlight|highlighttext|linktext|mark|marktext|selecteditem|" +
        "selecteditemtext|visitedtext";

    /// <summary>A color written as a value: a hex, a color function of numbers, or a named or system color, in any case.</summary>
    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b|\b(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(\s*[\d.]|(?<![\w.$-])(?i:" + NamedColors + "|" + SystemColors + @")(?![\w.$(-])", RegexOptions.CultureInvariant)]
    private static partial Regex ColorLiteral();

    /// <summary>A style a script sets: el.style.prop = value, or el.style.setProperty('prop', value), to the line's end.</summary>
    [GeneratedRegex(@"\.style\.(?:setProperty\(\s*['""`](?<property>[\w-]+)['""`]\s*,|(?<property>\w+)\s*=(?!=))(?<value>[^\n]*)", RegexOptions.CultureInvariant)]
    private static partial Regex StyleWrite();

    [GeneratedRegex(@"^(fontSize|font|font-size|border\w*Radius|border(-[a-z]+)*-radius|--text-[\w-]+|--radius-[\w-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SizedStyle();

    /// <summary>A size handed to a library as an option, such as mermaid's fontSize: its value up to the , or the line's end.</summary>
    [GeneratedRegex(@"\b(?:fontSize|border\w*Radius)\s*:(?<value>[^,\n]*)",RegexOptions.CultureInvariant)]
    private static partial Regex SizedOption();

    /// <summary>A whole style written as text: a style attribute, or a string or template set as cssText.</summary>
    [GeneratedRegex(@"\bstyle:\s*['""`]|setAttribute\(\s*['""`]style['""`]|\.style\.cssText\s*=\s*['""`]", RegexOptions.CultureInvariant)]
    private static partial Regex StyleAttribute();
}
