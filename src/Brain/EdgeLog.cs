using System.Globalization;

namespace Brain;

/// <summary>
/// A scope's edge log, a tsv of from, rel, to, weight and asof: one line per travelled edge, updated in place on each
/// travel. A log in the old row format, one line per travel of date, from, rel and to, reads folded, and the first
/// write or <c>brain fold</c> rewrites it folded.
/// </summary>
internal static class EdgeLog
{
    public const string Header = "from\trel\tto\tweight\tasof";
    public const string RowHeader = "date\tfrom\trel\tto";
    private const char Tab = '\t';
    private const string WeightFormat = "R";
    private const int RowColumns = 4;
    private const int Columns = 5;
    private const int RelColumn = 1;
    private const int ToColumn = 2;
    private const int WeightColumn = 3;
    private const int AsOfColumn = 4;

    public static string Key(string from, string rel, string to) => string.Join(Tab, from, rel, to);

    /// <summary>The log's edges by <see cref="FoldedEdge.Key"/>; empty when the file does not exist.</summary>
    public static Dictionary<string, FoldedEdge> Read(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, FoldedEdge>(StringComparer.Ordinal);
        }

        var lines = File.ReadAllLines(path);
        var edges = lines.Length > 0 && string.Equals(lines[0], RowHeader, StringComparison.Ordinal)
            ? FoldRows(lines.Skip(1).Select(ParseRow).OfType<FoldedEdge>())
            : lines.Skip(1).Select(Parse).OfType<FoldedEdge>();
        return edges.ToDictionary(edge => edge.Key, StringComparer.Ordinal);
    }

    /// <summary>Logs one travel of <paramref name="edge"/> of <paramref name="from"/> on <paramref name="today"/>.</summary>
    public static void Travel(PlacedNode from, Edge edge, DateOnly today)
    {
        var path = from.Scope.LogPath;
        var edges = Read(path);
        var key = Key(from.Ref, edge.Rel, edge.Target);
        var before = edges.GetValueOrDefault(key) ?? new FoldedEdge(from.Ref, edge.Rel, edge.Target, 0, today);
        edges[key] = before.Travelled(today);
        Write(path, edges.Values);
    }

    /// <summary>Rewrites the log at <paramref name="path"/> folded and returns its line counts before and after.</summary>
    public static (int Before, int After) Fold(string path)
    {
        var before = File.ReadAllLines(path).Length;
        var edges = Read(path);
        Write(path, edges.Values);
        return (before, edges.Count + 1);
    }

    /// <summary>One folded edge per key, as of its latest travel: the sum of each travel's decay up to that day.</summary>
    private static IEnumerable<FoldedEdge> FoldRows(IEnumerable<FoldedEdge> rows) =>
        rows.GroupBy(row => row.Key, StringComparer.Ordinal).Select(group =>
        {
            var asOf = group.Max(row => row.AsOf);
            return group.First() with { Weight = group.Sum(row => Decay.Of(row.AsOf, asOf)), AsOf = asOf };
        });

    private static void Write(string path, IEnumerable<FoldedEdge> edges)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = edges.OrderBy(edge => edge.Key, StringComparer.Ordinal).Select(Line).Prepend(Header);
        AtomicFile.Write(path, string.Join(NodeSyntax.NewLine, lines) + NodeSyntax.NewLine);
    }

    private static string Line(FoldedEdge edge) => string.Join(
        Tab,
        edge.From,
        edge.Rel,
        edge.To,
        edge.Weight.ToString(WeightFormat, CultureInfo.InvariantCulture),
        edge.AsOf.ToString(FieldNames.DateFormat, CultureInfo.InvariantCulture));

    private static FoldedEdge? Parse(string line)
    {
        var cells = line.Split(Tab);
        return cells.Length == Columns
            && double.TryParse(cells[WeightColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            && TryDate(cells[AsOfColumn], out var asOf)
            ? new FoldedEdge(cells[0], cells[RelColumn], cells[ToColumn], weight, asOf)
            : null;
    }

    /// <summary>An old row of date, from, rel and to, as one travel of weight 1 on its date.</summary>
    private static FoldedEdge? ParseRow(string line)
    {
        var cells = line.Split(Tab);
        return cells.Length == RowColumns && TryDate(cells[0], out var date)
            ? new FoldedEdge(cells[1], cells[RelColumn + 1], cells[ToColumn + 1], 1, date)
            : null;
    }

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, FieldNames.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
