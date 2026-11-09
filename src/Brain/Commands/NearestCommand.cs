using Brain.Search;

namespace Brain.Commands;

/// <summary>
/// The entries nearest to a node, the lines every staged node write prints under it, asked on their own: for a node by
/// its ref, leaving the node itself out, or with <c>--scope</c> for a node's markdown not yet written, read as add and
/// edit read it, as it would land in that scope. It reads, so it travels no edge.
/// </summary>
internal static class NearestCommand
{
    public const string Name = "nearest";
    private static readonly string Usage = Help.UsageOf(Name);
    private const string ScopeOption = "scope";
    private const string FileOption = "file";
    private const string Document = "this markdown";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, ScopeOption, FileOption);
        if (options.Get(ScopeOption) is { } scopeName)
        {
            options.Expect(0, Usage);
            var file = options.Get(FileOption);
            var draft = NodeDraft.Parse(file is null ? env.In.ReadToEnd() : NodeContent.ReadFile(env, file));
            var content = draft.Content;
            var scope = ScopeResolver.InEffect(env).FirstOrDefault(scope => scope.Name == scopeName)
                ?? throw new BrainException(ScopeNames.IsScope(scopeName) ? $"no {scopeName} scope is in effect here" : $"{scopeName} is not a scope: personal, global or project");
            var document = Subjects.With(new Node(string.Empty, content.Title, [], draft.Edges, content.Body, content.Why, []), draft.Subjects);
            return Print(env, Document, Nearest.To(env, scope, document, Nearest.Shown));
        }

        if (options.Get(FileOption) is not null)
        {
            throw BrainException.Usage(Usage);
        }

        var node = Graph.Load(env).Require(options.Expect(1, Usage)[0]);
        return Print(env, node.Ref, Nearest.To(env, node.Scope, node.Node, Nearest.Shown));
    }

    private static int Print(BrainEnv env, string about, Ranked<Found> ranked)
    {
        Nearest.Print(env, about, ranked);
        env.Refs?.Return(ranked.Hits.Select(hit => hit.Node));
        return ExitCodes.Ok;
    }
}
