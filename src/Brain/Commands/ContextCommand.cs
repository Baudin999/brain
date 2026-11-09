using System.Globalization;
using System.Text;
using Brain.Usage;

namespace Brain.Commands;

/// <summary>
/// The scopes in effect, with counts and paths: pointers for the session start, never content. When errors were written
/// to the usage log since the user was last told, a last line tells how many and the latest, once. The agents line names
/// the model and effort the settings give each closed agent the open agent sends, or why the settings cannot be read.
/// </summary>
internal static class ContextCommand
{
    public const string Name = "context";
    private const string HomeFormat = "brain     {0}";
    private const string ScopeFormat = "{0,-9} {1} nodes in {2}";
    private static readonly CompositeFormat HomeLine = CompositeFormat.Parse(HomeFormat);
    private static readonly CompositeFormat ScopeLine = CompositeFormat.Parse(ScopeFormat);
    private const string NoProject = "project   none here";
    private const string AgentsLabel = "agents    ";
    private const string Pointers = "use: brain lookup <words>, brain node <ref>, brain walk <ref>";

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        Options.ThrowIfAny(arguments, Help.UsageOf(Name));
        var graph = Graph.Load(env);
        env.Out.WriteLine(Format(HomeLine, env.Home));
        if (graph.Scopes.Named(ScopeNames.Project) is null)
        {
            env.Out.WriteLine(NoProject);
        }

        foreach (var scope in graph.Scopes)
        {
            var count = graph.Nodes.Count(node => node.Scope == scope);
            env.Out.WriteLine(Format(ScopeLine, scope.Name, count, scope.GraphDir));
        }

        env.Out.WriteLine(AgentsLabel + Agents(env.Home));
        env.Out.WriteLine(Pointers);
        if (Failures.Untold(env.Home, env.Session, env.Err) is { } errors)
        {
            env.Out.WriteLine(errors);
        }

        return ExitCodes.Ok;
    }

    private static string Agents(string home)
    {
        try
        {
            return string.Join(", ", BrainSettings.Read(home).Agents.Select(agent => $"{agent.Key} {agent.Model} {agent.Effort}"));
        }
        catch (BrainException error)
        {
            return error.Message;
        }
    }

    private static string Format(CompositeFormat format, params object?[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values);
}
