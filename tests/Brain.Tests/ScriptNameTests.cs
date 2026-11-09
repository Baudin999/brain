using System.Text.RegularExpressions;

namespace Brain.Tests;

/// <summary>
/// A window's classic scripts share one global scope, its own and the shared ones, so a top-level function name is
/// defined once in each window. A second definition in a later file silently replaces the first, as settings.js once
/// replaced the conversation picker. A module, as a page in web/pages, has a scope of its own, but each name it offers
/// the classic scripts through <c>offer({ ... })</c> lands on window beside theirs, so an offered name is read as a
/// definition too. A module's own top-level functions stay in its scope and are not read.
/// </summary>
public sealed partial class ScriptNameTests
{
    [Theory]
    [InlineData("web/", 5)]
    [InlineData("workspace/", 5)]
    [InlineData("code/", 1)]
    public void NoTwoScriptsOfAWindowDefineATopLevelFunctionOfTheSameName(string surface, int least)
    {
        var scripts = WindowResources.ClassicScripts(surface).ToList();
        var modules = WindowResources.Of(surface, ".js").Except(scripts).ToList();
        var files = scripts.Select((name) => new Script(name, WindowResources.Read(name), false))
            .Concat(modules.Select((name) => new Script(name, WindowResources.Read(name), true)));

        var problems = Duplicates(files).ToList();

        Assert.True(scripts.Count >= least, $"only {scripts.Count} scripts were read");
        Assert.Contains(scripts, (name) => name.StartsWith(WindowResources.Shared, StringComparison.Ordinal));
        Assert.Contains(surface + "index.html", WindowResources.Named(".html"));
        Assert.Contains("toggleSettings", Defined(new Script("web/pages/settings.js", WindowResources.Read("web/pages/settings.js"), true)));
        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    /// <summary>The check fails on a name two files define, so a rule that matches nothing cannot pass it unseen.</summary>
    [Fact]
    public void ADuplicateAcrossTwoFilesFails()
    {
        var problems = Duplicates(
        [
            new("web/a.js", "function openPicker() {}\n", false),
            new("web/b.js", "async function openPicker() {}\n", false),
        ]).ToList();

        Assert.Equal(["openPicker is defined in web/a.js and web/b.js"], problems);
    }

    /// <summary>A name a module offers clashes with a classic script's function and with another module's offer alike.</summary>
    [Theory]
    [InlineData("function setting() {}\n", false, "offer({ setting, firstSitting });\n", true, "setting is defined in web/a.js and web/b.js")]
    [InlineData("offer({\n  optionList,\n});\n", true, "offer({ optionList });\n", true, "optionList is defined in web/a.js and web/b.js")]
    [InlineData("offer({ toggleSettings });\n", true, "function toggleSettings() {}\n", false, "toggleSettings is defined in web/a.js and web/b.js")]
    [InlineData("Object.assign(window, { markdownView, resolved });\n", true, "function markdownView() {}\n", false, "markdownView is defined in web/a.js and web/b.js")]
    [InlineData("function resolved() {}\n", false, "Object.assign(window, {\n  resolved,\n});\n", true, "resolved is defined in web/a.js and web/b.js")]
    public void AnOfferedNameThatClashesFails(string first, bool firstIsModule, string second, bool secondIsModule, string expected)
    {
        var problems = Duplicates([new("web/a.js", first, firstIsModule), new("web/b.js", second, secondIsModule)]).ToList();

        Assert.Equal([expected], problems);
    }

    /// <summary>A module's own top-level function is in its scope, not on window, so it clashes with nothing.</summary>
    [Fact]
    public void AModulesOwnFunctionPasses()
    {
        var problems = Duplicates(
        [
            new("web/a.js", "function point() {}\n", false),
            new("web/b.js", "function point() {}\nexport function optionList() {}\noffer({ optionList });\n", true),
        ]);

        Assert.Empty(problems);
    }

    [Fact]
    public void ANestedFunctionOrADistinctNamePasses()
    {
        var problems = Duplicates(
        [
            new("web/a.js", "function run() {\n  function inner() {}\n}\n", false),
            new("web/b.js", "function other() {\n  function inner() {}\n}\n", false),
        ]);

        Assert.Empty(problems);
    }

    private static IEnumerable<string> Duplicates(IEnumerable<Script> scripts)
    {
        var definedIn = new Dictionary<string, string>();
        foreach (var script in scripts)
        {
            var file = script.Name;
            foreach (var function in Defined(script))
            {
                if (definedIn.TryGetValue(function, out var first) && first != file)
                {
                    yield return $"{function} is defined in {first} and {file}";
                    continue;
                }

                definedIn[function] = file;
            }
        }
    }

    /// <summary>The names a script puts on window: a classic script's top-level functions, or the names a module offers.</summary>
    private static IEnumerable<string> Defined(Script script)
    {
        if (!script.Module)
        {
            return TopLevelFunction().Matches(script.Text).Select((definition) => definition.Groups["name"].Value);
        }

        return Offer().Matches(script.Text)
            .SelectMany((offer) => offer.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A script of the window: a classic script, or a module with a scope of its own.</summary>
    private sealed record Script(string Name, string Text, bool Module);

    /// <summary>
    /// An offer() call, or an Object.assign onto window as shared/offer.js and lit.js write, with its names written as
    /// the shorthand keys of one object literal.
    /// </summary>
    [GeneratedRegex(@"(?:\boffer\(|\bObject\.assign\(window,\s*)\{(?<names>[\w$,\s]*)\}\)", RegexOptions.CultureInvariant)]
    private static partial Regex Offer();

    /// <summary>A function declared at column 0: nested ones are indented.</summary>
    [GeneratedRegex(@"^(?:async\s+)?function\s*\*?\s*(?<name>[\w$]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TopLevelFunction();
}
