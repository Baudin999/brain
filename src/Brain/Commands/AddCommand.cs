using Brain.Judging;
using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// Creates one node, born today, with its subjects and its edges, once the gate passes it. A node is born as a
/// document: with no title argument, add reads the markdown edit reads, from stdin or <c>--file</c>, the markdown of a
/// node file: <c>subjects: [a, b]</c> in frontmatter, the title line, the body, <c>## Why</c> and the why, and
/// <c>## Edges</c>. With a title argument it takes the body, why and subjects from <c>--body</c>, <c>--why</c> and
/// <c>--subjects</c>, and carries no edge. The kind is information unless it asks for an idea. Every edge is checked as
/// <c>brain link</c> checks it before the gate, and linked through link's own code on the staged graph, so it lands
/// only with the node. From the command line the node waits as a pending write for the user's yes.
/// </summary>
internal static class AddCommand
{
    public const string Name = "add";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string KindOption = "kind";
    private const string NamedByOption = "named-by";
    private const string SessionOption = "session";
    private const string WhyOption = "why";
    private const string BodyOption = "body";
    private const string FileOption = "file";
    private const string SubjectsOption = "subjects";
    private const string Added = "added ";
    private const int SlugArgument = 1;
    private const int TitleArgument = 2;
    private const int Placed = 2;
    private const int Titled = 3;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, KindOption, NamedByOption, SessionOption, WhyOption, BodyOption, FileOption, SubjectsOption);
        var positional = options.Positional;
        var draft = Draft(env, options);
        var content = draft.Content;
        var spec = new NewNode(
            positional[0],
            positional[SlugArgument],
            content.Title,
            options.Get(KindOption) ?? NewNode.DefaultKind,
            options.Get(NamedByOption) ?? NewNode.DefaultNamer,
            options.Get(SessionOption))
        {
            Body = content.Body,
            Why = content.Why,
            Subjects = draft.Subjects,
        };
        return WriteGate.Run(env, (graph, output) =>
        {
            var drafted = NodeCreator.Draft(graph, spec, env.Today);
            List<Edge> edges = [.. draft.Edges.Select(edge => EdgeLinker.Checked(graph, drafted, edge.Rel, edge.Target))];
            JudgeGate.Pass(env, graph, [JudgedEntry.Of(spec) with { Edges = edges, NodeWrite = true }]);
            var node = NodeCreator.Create(graph, spec, env.Today);
            EdgeLinker.LinkAll(graph, node.Ref, draft.Edges);
            output.WriteLine(Added + node.Ref);
        }, spec.Session);
    }

    /// <summary>The write from the arguments, or from markdown when no title is given; a mix of both is refused.</summary>
    private static NodeDraft Draft(BrainEnv env, Options options)
    {
        var positional = options.Positional;
        var file = options.Get(FileOption);
        if (positional.Count == Titled && file is null)
        {
            var content = new NodeContent(positional[TitleArgument].Trim(), options.Get(BodyOption)?.Trim() ?? string.Empty, options.Get(WhyOption)?.Trim() ?? string.Empty);
            var subjects = options.Get(SubjectsOption) is { } list ? Subjects.FromArgument(list) : [];
            return new NodeDraft(content, subjects, []);
        }

        if (positional.Count == Placed && options.Get(BodyOption) is null && options.Get(WhyOption) is null && options.Get(SubjectsOption) is null)
        {
            return NodeDraft.Parse(file is null ? env.In.ReadToEnd() : NodeContent.ReadFile(env, file));
        }

        throw BrainException.Usage(Usage);
    }
}
