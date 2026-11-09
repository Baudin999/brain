using Brain.Search;
using Brain.Usage;

namespace Brain.Judging;

/// <summary>
/// The one point where add, update, edit and merge put their nodes before anything is written. The free rules read each
/// entry first and refuse without calling the model; the judge reads only what passes them, each entry with the
/// subjects in use in its scope and its nearest entries, found before the judge runs. A change that carries no prose,
/// such as a node's subjects, meets the free rules alone. The gate refuses an entry the judge refuses, one it finds
/// says the same as or contradicts a nearest entry, and a node write that carries no edge while the judge names edges
/// for it. A duplicate and a contradiction exclude each other, so a duplicate the judge also names as a contradiction
/// of the same nearest entry counts as the contradiction only. Each duplicate, contradiction and missing edge is
/// logged in the usage log as it is refused. A refusal leaves the graph as it was.
/// </summary>
internal static class JudgeGate
{
    private const string Join = ", ";

    public static void Pass(BrainEnv env, Graph graph, IReadOnlyList<JudgedEntry> entries)
    {
        Free(env, graph, entries);
        if (entries.Count == 0)
        {
            return;
        }

        List<JudgedEntry> read = [.. entries.Select(entry => Prepared(env, graph, entry))];
        var verdict = env.Judge.Judge(read);
        var problems = new List<string>();
        if (!verdict.Ok)
        {
            problems.Add(verdict.Reason);
        }

        problems.AddRange(Duplicates(env, read, verdict));
        problems.AddRange(Contradictions(env, read, verdict));
        problems.AddRange(MissingEdges(env, graph, read, verdict));
        if (problems.Count > 0)
        {
            throw new BrainException($"the judge refused: {string.Join(' ', problems)}");
        }
    }

    /// <summary>Holds every entry to the free rules, and calls no model.</summary>
    public static void Free(BrainEnv env, Graph graph, IReadOnlyList<JudgedEntry> entries)
    {
        var commits = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var scope = graph.Scopes.Require(entry.Scope);
            var repo = scope.Repo ?? env.Home;
            if (!commits.TryGetValue(repo, out var known))
            {
                known = commits[repo] = Git.Subjects(repo);
            }

            if (FreeRules.Problem(entry, known) is { } problem)
            {
                throw new BrainException($"refused {entry.Ref}: {problem}");
            }
        }
    }

    /// <summary>
    /// The entry with the subjects in use in its scope and its nearest entries, the query <c>brain nearest</c> asks.
    /// </summary>
    private static JudgedEntry Prepared(BrainEnv env, Graph graph, JudgedEntry entry)
    {
        var scope = graph.Scopes.Require(entry.Scope);
        var nearest = Nearest.To(env, scope, entry.Document(), Nearest.Shown);
        var inUse = Subjects.InUse(graph.NodesOf(scope));
        return entry with { Nearest = [.. nearest.Hits.Select(hit => hit.Node)], InUse = [.. inUse.Select(use => use.Subject)] };
    }

    /// <summary>
    /// Each duplicate the judge names between an entry and one of the nearest entries it was shown, and does not also
    /// name as a contradiction: an entry that contradicts a node does not say the same as it.
    /// </summary>
    private static List<string> Duplicates(BrainEnv env, IReadOnlyList<JudgedEntry> read, Verdict verdict)
    {
        var problems = new List<string>();
        foreach (var duplicate in verdict.Duplicates)
        {
            if (Contradicts(verdict, duplicate))
            {
                continue;
            }

            if (Shown(read, duplicate.Entry, duplicate.Node) is not { } entry)
            {
                continue;
            }

            Log(env, new UsageEvent(UsageLog.Now(), UsageEvent.Duplicate, env.Session)
            {
                Ref = entry.Ref,
                Title = entry.Title,
                Match = duplicate.Node,
            });
            problems.Add($"{entry.Ref} says the same as {duplicate.Node}, which the graph holds already.");
        }

        return problems;
    }

    /// <summary>Each contradiction the judge names between an entry and one of the nearest entries it was shown.</summary>
    private static List<string> Contradictions(BrainEnv env, IReadOnlyList<JudgedEntry> read, Verdict verdict)
    {
        var problems = new List<string>();
        foreach (var contradiction in verdict.Contradictions)
        {
            if (Shown(read, contradiction.Entry, contradiction.Node) is not { } entry)
            {
                continue;
            }

            Log(env, new UsageEvent(UsageLog.Now(), UsageEvent.Contradiction, env.Session)
            {
                Ref = entry.Ref,
                Title = entry.Title,
                Match = contradiction.Node,
                Reason = contradiction.What,
            });
            problems.Add($"{entry.Ref} contradicts {contradiction.Node}: {contradiction.What}");
        }

        return problems;
    }

    /// <summary>
    /// Each node write that carries no edge while the judge names edges for it: only an edge to one of the nearest
    /// entries it was shown that <c>brain link</c> would accept counts.
    /// </summary>
    private static List<string> MissingEdges(BrainEnv env, Graph graph, IReadOnlyList<JudgedEntry> read, Verdict verdict)
    {
        var problems = new List<string>();
        foreach (var entry in read.Where(entry => entry.NodeWrite && entry.Edges.Count == 0))
        {
            var holder = new PlacedNode(graph.Scopes.Require(entry.Scope), entry.Document());
            List<Edge> named =
            [
                .. verdict.Edges
                    .Where(edge => Shown(read, edge.Entry, edge.To) == entry)
                    .Select(edge => new Edge(edge.Rel, edge.To))
                    .Where(edge => Linkable(graph, holder, edge))
                    .Distinct(),
            ];
            if (named.Count == 0)
            {
                continue;
            }

            List<string> lines = [.. named.Select(edge => edge.Rel + NodeSyntax.Arrow + edge.Target)];
            Log(env, new UsageEvent(UsageLog.Now(), UsageEvent.MissingEdges, env.Session)
            {
                Ref = entry.Ref,
                Title = entry.Title,
                Edges = lines,
            });
            problems.Add($"{entry.Ref} carries no edge, and the judge names its edges: {string.Join(Join, lines)}. Carry them in the write and write it again.");
        }

        return problems;
    }

    /// <summary>
    /// The entry <paramref name="entryRef"/> names when <paramref name="nodeRef"/> is one of the nearest entries it was
    /// shown; null when the judge named a ref it was never shown, so its finding counts for nothing.
    /// </summary>
    private static JudgedEntry? Shown(IReadOnlyList<JudgedEntry> read, string entryRef, string nodeRef)
    {
        var entry = read.FirstOrDefault(entry => string.Equals(entry.Ref, entryRef, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        var near = entry.Nearest.Any(node => string.Equals(node.Ref, nodeRef, StringComparison.Ordinal));
        return near ? entry : null;
    }

    /// <summary>True when the verdict names the duplicate's entry and node under contradictions as well.</summary>
    private static bool Contradicts(Verdict verdict, Duplicate duplicate) =>
        verdict.Contradictions.Any(contradiction =>
            string.Equals(contradiction.Entry, duplicate.Entry, StringComparison.Ordinal)
            && string.Equals(contradiction.Node, duplicate.Node, StringComparison.Ordinal));

    /// <summary>True when the edge passes <see cref="EdgeLinker.Checked"/>, the check a carried edge meets.</summary>
    private static bool Linkable(Graph graph, PlacedNode holder, Edge edge)
    {
        try
        {
            EdgeLinker.Checked(graph, holder, edge.Rel, edge.Target);
            return true;
        }
        catch (BrainException)
        {
            return false;
        }
    }

    private static void Log(BrainEnv env, UsageEvent usage) => UsageLog.Append(env.Home, usage, env.Err);
}
