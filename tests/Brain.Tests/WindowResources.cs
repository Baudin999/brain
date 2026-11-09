using System.Text.RegularExpressions;
using Brain.Serving;

namespace Brain.Tests;

/// <summary>
/// The files the windows serve, as the gates read them, sorted: brain's window's in web/, a workspace's in workspace/,
/// brain code's in code/, and the stylebook, the scripts and the sheets they share in shared/, minus shared/vendor/. One
/// gate holds every window by the same rules.
/// </summary>
internal static partial class WindowResources
{
    public const string Tokens = "shared/tokens.css";
    public const string Components = "shared/components.css";
    public const string Shared = "shared/";
    public const string ComponentFolder = "web/components/";
    public const string SharedComponentFolder = "shared/components/";

    /// <summary>Each window's own folder: brain's window, a workspace's and brain code's. Each serves shared/ beside it.</summary>
    public static readonly string[] Surfaces = ["web/", "workspace/", "code/"];

    /// <summary>Every file of every window that ends with <paramref name="extension"/>.</summary>
    public static IEnumerable<string> Named(string extension) =>
        Served().Where((name) => name.EndsWith(extension, StringComparison.Ordinal));

    /// <summary>The files one window serves that end with <paramref name="extension"/>: its own and the shared ones.</summary>
    public static IEnumerable<string> Of(string surface, string extension) =>
        Named(extension).Where((name) => name.StartsWith(surface, StringComparison.Ordinal) || name.StartsWith(Shared, StringComparison.Ordinal));

    private static IEnumerable<string> Served() =>
        typeof(OmarchyTheme).Assembly.GetManifestResourceNames()
            .Where((name) => Surfaces.Append(Shared).Any((folder) => name.StartsWith(folder, StringComparison.Ordinal)))
            .Where((name) => !name.StartsWith(Shared + "vendor/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// The sheets that draw components: components.css, and each component's own sheet in web/components/ or
    /// shared/components/.
    /// </summary>
    public static IEnumerable<string> ComponentSheets() =>
        Named(".css").Where((name) => name == Components || IsComponentSheet(name));

    /// <summary>A component's own sheet: in brain's window, or shared with the workspace.</summary>
    public static bool IsComponentSheet(string name) =>
        name.EndsWith(".css", StringComparison.Ordinal)
        && (name.StartsWith(ComponentFolder, StringComparison.Ordinal) || name.StartsWith(SharedComponentFolder, StringComparison.Ordinal));

    /// <summary>
    /// The classic scripts a window's index.html loads with defer, in the order it loads them: its own, or else the shared
    /// one served at that path. They share the page's one global scope, and a module has its own.
    /// </summary>
    public static IEnumerable<string> ClassicScripts(string surface)
    {
        var served = Named(".js").ToHashSet(StringComparer.Ordinal);
        foreach (Match script in ClassicScript().Matches(Read(surface + "index.html")))
        {
            var file = script.Groups["file"].Value;
            yield return served.Contains(surface + file) ? surface + file : Shared + file;
        }
    }

    public static string Read(string name)
    {
        using var stream = typeof(OmarchyTheme).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A sheet with each comment blanked out, its newlines kept, so a line number still points into the file.</summary>
    public static string WithoutComments(string css) =>
        Comment().Replace(css, (comment) => string.Concat(comment.Value.Select((c) => c == '\n' ? "\n" : " ")));

    public static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<script src=""/(?<file>[\w./-]+\.js)"" defer>", RegexOptions.CultureInvariant)]
    private static partial Regex ClassicScript();
}
