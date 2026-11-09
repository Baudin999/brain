using System.Text.RegularExpressions;

namespace Brain.Merge;

/// <summary>
/// Reads the <c>+</c> lines of a worker report: a node line with <c>|</c> columns, an edge line <c>from -rel-> to</c>, or
/// a subjects line <c>scope:slug [a, b]</c>. Every other line is prose and is ignored.
/// </summary>
internal static partial class MergeParser
{
    private const string Plus = "+";
    private const char Column = '|';
    private const int NodeColumns = 4;
    private const int MostColumns = 5;
    private const int KindColumn = 2;
    private const int NamerColumn = 3;
    private const int WhyColumn = 4;

    public static (IReadOnlyList<MergeOp> Ops, IReadOnlyList<string> Errors) Parse(TextReader report)
    {
        var ops = new List<MergeOp>();
        var errors = new List<string>();
        var number = 0;
        for (var line = report.ReadLine(); line is not null; line = report.ReadLine())
        {
            number++;
            var text = line.Trim();
            if (!text.StartsWith(Plus, StringComparison.Ordinal))
            {
                continue;
            }

            var op = Read(number, text[Plus.Length..].Trim());
            if (op is null)
            {
                errors.Add($"line {number}: cannot read {text}");
                continue;
            }

            ops.Add(op);
        }

        return (ops, errors);
    }

    private static MergeOp? Read(int number, string body) =>
        body.Contains(Column, StringComparison.Ordinal) ? ReadNode(number, body) : (MergeOp?)ReadEdge(number, body) ?? ReadSubjects(number, body);

    private static NodeOp? ReadNode(int number, string body)
    {
        var columns = body.Split(Column, MostColumns, StringSplitOptions.TrimEntries);
        if (columns.Length < NodeColumns || Ref.Parse(columns[0]).Node is not { Scope: { } scope } node)
        {
            return null;
        }

        var kind = columns[KindColumn].Length > 0 ? columns[KindColumn] : NewNode.DefaultKind;
        var spec = new NewNode(scope, node.Slug, columns[1], kind, columns[NamerColumn], Session: null)
        {
            Why = Optional(columns, WhyColumn) ?? string.Empty,
        };
        return new NodeOp(number, spec);
    }

    /// <summary>The why column, or null when the line leaves it out or empty.</summary>
    private static string? Optional(string[] columns, int at) => columns.Length > at && columns[at].Length > 0 ? columns[at] : null;

    private static EdgeOp? ReadEdge(int number, string body)
    {
        var match = EdgeLine().Match(body);
        return match.Success
            ? new EdgeOp(number, match.Groups[nameof(EdgeOp.From)].Value, match.Groups[nameof(EdgeOp.Rel)].Value, match.Groups[nameof(EdgeOp.To)].Value)
            : null;
    }

    private static SubjectsOp? ReadSubjects(int number, string body)
    {
        var match = SubjectsLine().Match(body);
        var list = match.Groups[nameof(SubjectsOp.Subjects)].Value;
        return match.Success && Subjects.Problem(list) is null
            ? new SubjectsOp(number, match.Groups[nameof(SubjectsOp.Node)].Value, Subjects.Parse(list))
            : null;
    }

    [GeneratedRegex(@"^(?<Node>[^\s\[]+)\s+(?<Subjects>\[.*\])$")]
    private static partial Regex SubjectsLine();

    [GeneratedRegex(@"^(?<From>\S+)\s+-(?<Rel>[^\s>]+)->\s+(?<To>\S+)$")]
    private static partial Regex EdgeLine();
}
