using System.Globalization;
using Brain.Search;

namespace Brain.Staging;

/// <summary>
/// Where add, edit, update, merge and forget write their nodes. The write runs on a staged graph after every gate the
/// command holds it to, and what it would change becomes a pending write that waits for the user's yes: the command
/// prints the write's text and the id to land it with.
/// </summary>
internal static class WriteGate
{
    /// <summary>
    /// Stages what <paramref name="write"/> changes. The pending write keeps where it was staged from: the session, which
    /// <paramref name="session"/> names when the command was given one, and the project in effect.
    /// </summary>
    public static int Run(BrainEnv env, Action<Graph, TextWriter> write, string? session = null)
    {
        var graph = Graph.Stage(env);
        using var said = new StringWriter(CultureInfo.InvariantCulture);
        write(graph, said);
        var lines = said.ToString().Split(NodeSyntax.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var changes = graph.Staged!.Changes;
        if (changes.Count == 0)
        {
            foreach (var line in lines)
            {
                env.Out.WriteLine(line);
            }

            return ExitCodes.Ok;
        }

        var pending = new PendingStore(env.Home).Stage(lines, changes, new WriteOrigin(string.IsNullOrEmpty(session) ? env.Session : session));
        env.Out.Write(StagedText.Of(lines, changes));
        env.Out.WriteLine();
        env.Out.WriteLine(
            $"pending {pending.Id}: nothing is written yet. Run `brain land {pending.Id}` alone, as its own command, in the background: the user reads the write in brain's window and answers, and the command returns the answer.");
        ShowNearest(env, changes);
        return ExitCodes.Ok;
    }

    /// <summary>
    /// Under the staged write, the entries nearest to each node whose title, body or why it writes, so the agent sees a
    /// likely duplicate before the user does. The write is staged already: nothing here blocks or changes it, and a
    /// search that fails says so in one line.
    /// </summary>
    private static void ShowNearest(BrainEnv env, IReadOnlyList<StagedChange> changes)
    {
        foreach (var change in changes)
        {
            try
            {
                if (Nearest.ToChange(env, change, Nearest.Shown) is { } ranked)
                {
                    env.Out.WriteLine();
                    Nearest.Print(env, change.Ref, ranked);
                }
            }
            catch (BrainException failure)
            {
                env.Out.WriteLine($"nearest to {change.Ref}: not searched, {failure.Message}");
            }
        }
    }
}
