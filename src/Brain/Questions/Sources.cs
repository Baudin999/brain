using System.Text.RegularExpressions;

namespace Brain.Questions;

/// <summary>
/// The tools whose questions brain's window shows, from <c>sources.tsv</c> in the data repo: one line per source, its
/// name, a tab, the shell command that prints its open questions as JSON, and optionally, each after a tab, the shell
/// command that prints a question's context for a query, the one that prints its events as JSON and the one that
/// prints the code stored under a hash, which brain appends as an argument. A blank line or one starting with # is
/// skipped. The name names the source's inbox and its answers directory, so it is a plain word.
/// </summary>
internal static partial class Sources
{
    public const string FileName = "sources.tsv";
    private const char Tab = '\t';
    private const char Comment = '#';

    // navarone: 0000101
    public static IReadOnlyList<Source> All(string home)
    {
        var path = Path.Combine(home, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var sources = new List<Source>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(Comment))
            {
                continue;
            }

            sources.Add(Parse(line, sources));
        }

        return sources;
    }

    private static Source Parse(string line, List<Source> before)
    {
        var columns = line.Split(Tab, 5).Select(column => column.Trim()).ToList();
        var name = columns[0];
        var command = Column(columns, 1) ?? string.Empty;
        return new Source(name, command, Problem(name, command, before), Column(columns, 2), Column(columns, 3), Column(columns, 4));
    }

    private static string? Problem(string name, string command, List<Source> before)
    {
        if (!IsName(name))
        {
            return $"{FileName} names a source \"{name}\"; a name is letters, digits, dot, dash and underscore";
        }

        if (before.Any(source => source.Name == name))
        {
            return $"{FileName} names {name} twice";
        }

        if (command.Length == 0)
        {
            return $"{FileName} gives {name} no command after a tab";
        }

        return null;
    }

    /// <summary>The column's text, null when the line has no such column or it is empty.</summary>
    private static string? Column(List<string> columns, int index)
    {
        if (index >= columns.Count)
        {
            return null;
        }

        if (columns[index].Length == 0)
        {
            return null;
        }

        return columns[index];
    }

    /// <summary>True for a word safe as a file name: a letter or digit, then letters, digits, dot, dash and underscore.</summary>
    public static bool IsName(string name) => NamePattern().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>One line of <c>sources.tsv</c>; Problem says why the line cannot run, and Context, Events and Code are its optional commands.</summary>
internal sealed record Source(string Name, string Command, string? Problem, string? Context = null, string? Events = null, string? Code = null);
