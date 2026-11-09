using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Brain.Questions;
using Brain.Stats;

namespace Brain.Serving;

/// <summary>
/// What the window's Navarone tab reads beside the navarone inbox's escalations: every problem Navarone raised, from
/// its event log, each with its story in log order and the project its repo is in projects.tsv; the code an event
/// names by its hash; and a lens on a method of a problem the window was last shown. A problem is a worktree and an id.
/// brain reads Navarone's commands and never writes its files. Without a navarone line, or with one whose events
/// command fails, there are no problems; the code and the lens refuse and say why.
/// </summary>
internal sealed partial class NavaroneTab(BrainEnv env)
{
    // The code under a hash never changes, so a hash found is asked for once for the life of the process.
    private static readonly ConcurrentDictionary<string, string> Stored = new(StringComparer.Ordinal);

    private readonly Lock shown = new();
    private Dictionary<(string Worktree, string Id), ProblemView> listed = [];

    /// <summary>Every problem the log tells a story of, the one with the latest event first.</summary>
    public IReadOnlyList<ProblemView> List()
    {
        var log = NavaroneLog.Read(env.Home) ?? [];
        var projects = ProjectRegistry.All(env.Home).ToList();
        var problems = new List<ProblemView>();
        foreach (var story in Stories(log))
        {
            var project = ProjectRegistry.At(projects, env.UserHome, story[0].Repo);
            problems.Add(ProblemView.Of(story, ReviewViews.Graphs(project)));
        }

        problems.Reverse();
        lock (shown)
        {
            listed = problems.ToDictionary(problem => (problem.Worktree, problem.Id));
        }

        return problems;
    }

    /// <summary>The code stored under a hash a shown problem's story names; throws a refusal for any other hash, or when Navarone holds none.</summary>
    public CodeView Code(string hash)
    {
        BrainException.ThrowIf(HashPattern().IsMatch(hash) ? null : $"{hash} is no hash of Navarone's code store");
        lock (shown)
        {
            BrainException.ThrowIf(listed.Values.Any(problem => problem.Names(hash)) ? null : $"no problem in this window names code {hash}; refresh the Navarone tab");
        }

        if (Stored.TryGetValue(hash, out var known))
        {
            return new CodeView(hash, known);
        }

        var source = NavaroneLog.Source(env.Home) ?? throw new BrainException($"{Sources.FileName} has no navarone line that runs");
        var command = source.Code ?? throw new BrainException($"{Sources.FileName} gives navarone no code command");
        var code = SourceCommand.Run(command, env.Home, [hash], SourceCommand.Timeout);
        Stored[hash] = code;
        return new CodeView(hash, code);
    }

    /// <summary>
    /// The lens on a shown problem's method for a query, run in the problem's worktree, or in its repo once the worktree
    /// is gone, with the query's words as arguments no shell parses. An escalation's finding line is lit.
    /// </summary>
    public ContextView Lens(string worktree, string id, string query)
    {
        ProblemView? problem;
        lock (shown)
        {
            problem = listed.GetValueOrDefault((worktree, id));
        }

        BrainException.ThrowIf(problem is null ? $"no problem {id} of {worktree} is in this window; refresh the Navarone tab" : null);
        var directory = Directory.Exists(problem!.Worktree) ? problem.Worktree : problem.Repo;
        BrainException.ThrowIf(Path.IsPathFullyQualified(directory) && Directory.Exists(directory) ? null : $"problem {id} names a worktree and a repo that are both gone: {problem.Worktree}");
        var source = NavaroneLog.Source(env.Home) ?? throw new BrainException($"{Sources.FileName} has no navarone line that runs");
        return ContextView.Of(SourceContext.Run(source, query, directory, SourceCommand.Timeout), problem.Focus());
    }

    /// <summary>The events of each problem the tab tells, each in log order, in the order each problem's last event happened.</summary>
    private static List<List<NavaroneEvent>> Stories(IReadOnlyList<NavaroneEvent> log) =>
        NavaroneLog.Stories(log.Where(one => StoryStep.Types.Contains(one.Type)));

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}

/// <summary>
/// One problem Navarone raised: its key, worktree and id; the repo and branch it was raised in; its check and symbol;
/// the symbol in the code's own words; the method as a lens names it, null for a symbol that is no method; where its
/// story stands now; the project of its repo as the window focuses it; and its story, every event of it in log order.
/// </summary>
internal sealed record ProblemView(
    string Worktree,
    string Id,
    string Repo,
    string? Branch,
    string Check,
    string Symbol,
    string Name,
    string? Method,
    string State,
    IReadOnlyList<GraphView> Graphs,
    IReadOnlyList<StoryStep> Story)
{
    public static ProblemView Of(List<NavaroneEvent> story, IReadOnlyList<GraphView> graphs)
    {
        var first = story[0];
        var symbol = story.Select(one => one.Symbol).FirstOrDefault(one => one is not null) ?? string.Empty;
        var check = story.Select(one => one.Check).FirstOrDefault(one => one is not null) ?? string.Empty;
        var steps = story.Select(StoryStep.Of).ToList();
        return new ProblemView(first.Worktree, first.Id!, first.Repo, first.Branch, check, symbol, NavaroneSymbol.Name(symbol), NavaroneSymbol.Method(symbol), steps[^1].Stands, graphs, steps);
    }

    /// <summary>True when a step of the story names the code under the hash.</summary>
    public bool Names(string hash) => Story.Any(step => step.Before == hash || step.After == hash);

    /// <summary>The line of the last escalation's finding, which a lens lights; none when the problem was never escalated.</summary>
    public IReadOnlyList<FocusLines> Focus()
    {
        var escalated = Story.LastOrDefault(step => step.Type == StoryStep.Escalated);
        if (escalated is not { Path: { } path, Line: > 0 and var line })
        {
            return [];
        }

        return [new FocusLines(path, [(int)line])];
    }
}

/// <summary>
/// One event of a problem's story, as the window tells it: its type and time, and the fields that type holds. Before
/// and After name code by its hash, which the window asks for only when the story opens.
/// </summary>
internal sealed record StoryStep(
    string Type,
    DateTimeOffset At,
    string? Before,
    string? After,
    string? Reason,
    string? Finding,
    string? Text,
    string? User,
    string? Words,
    string? Path,
    long Line,
    bool SecondRound)
{
    public const string Escalated = "escalated";

    /// <summary>The events that tell a problem's story, and where the story stands after each, in the words the tab shows.</summary>
    private static readonly Dictionary<string, string> StandsAfter = new(StringComparer.Ordinal)
    {
        ["raised"] = "open",
        ["fixed"] = "fixed",
        ["resolved"] = "reasoned",
        ["refused-fix"] = "refused",
        ["refused-reason"] = "refused",
        ["accepted"] = "accepted",
        [Escalated] = "escalated",
        ["settled"] = "settled",
    };

    public static IReadOnlyCollection<string> Types => StandsAfter.Keys;

    /// <summary>Where the story stands once this step happened.</summary>
    [JsonIgnore]
    public string Stands => StandsAfter[Type];

    public static StoryStep Of(NavaroneEvent one) =>
        new(one.Type, one.At, one.Before, one.After, one.Reason, one.Finding, one.Text, one.User, one.Words, one.Path, one.Line, one.SecondRound);
}

/// <summary>The code Navarone stores under a hash, as it was written.</summary>
internal sealed record CodeView(string Hash, string Code);
