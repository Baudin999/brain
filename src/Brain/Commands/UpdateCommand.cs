using Brain.Judging;
using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// Rewrites the title, body, why, kind or subjects of an existing node in place, once the gate passes it. Every other
/// line stays; git keeps the old text. Each part is optional, but one must be asked. A change of subjects alone carries
/// no prose, so it meets the free rules and not the judge, and it lands at once. Any other change, from the command
/// line, waits as a pending write for the user's yes.
/// </summary>
internal static class UpdateCommand
{
    public const string Name = "update";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string KindOption = "kind";
    private const string WhyOption = "why";
    private const string BodyOption = "body";
    private const string SubjectsOption = "subjects";
    private const string Updated = "updated ";
    private const int TitleArgument = 1;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, KindOption, WhyOption, BodyOption, SubjectsOption);
        var positional = options.Positional;
        var asked = new Asked(
            positional.Count > TitleArgument ? positional[TitleArgument] : null,
            options.Get(BodyOption),
            options.Get(WhyOption),
            options.Get(KindOption),
            options.Get(SubjectsOption) is { } subjects ? Subjects.FromArgument(subjects) : null);
        if (positional.Count is < 1 or > 2 || asked.Nothing)
        {
            throw BrainException.Usage(Usage);
        }

        if (asked.SubjectsOnly)
        {
            var graph = Graph.Load(env);
            var tagged = graph.Require(positional[0]);
            var retagged = Revise(tagged.Node, asked);
            JudgeGate.Free(env, graph, [JudgedEntry.Of(tagged, retagged)]);
            graph.Save(tagged with { Node = retagged });
            env.Out.WriteLine(Updated + tagged.Ref);
            return ExitCodes.Ok;
        }

        return WriteGate.Run(env, (graph, output) =>
        {
            var node = graph.Require(positional[0]);
            var revised = Revise(node.Node, asked);
            JudgeGate.Pass(env, graph, [JudgedEntry.Of(node, revised)]);
            graph.Save(node with { Node = revised });
            output.WriteLine(Updated + node.Ref);
        });
    }

    /// <summary>The node with the asked changes.</summary>
    private static Node Revise(Node node, Asked asked)
    {
        BrainException.ThrowIf(asked.Title is null ? null : Titles.Problem(asked.Title));
        BrainException.ThrowIf(asked.Body is null ? null : Titles.TextProblem(NodeParts.Body, asked.Body));
        BrainException.ThrowIf(asked.Why is null ? null : Titles.TextProblem(NodeParts.Why, asked.Why));
        var kind = asked.Kind ?? node.Field(FieldNames.Kind) ?? string.Empty;
        var revised = node.WithField(new NodeField(FieldNames.Kind, kind)) with
        {
            Title = asked.Title?.Trim() ?? node.Title,
            Body = asked.Body?.Trim() ?? node.Body,
            Why = asked.Why?.Trim() ?? node.Why,
        };
        return asked.Subjects is null ? revised : Subjects.With(revised, asked.Subjects);
    }

    /// <summary>What an update asks to change; null for a part it leaves alone.</summary>
    private sealed record Asked(string? Title, string? Body, string? Why, string? Kind, IReadOnlyList<string>? Subjects)
    {
        public bool SubjectsOnly => Title is null && Body is null && Why is null && Kind is null;

        public bool Nothing => SubjectsOnly && Subjects is null;
    }
}
