using System.Globalization;

namespace Brain.Installing;

/// <summary>
/// A unified diff of two texts, line by line, with three lines of context around each change: how the install shows
/// what a file becomes. A text that is null is a file that does not exist, so a new file shows whole, as added lines.
/// </summary>
internal static class LineDiff
{
    private const int Context = 3;
    private const char Same = ' ';
    private const char Removed = '-';
    private const char Added = '+';

    /// <summary>The diff's lines under a header naming <paramref name="label"/>; none when the texts are equal.</summary>
    public static IReadOnlyList<string> Unified(string label, string? before, string? after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return [];
        }

        var script = Script(Lines(before), Lines(after));
        var lines = new List<string>
        {
            "--- " + (before is null ? "/dev/null" : label),
            "+++ " + (after is null ? "/dev/null" : label),
        };
        foreach (var (start, end) in Hunks(script))
        {
            lines.Add(Header(script, start, end));
            lines.AddRange(script.Skip(start).Take(end - start).Select(step => step.Kind + step.Text));
        }

        return lines;
    }

    private static string[] Lines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.TrimEnd('\n').Split('\n');

    /// <summary>The shortest edit from <paramref name="before"/> to <paramref name="after"/>, by their longest common subsequence.</summary>
    private static List<(char Kind, string Text)> Script(string[] before, string[] after)
    {
        var common = new int[before.Length + 1][];
        for (var i = 0; i <= before.Length; i++)
        {
            common[i] = new int[after.Length + 1];
        }

        for (var i = before.Length - 1; i >= 0; i--)
        {
            for (var j = after.Length - 1; j >= 0; j--)
            {
                common[i][j] = string.Equals(before[i], after[j], StringComparison.Ordinal)
                    ? common[i + 1][j + 1] + 1
                    : Math.Max(common[i + 1][j], common[i][j + 1]);
            }
        }

        var script = new List<(char, string)>();
        int at = 0, to = 0;
        while (at < before.Length || to < after.Length)
        {
            if (at < before.Length && to < after.Length && string.Equals(before[at], after[to], StringComparison.Ordinal))
            {
                script.Add((Same, before[at++]));
                to++;
            }
            else if (to >= after.Length || (at < before.Length && common[at + 1][to] >= common[at][to + 1]))
            {
                script.Add((Removed, before[at++]));
            }
            else
            {
                script.Add((Added, after[to++]));
            }
        }

        return script;
    }

    /// <summary>Each run of the script that holds changes, widened by the context and merged where two runs touch.</summary>
    private static List<(int Start, int End)> Hunks(List<(char Kind, string Text)> script)
    {
        var hunks = new List<(int Start, int End)>();
        for (var i = 0; i < script.Count; i++)
        {
            if (script[i].Kind == Same)
            {
                continue;
            }

            var start = Math.Max(0, i - Context);
            var end = Math.Min(script.Count, i + Context + 1);
            if (hunks.Count > 0 && start <= hunks[^1].End)
            {
                hunks[^1] = (hunks[^1].Start, end);
            }
            else
            {
                hunks.Add((start, end));
            }
        }

        return hunks;
    }

    /// <summary>The <c>@@ -a,b +c,d @@</c> line of the hunk from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static string Header(List<(char Kind, string Text)> script, int start, int end)
    {
        var before = script.Take(start).Count(step => step.Kind != Added);
        var after = script.Take(start).Count(step => step.Kind != Removed);
        var hunk = script.Skip(start).Take(end - start).ToList();
        var removed = hunk.Count(step => step.Kind != Added);
        var added = hunk.Count(step => step.Kind != Removed);
        return string.Create(CultureInfo.InvariantCulture, $"@@ -{Range(before, removed)} +{Range(after, added)} @@");
    }

    private static string Range(int skipped, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{(count == 0 ? skipped : skipped + 1)},{count}");
}
