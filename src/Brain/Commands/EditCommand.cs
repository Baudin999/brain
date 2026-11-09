using Brain.Judging;
using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// Rewrites a node from markdown on stdin or a file, the markdown of a node file: <c>subjects: [a, b]</c> in
/// frontmatter, the title line, the body, <c>## Why</c> and the why, and <c>## Edges</c>. The write replaces the node's
/// title, body, why, subjects and edges; a part left out is written empty, so an edge the edit does not carry goes. The
/// edges are checked as <c>brain link</c> checks them, the free rules and the judge read the node as it would be, and
/// the edges are linked through link's own code on the staged graph, so they land only with the node. From the command
/// line the write waits for the user's yes. Kind and the other fields stay; update changes the kind.
/// </summary>
internal static class EditCommand
{
    public const string Name = "edit";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string FileOption = "file";
    private const string Edited = "edited ";
    private const string Unchanged = "unchanged ";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, FileOption);
        var reference = options.Expect(1, Usage)[0];
        var path = options.Get(FileOption);
        var draft = NodeDraft.Parse(path is null ? env.In.ReadToEnd() : NodeContent.ReadFile(env, path));
        BrainException.ThrowIf(draft.Content.Problem());
        return WriteGate.Run(env, (graph, output) =>
        {
            var node = graph.Require(reference);
            var written = Subjects.With(node.Node, draft.Subjects) with
            {
                Title = draft.Content.Title,
                Body = draft.Content.Body,
                Why = draft.Content.Why,
                Edges = [],
            };
            List<Edge> edges = [.. draft.Edges.Select(edge => EdgeLinker.Checked(graph, node with { Node = written }, edge.Rel, edge.Target))];
            var revised = written with { Edges = [.. edges.Distinct()] };
            if (string.Equals(NodeRenderer.Render(revised), NodeRenderer.Render(node.Node), StringComparison.Ordinal))
            {
                output.WriteLine(Unchanged + node.Ref);
                return;
            }

            JudgeGate.Pass(env, graph, [JudgedEntry.Of(node, revised) with { NodeWrite = true }]);
            graph.Save(node with { Node = written });
            EdgeLinker.LinkAll(graph, node.Ref, draft.Edges);
            output.WriteLine(Edited + node.Ref);
        });
    }
}
