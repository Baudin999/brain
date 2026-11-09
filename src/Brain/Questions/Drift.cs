using System.Globalization;

namespace Brain.Questions;

/// <summary>
/// What does not add up in the code blocks of a question or of a source's context, worked out by brain so the window can
/// say so: a tally whose base and weights come to another value than the source says, and a mark that lies outside the
/// code shown, reaches outside the mark it sits in, overlaps the mark before it or does not read the text the source says
/// it does. brain adds every mark's weight, nested marks too, and knows the meaning of none of them.
/// </summary>
internal static class Drift
{
    /// <summary>Every drift of the blocks, each with the path to its block: a block's index, then a step's and a block's within it.</summary>
    public static IReadOnlyList<BlockDrift> Of(IReadOnlyList<QuestionBlock> blocks) =>
        [.. Walk(blocks, [], (block, at) => Of(block).Select(says => new BlockDrift(at, says)))];

    /// <summary>brain's sum for every code block with a tally, so the window draws it and never adds up on its own.</summary>
    public static IReadOnlyList<BlockSum> Sums(IReadOnlyList<QuestionBlock> blocks) =>
        [.. Walk(blocks, [], (block, at) => block.Type == QuestionBlock.CodeType && block.Tally is { } tally ? [Sum(at, tally, block.Marks ?? [])] : Array.Empty<BlockSum>())];

    /// <summary>brain's comparison for every facts block that asks for one, so the window draws it and never compares on its own.</summary>
    public static IReadOnlyList<BlockAgreement> Agreements(IReadOnlyList<QuestionBlock> blocks) =>
        [.. Walk(blocks, [], (block, at) => Agreement(at, block) is { } agreement ? [agreement] : Array.Empty<BlockAgreement>())];

    /// <summary>
    /// Whether the values of a facts block with agreement true are equal as strings, and each distinct value with its
    /// count in order of first appearance; null without the field and with fewer than two facts.
    /// </summary>
    public static BlockAgreement? Agreement(IReadOnlyList<int> at, QuestionBlock block)
    {
        if (block.Type != QuestionBlock.FactsType || block.Agreement != true || block.Facts is not { Count: > 1 } facts)
        {
            return null;
        }

        var split = facts.GroupBy(fact => fact.Value, StringComparer.Ordinal).Select(group => new ValueCount(group.Key, group.Count())).ToList();
        return new BlockAgreement(at, split.Count == 1, split);
    }

    private static BlockSum Sum(IReadOnlyList<int> at, Tally tally, IReadOnlyList<CodeMark> marks)
    {
        var weights = Sum(marks);
        return new BlockSum(at, weights, tally.Base + weights, tally.Base + weights == tally.Value);
    }

    /// <summary>What does not add up in one code block, in the order of its marks; none for another type.</summary>
    public static IReadOnlyList<string> Of(QuestionBlock block)
    {
        if (block.Type != QuestionBlock.CodeType || block.Code is null)
        {
            return [];
        }

        var says = new List<string>();
        var code = new CodeSpan(block.Code, block.StartLine ?? 1);
        Check(code, block.Marks ?? [], null, says);
        if (block.Tally is { } tally && Sum([], tally, block.Marks ?? []) is { AddsUp: false } sum)
        {
            says.Add($"{tally.Label}: base {Show(tally.Base)} + weights {Show(sum.Weights)} = {Show(sum.Total)}, and the source says {Show(tally.Value)}");
        }

        return says;
    }

    /// <summary>What <paramref name="each"/> finds in every block, a timeline's step blocks too, with the path to the block.</summary>
    private static IEnumerable<T> Walk<T>(IReadOnlyList<QuestionBlock> blocks, IReadOnlyList<int> path, Func<QuestionBlock, IReadOnlyList<int>, IEnumerable<T>> each)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            IReadOnlyList<int> at = [.. path, index];
            foreach (var found in each(blocks[index], at))
            {
                yield return found;
            }

            var steps = blocks[index].Type == QuestionBlock.TimelineType ? blocks[index].Steps ?? [] : [];
            for (var step = 0; step < steps.Count; step++)
            {
                foreach (var found in Walk(steps[step].Blocks, [.. at, step], each))
                {
                    yield return found;
                }
            }
        }
    }

    private static void Check(CodeSpan code, IReadOnlyList<CodeMark> marks, (CodeMark Mark, int From, int To)? parent, List<string> says)
    {
        (CodeMark Mark, int To)? before = null;
        foreach (var mark in marks.OrderBy(mark => mark.Start.Line).ThenBy(mark => mark.Start.Col))
        {
            var name = $"mark \"{mark.Label}\" at {Show(mark.Start)} to {Show(mark.End)}";
            var from = code.Offset(mark.Start);
            var to = code.Offset(mark.End);
            if (from is not { } start || to is not { } end)
            {
                says.Add($"{name} lies outside the code shown, lines {code.First} to {code.Last}");
                Check(code, mark.Marks ?? [], null, says);
                continue;
            }

            if (end <= start)
            {
                says.Add(end < start ? $"{name} ends before it starts" : $"{name} is empty");
            }
            else if (parent is { } outer && (start < outer.From || end > outer.To))
            {
                says.Add($"{name} reaches outside the mark \"{outer.Mark.Label}\" it sits in");
            }

            if (before is { } previous && start < previous.To)
            {
                says.Add($"{name} overlaps the mark \"{previous.Mark.Label}\" before it");
            }

            var reads = end >= start ? code.Text[start..end] : null;
            if (mark.Text is { } text && reads is not null && reads != text)
            {
                says.Add($"{name} says it reads \"{text}\", and the code there reads \"{reads}\"");
            }

            before = (mark, Math.Max(end, start));
            Check(code, mark.Marks ?? [], (mark, start, end), says);
        }
    }

    private static decimal Sum(IReadOnlyList<CodeMark> marks) => marks.Sum(mark => mark.Weight + Sum(mark.Marks ?? []));

    private static string Show(decimal number) => number.ToString("G29", CultureInfo.InvariantCulture);

    private static string Show(CodePosition position) => string.Create(CultureInfo.InvariantCulture, $"{position.Line}:{position.Col}");

    /// <summary>
    /// A code block as one string, its lines joined by \n with the one trailing \n the window does not show dropped, and
    /// the offset in it of each file position the block shows.
    /// </summary>
    internal sealed class CodeSpan
    {
        private readonly int[] starts;
        private readonly int[] lengths;

        public CodeSpan(string code, int first)
        {
            Text = code.EndsWith('\n') ? code[..^1] : code;
            var lines = Text.Split('\n');
            starts = new int[lines.Length];
            lengths = [.. lines.Select(line => line.Length)];
            for (var index = 1; index < lines.Length; index++)
            {
                starts[index] = starts[index - 1] + lengths[index - 1] + 1;
            }

            First = first;
        }

        public string Text { get; }

        public int First { get; }

        public int Last => First + starts.Length - 1;

        /// <summary>The offset of a line and a 1-based column; null when the block does not show it.</summary>
        public int? Offset(CodePosition position)
        {
            var line = position.Line - First;
            if (line < 0 || line >= starts.Length || position.Col < 1 || position.Col - 1 > lengths[line])
            {
                return null;
            }

            return starts[line] + position.Col - 1;
        }
    }
}

/// <summary>One thing that does not add up, and the path to its block within the question.</summary>
internal sealed record BlockDrift(IReadOnlyList<int> At, string Says);

/// <summary>brain's sum for a code block's tally: the weights of all its marks, base plus them, and whether that is the value.</summary>
internal sealed record BlockSum(IReadOnlyList<int> At, decimal Weights, decimal Total, bool AddsUp);

/// <summary>brain's comparison of a facts block's values: whether they are all equal, and each distinct value with its count.</summary>
internal sealed record BlockAgreement(IReadOnlyList<int> At, bool Agrees, IReadOnlyList<ValueCount> Split);

internal sealed record ValueCount(string Value, int Count);
