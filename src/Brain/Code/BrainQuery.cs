using System.Text.RegularExpressions;

namespace Brain.Code;

/// <summary>One node a brain query found: its ref and its title.</summary>
internal sealed record BrainItem(string Ref, string Title);

/// <summary>
/// A Bash call that runs one of the <c>brain</c> CLI's reading commands, <c>lookup</c>, <c>nearest</c>, <c>node</c> or
/// <c>walk</c>, with what it asked. The window draws such a call as a brain query, not as a command, and lists the nodes
/// its output holds.
/// </summary>
internal sealed partial record BrainQuery(string Verb, string Asked)
{
    /// <summary>The query a Bash <paramref name="command"/> runs, or null when it runs no reading command of brain.</summary>
    public static BrainQuery? Of(string command)
    {
        var match = Reading().Match(command);
        if (!match.Success)
        {
            return null;
        }

        var asked = Redirect().Replace(match.Groups["rest"].Value, string.Empty).Trim().Trim('"', '\'');
        return new BrainQuery(match.Groups["verb"].Value, asked);
    }

    /// <summary>
    /// The nodes the command's <paramref name="output"/> lists, in its order, each read from the shape of its line: a line
    /// that opens with a ref is lookup's or node's own, <c>ref  confidence  title</c>, or nearest's when indented,
    /// <c>  ref  title  (scope)</c>; and a line whose second column is an edge, <c>  score  ref relation -> ref  title</c>,
    /// is a walk's, which leads to the ref of the two that was not asked. A field, an edge without a title and a header
    /// are no items.
    /// </summary>
    // navarone: 0000204
    public IReadOnlyList<BrainItem> Items(string output)
    {
        var items = new List<BrainItem>();
        foreach (var line in output.Split('\n'))
        {
            if (Item(line) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private BrainItem? Item(string line)
    {
        var columns = Columns().Split(line.Trim());
        if (columns.Length < 2)
        {
            return null;
        }

        if (!IsRef(columns[0]))
        {
            return Neighbour(columns);
        }

        if (line.StartsWith(' '))
        {
            return new BrainItem(columns[0], columns[1]);
        }

        if (Verb == "walk")
        {
            return null;
        }

        return new BrainItem(columns[0], columns[^1]);
    }

    /// <summary>The node a walk line leads to: of the two refs of its edge, the one the walk did not start from.</summary>
    private BrainItem? Neighbour(string[] columns)
    {
        if (columns.Length < 3)
        {
            return null;
        }

        if (columns[1].Split(' ') is not [var from, _, "->", var to])
        {
            return null;
        }

        if (Slug(to) == Slug(Asked))
        {
            return new BrainItem(from, columns[2]);
        }

        return new BrainItem(to, columns[2]);
    }

    private static bool IsRef(string text) => text.Contains(':', StringComparison.Ordinal) && !text.Contains(' ', StringComparison.Ordinal);

    private static string Slug(string reference) => reference[(reference.LastIndexOf(':') + 1)..];

    [GeneratedRegex(@"^\s*(?:cd\s+\S+\s*&&\s*)?(?:\S*/)?brain\s+(?<verb>lookup|nearest|node|walk)\b(?<rest>[^|;&\n]*)")]
    private static partial Regex Reading();

    [GeneratedRegex(@"\s*\d*>\s*$")]
    private static partial Regex Redirect();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();
}
