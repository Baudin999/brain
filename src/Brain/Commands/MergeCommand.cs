using Brain.Judging;
using Brain.Merge;
using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// Applies a worker report from stdin, all or nothing: nodes first, then subjects and edges. Every line is tried on a
/// rehearsal of the graph first, then the gate reads every new node with the subjects and edges its lines give it, the
/// judge in one call, and a new node with no subject line is refused. An existing node whose subjects the report sets
/// meets the free rules alone: subjects carry no prose. A line that cannot be read, a line that fails, or a refusal
/// applies nothing and exits 1. From the command line the whole block waits as one pending write for the user's yes.
/// </summary>
internal static class MergeCommand
{
    public const string Name = "merge";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Help.UsageOf(Name));
        var (ops, errors) = MergeParser.Parse(env.In);
        var ordered = ops.OrderBy(op => op.Stage).ToList();
        var rehearsal = Graph.Rehearse(env);
        var failures = errors.Concat(Failures(rehearsal, ordered, env.Today)).ToList();
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                env.Err.WriteLine(failure);
            }

            env.Out.WriteLine($"merge: {failures.Count} failed, nothing applied");
            return ExitCodes.Failure;
        }

        return WriteGate.Run(env, (graph, output) =>
        {
            JudgeGate.Free(env, graph, Retagged(graph, rehearsal, ops));
            JudgeGate.Pass(env, graph, NewNodes(graph, rehearsal, ops));
            foreach (var op in ordered)
            {
                output.WriteLine(op.Apply(graph, env.Today));
            }

            output.WriteLine($"merge: {ops.Count} applied, 0 failed");
        });
    }

    /// <summary>Tries every line on the rehearsal and says why each one that fails does, in report order.</summary>
    private static List<string> Failures(Graph rehearsal, IReadOnlyList<MergeOp> ordered, DateOnly today)
    {
        var failures = new List<(int Line, string Why)>();
        foreach (var op in ordered)
        {
            try
            {
                op.Apply(rehearsal, today);
            }
            catch (BrainException refusal)
            {
                failures.Add((op.LineNumber, $"line {op.LineNumber}: {refusal.Message}"));
            }
        }

        return [.. failures.OrderBy(failure => failure.Line).Select(failure => failure.Why)];
    }

    /// <summary>
    /// The nodes the report would create, with the subjects and edges the rehearsal gave them, as node writes: each
    /// needs a subject line. One it names that exists is skipped, so the gate does not read it.
    /// </summary>
    private static List<JudgedEntry> NewNodes(Graph graph, Graph rehearsal, IReadOnlyList<MergeOp> ops) =>
    [
        .. ops.OfType<NodeOp>()
            .Where(op => !NodeCreator.Exists(graph, op.Spec))
            .Select(op => (op.Spec, Node: rehearsal.Require(NodeRef.Qualified(op.Spec.Scope, op.Spec.Slug)).Node))
            .Select(made => JudgedEntry.Of(made.Spec) with { Subjects = Subjects.Of(made.Node), Edges = made.Node.Edges, NodeWrite = true }),
    ];

    /// <summary>The existing nodes whose subjects the report sets, as the rehearsal left them.</summary>
    private static List<JudgedEntry> Retagged(Graph graph, Graph rehearsal, IReadOnlyList<MergeOp> ops) =>
    [
        .. ops.OfType<SubjectsOp>()
            .Select(op => Ref.Parse(op.Node).Node is { } reference && graph.Find(reference) is { } node ? JudgedEntry.Of(node, rehearsal.Require(node.Ref).Node) : null)
            .OfType<JudgedEntry>()
            .DistinctBy(entry => entry.Ref, StringComparer.Ordinal),
    ];
}
