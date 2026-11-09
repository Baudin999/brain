using System.Text.Json;
using Brain.Questions;

namespace Brain.Stats;

/// <summary>
/// Navarone's event log as brain reads it: through the events command of the navarone line of <c>sources.tsv</c>, every
/// event in log order. brain reads the log and never writes it. The log is null without a navarone line that gives an
/// events command, and when that command fails or prints what brain cannot read, so brain works without Navarone.
/// </summary>
internal static class NavaroneLog
{
    public const string SourceName = "navarone";

    /// <summary>What ends a problem's id in the id of the question that asks the user to judge its clash: <c>&lt;id&gt;-&lt;hash&gt;</c>.</summary>
    private const char ClashIdEnd = '-';

    /// <summary>The navarone line of sources.tsv, null when there is none or it cannot run.</summary>
    public static Source? Source(string home)
    {
        var source = Sources.All(home).FirstOrDefault(one => one.Name == SourceName);
        if (source is not { Problem: null })
        {
            return null;
        }

        return source;
    }

    public static IReadOnlyList<NavaroneEvent>? Read(string home)
    {
        if (Source(home) is not { Events: { } command })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(SourceCommand.Run(command, home));
            return Events(document.RootElement);
        }
        catch (BrainException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The clashes that wait for the user's judgement, as Navarone's disputes command, the navarone line's own command,
    /// lists them: each as the worktree it is on and the id of its problem, which opens its question's id. Null without
    /// a navarone line, and when the command fails or prints what brain cannot read.
    /// </summary>
    public static IReadOnlySet<(string Worktree, string Id)>? Clashes(string home)
    {
        if (Source(home) is not { } source)
        {
            return null;
        }

        IReadOnlyList<Question> questions;
        try
        {
            questions = SourceReply.Parse(source.Name, SourceCommand.Run(source.Command, home)).Questions;
        }
        catch (BrainException)
        {
            return null;
        }

        var clashes = new HashSet<(string Worktree, string Id)>();
        foreach (var question in questions)
        {
            var id = question.Id.Split(ClashIdEnd)[0];
            clashes.Add((question.Repo, id));
        }

        return clashes;
    }

    /// <summary>
    /// The events of each problem, by worktree and id, each in log order, in the order each problem's last event
    /// happened. An id is granted per worktree, so a problem is a worktree and an id; an event of no problem is left out.
    /// </summary>
    public static List<List<NavaroneEvent>> Stories(IEnumerable<NavaroneEvent> log)
    {
        var stories = new Dictionary<(string, string), List<NavaroneEvent>>();
        foreach (var one in log)
        {
            if (one.Id is null)
            {
                continue;
            }

            var key = (one.Worktree, one.Id);
            if (!stories.TryGetValue(key, out var story))
            {
                story = [];
                stories[key] = story;
            }

            story.Add(one);
        }

        return [.. stories.Values.OrderBy(story => story[^1].Position)];
    }

    /// <summary>Every event the document lists that reads; null when it lists none as an array.</summary>
    private static List<NavaroneEvent>? Events(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("events", out var all) || all.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var events = new List<NavaroneEvent>();
        foreach (var one in all.EnumerateArray())
        {
            var parsed = NavaroneEvent.Parse(one, events.Count);
            if (parsed is not null)
            {
                events.Add(parsed);
            }
        }

        return events;
    }
}
