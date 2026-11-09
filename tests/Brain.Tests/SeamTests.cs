using System.Text;
using System.Text.RegularExpressions;

namespace Brain.Tests;

/// <summary>
/// seam.js as a gate: the one seam between the window's classic scripts and its modules. Besides lit.js,
/// which hands lit-html to the classic scripts, and offer.js, which hands them the shared components, only seam.js
/// writes to window, through offer(). Every name its app object hands the modules is a top-level name a classic script
/// declares, so a stale entry fails. A module names a classic script's global only through app: each name a classic
/// script declares at its top level that a module's code names bare, not as a property, the module binds itself, as
/// <c>const { toast } = app</c>, a local declaration, a parameter or an import. The code is read with its comments, its
/// strings and the text of its templates blanked, so a word in a sentence names nothing.
/// </summary>
public sealed partial class SeamTests
{
    private const string Seam = "web/seam.js";

    /// <summary>
    /// The modules that may write to window: the seam, lit.js, which puts html, render and nothing there, and offer.js,
    /// which offers the shared components.
    /// </summary>
    private static readonly HashSet<string> WindowWriters = [Seam, "shared/lit.js", "shared/offer.js"];

    [Fact]
    public void NoModuleButTheSeamWritesToWindow()
    {
        var modules = Modules().Where((name) => !WindowWriters.Contains(name)).ToList();

        var problems = modules.SelectMany((name) => WindowWrites(name, WindowResources.Read(name))).ToList();

        Assert.True(modules.Count >= 7, $"only {modules.Count} modules were read");
        Assert.Contains("web/pages/settings.js", modules);
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void NoComponentKnowsTheSeamOrOffersAnything()
    {
        var components = WindowResources.Of("web/", ".js").Where((name) => name.StartsWith("web/components/", StringComparison.Ordinal) || name.StartsWith("shared/components/", StringComparison.Ordinal)).ToList();

        var problems = components.SelectMany((name) => SeamUses(name, WindowResources.Read(name))).ToList();

        Assert.True(components.Count >= 6, $"only {components.Count} components were read");
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Theory]
    [InlineData("import { offer } from '/seam.js';")]
    [InlineData("import { app } from '/seam.js';\nexport function x() {}")]
    [InlineData("export function x() {}\noffer({ x });")]
    public void AComponentThatImportsTheSeamOrOffersFails(string js)
    {
        Assert.NotEmpty(SeamUses("web/components/x.js", js));
    }

    [Theory]
    [InlineData("import { html } from '/lit-html.js';\nexport function x() {}")]
    [InlineData("// The Stylebook draws it: offer({ x }) from seam.js.\nexport function x() {}")]
    public void AComponentThatKnowsNothingOfTheSeamPasses(string js)
    {
        Assert.Empty(SeamUses("web/components/x.js", js));
    }

    [Theory]
    [InlineData("window.setting = setting;")]
    [InlineData("Object.assign(window, { setting });")]
    [InlineData("window['setting'] = setting;")]
    [InlineData("globalThis.setting = setting;")]
    [InlineData("const { setting } = globalThis;")]
    public void AWriteToWindowFails(string js)
    {
        Assert.NotEmpty(WindowWrites("web/x.js", js));
    }

    [Theory]
    [InlineData("window.addEventListener('keydown', onKey);")]
    [InlineData("if (window.innerWidth === 0) return;")]
    [InlineData("// window.setting = setting;\noffer({ setting });")]
    public void AModuleThatOnlyReadsWindowPasses(string js)
    {
        Assert.Empty(WindowWrites("web/x.js", js));
    }

    [Fact]
    public void EveryNameTheSeamHandsTheModulesIsAClassicScriptsGlobal()
    {
        var declared = ClassicDeclarations();

        var problems = StaleNames(WindowResources.Read(Seam), declared).ToList();

        Assert.Contains("registerInbox", declared);
        Assert.Contains("NODE_REVIEW", declared);
        Assert.True(AppNames(WindowResources.Read(Seam)).Count >= 10, "the seam's app object was not read");
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void AStaleNameInTheSeamFails()
    {
        var problems = StaleNames("export const app = {\n  state,\n  gone,\n};\n", ["state"]).ToList();

        Assert.Equal(["seam.js hands the modules gone, which no classic script declares at its top level"], problems);
    }

    [Fact]
    public void AModuleNamesAClassicGlobalOnlyThroughTheSeam()
    {
        var declared = ClassicDeclarations();
        var modules = Modules().Where((name) => name != Seam).ToList();

        var problems = modules.SelectMany((name) => UnboundNames(name, WindowResources.Read(name), declared)).ToList();

        Assert.True(modules.Count >= 8, $"only {modules.Count} modules were read");
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Theory]
    [InlineData("toast('saved');")]
    [InlineData("const at = $('main');")]
    [InlineData("const at = state.inbox;")]
    [InlineData("export function view() {\n  return html`<p>${keys('j')} move</p>`;\n}")]
    [InlineData("const { toast } = app;\nif (NODE_REVIEW) toast('x');")]
    public void AClassicGlobalNamedPastTheSeamFails(string js)
    {
        Assert.NotEmpty(UnboundNames("web/x.js", js, ["toast", "state", "keys", "NODE_REVIEW", "$"]));
    }

    [Theory]
    [InlineData("const { toast, state } = app;\ntoast(state.inbox);")]
    [InlineData("app.toast('saved');\nconst row = { state: 1 };\nrow.state = 2;")]
    [InlineData("function toast(message) {}\ntoast('x');")]
    [InlineData("const said = 'toast(it), the state of keys';\n// toast('x')")]
    [InlineData("export const view = () => html`<p>The state of the toast (now)</p>`;")]
    [InlineData("const pick = (keys, { state }) => keys(state);")]
    [InlineData("import { keys } from '/x.js';\nkeys();")]
    [InlineData("for (const state of all) {\n  use(state);\n}")]
    [InlineData("const ITEM = /^(.*)$/;")]
    public void AClassicGlobalBoundInTheModulePasses(string js)
    {
        Assert.Empty(UnboundNames("web/x.js", js, ["toast", "state", "keys", "NODE_REVIEW", "$"]));
    }

    /// <summary>The modules of brain's window: every script it serves that its index.html does not load as a classic one.</summary>
    private static List<string> Modules() =>
        WindowResources.Of("web/", ".js").Except(WindowResources.ClassicScripts("web/")).ToList();

    /// <summary>Every name a classic script of brain's window declares at column 0: a function, a const, a let, a var or a class.</summary>
    private static HashSet<string> ClassicDeclarations() =>
        WindowResources.ClassicScripts("web/")
            .SelectMany((name) => TopLevelDeclaration().Matches(WindowResources.Read(name)))
            .Select((declaration) => declaration.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A component file's import of seam.js, or its call of offer(): a component has no side effect on import and
    /// knows nothing of the seam.
    /// </summary>
    private static IEnumerable<string> SeamUses(string name, string js)
    {
        var code = Code(js);
        foreach (Match use in SeamImport().Matches(js))
        {
            yield return $"{name}:{WindowResources.LineOf(js, use.Index)}: a component never imports seam.js: the seam offers it, from its own side";
        }

        foreach (Match call in OfferCall().Matches(code))
        {
            yield return $"{name}:{WindowResources.LineOf(code, call.Index)}: a component never calls offer(): the seam offers it, from its own side";
        }
    }

    private static IEnumerable<string> WindowWrites(string name, string js)
    {
        var code = Code(js);
        foreach (Match write in WindowWrite().Matches(code))
        {
            yield return $"{name}:{WindowResources.LineOf(code, write.Index)}: {write.Value.Trim()}: only seam.js writes to window, through offer()";
        }
    }

    private static List<string> AppNames(string seam)
    {
        var app = AppObject().Match(Code(seam));
        if (!app.Success)
        {
            return [];
        }

        return app.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static IEnumerable<string> StaleNames(string seam, HashSet<string> declared) =>
        AppNames(seam)
            .Where((name) => !declared.Contains(name))
            .Select((name) => $"seam.js hands the modules {name}, which no classic script declares at its top level");

    /// <summary>Each classic global a module's code names bare without binding it itself.</summary>
    private static IEnumerable<string> UnboundNames(string name, string js, HashSet<string> declared)
    {
        var code = Code(js);
        var bound = Bound(code);
        var told = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match use in BareName().Matches(code))
        {
            var used = use.Groups["name"].Value;
            if (EndsRegularExpression(code, use))
            {
                continue;
            }

            if (!declared.Contains(used) || bound.Contains(used) || !told.Add(used))
            {
                continue;
            }

            yield return $"{name}:{WindowResources.LineOf(code, use.Index)}: {used} is a classic script's global: bind it from app, as const {{ {used} }} = app";
        }
    }

    /// <summary>A $ right before a slash is the end anchor that closes a regular expression, not the classic script's $.</summary>
    private static bool EndsRegularExpression(string code, Match use) =>
        use.Value == "$" && use.Index + 1 < code.Length && code[use.Index + 1] == '/';

    /// <summary>
    /// Every name the code binds: a declaration, the names of a destructure, an import's names, and each parameter
    /// of a function or an arrow, destructured ones included.
    /// </summary>
    private static HashSet<string> Bound(string code)
    {
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match declaration in Declaration().Matches(code))
        {
            bound.Add(declaration.Groups["name"].Value);
        }

        foreach (var pattern in new[] { Destructure(), Import(), Parameters() })
        {
            foreach (Match names in pattern.Matches(code))
            {
                foreach (Match identifier in Identifier().Matches(names.Groups["names"].Value))
                {
                    bound.Add(identifier.Value);
                }
            }
        }

        return bound;
    }

    /// <summary>
    /// The code with its comments, its strings and the text of its templates blanked, each newline kept. The code of a
    /// template's interpolations is kept, and read the same way.
    /// </summary>
    private static string Code(string js)
    {
        var code = new StringBuilder(js);
        var at = 0;
        while (at < js.Length)
        {
            at = ScanCode(js, code, at) + 1;
        }

        return code.ToString();
    }

    /// <summary>Reads code from at to the brace that closes it, or to the end; returns where it stopped.</summary>
    private static int ScanCode(string js, StringBuilder code, int at)
    {
        var depth = 0;
        while (at < js.Length)
        {
            var c = js[at];
            if (c is '\'' or '"')
            {
                at = Blank(js, code, at, StringEnd(js, at)) + 1;
                continue;
            }

            if (c == '/' && at + 1 < js.Length && js[at + 1] == '/')
            {
                var end = js.IndexOf('\n', at);
                at = Blank(js, code, at, end < 0 ? js.Length - 1 : end - 1) + 1;
                continue;
            }

            if (c == '`')
            {
                at = ScanTemplate(js, code, at + 1) + 1;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                if (depth == 0)
                {
                    return at;
                }

                depth--;
            }

            at++;
        }

        return at;
    }

    /// <summary>Blanks a template's text from at to its closing backtick, and reads each interpolation as code; returns the backtick.</summary>
    private static int ScanTemplate(string js, StringBuilder code, int at)
    {
        while (at < js.Length && js[at] != '`')
        {
            if (js[at] == '$' && at + 1 < js.Length && js[at + 1] == '{')
            {
                Blank(js, code, at, at + 1);
                at = ScanCode(js, code, at + 2) + 1;
                continue;
            }

            var step = js[at] == '\\' ? 2 : 1;
            Blank(js, code, at, Math.Min(at + step, js.Length) - 1);
            at += step;
        }

        return at;
    }

    /// <summary>The closing quote of the string whose opening quote is at at, or the line's end.</summary>
    private static int StringEnd(string js, int at)
    {
        var quote = js[at];
        at++;
        while (at < js.Length && js[at] != quote && js[at] != '\n')
        {
            at += js[at] == '\\' ? 2 : 1;
        }

        return Math.Min(at, js.Length - 1);
    }

    /// <summary>Blanks from to through, newlines kept; returns through.</summary>
    private static int Blank(string js, StringBuilder code, int from, int through)
    {
        for (var at = from; at <= through && at < js.Length; at++)
        {
            if (js[at] != '\n')
            {
                code[at] = ' ';
            }
        }

        return through;
    }

    [GeneratedRegex(@"^(?:async\s+)?(?:function\s*\*?|const|let|var|class)\s+(?<name>[\w$]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TopLevelDeclaration();

    [GeneratedRegex(@"\bwindow\s*(?:\.\s*[\w$]+|\[[^\]]*\])\s*=(?!=)|Object\.assign\(\s*window\b|\bglobalThis\b", RegexOptions.CultureInvariant)]
    private static partial Regex WindowWrite();

    [GeneratedRegex(@"^\s*import\b[^;]*['""][^'""]*seam\.js['""]", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SeamImport();

    [GeneratedRegex(@"\boffer\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex OfferCall();

    [GeneratedRegex(@"\bexport\s+const\s+app\s*=\s*\{(?<names>[\w$,\s]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex AppObject();

    /// <summary>A name not read as a property, after a dot, nor written as an object's key, before a colon.</summary>
    [GeneratedRegex(@"(?<![\w$.])(?<name>[A-Za-z_$][\w$]*)(?![\w$])(?!\s*:(?!:))", RegexOptions.CultureInvariant)]
    private static partial Regex BareName();

    [GeneratedRegex(@"\b(?:function\s*\*?|const|let|var|class)\s+(?<name>[\w$]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"\b(?:const|let|var)\s*\{(?<names>[^{}]*)\}\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex Destructure();

    [GeneratedRegex(@"\bimport\s*\{(?<names>[^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Import();

    /// <summary>The parameter list of a function or of an arrow.</summary>
    [GeneratedRegex(@"\bfunction\s*\*?\s*[\w$]*\s*\((?<names>[^()]*)\)|\((?<names>[^()]*)\)\s*=>|(?<names>[\w$]+)\s*=>", RegexOptions.CultureInvariant)]
    private static partial Regex Parameters();

    [GeneratedRegex(@"[A-Za-z_$][\w$]*", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
