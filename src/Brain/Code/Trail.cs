using System.Text.RegularExpressions;

namespace Brain.Code;

/// <summary>
/// The trail of one piece of work, read from the transcripts of its builder and of the validators sent for it, as a
/// story of steps in the order they happened. The builder's report is what it handed back, or sent the validator, before
/// a validator first wrote to it; each message a validator sent the builder is a round's findings; what the builder sent
/// or handed back after each is its answer, with the commits its Bash calls made meanwhile; and each validator's own
/// report closes its part, with the range it fast-forwarded the default branch by when it landed the work.
/// </summary>
internal static partial class Trail
{
    public const string Report = "report";
    public const string Findings = "findings";
    public const string Answer = "answer";
    public const string Verdict = "verdict";

    private const string Send = "SendMessage";
    private const string Handback = "SubagentHandback";
    private const string Bash = "Bash";

    /// <summary>
    /// The steps of the work of <paramref name="builder"/> and of each validator sent for it, the earliest first. A
    /// finished agent's last words stand for the report it gave no tool.
    /// </summary>
    // navarone: 0000192
    public static IReadOnlyList<CodeStep> Of(Worker builder, IReadOnlyList<Worker> validators)
    {
        var ids = validators.Select(validator => validator.Agent).ToHashSet(StringComparer.Ordinal);
        var steps = BuilderSteps(builder, ids);
        foreach (var validator in validators)
        {
            steps.AddRange(ValidatorSteps(builder.Agent, validator));
        }

        return InOrder(steps);
    }

    /// <summary>The builder's report and its answers: one for each turn between the messages its validators sent it.</summary>
    private static List<CodeStep> BuilderSteps(Worker builder, HashSet<string> validators)
    {
        var turns = new List<Turn>();
        var turn = new Turn(CallsOf(builder.Events, Bash));
        foreach (var one in builder.Events)
        {
            if (one.Kind == CodeEvent.Message && validators.Contains(one.Id ?? string.Empty))
            {
                turn.End();
                turns.Add(turn);
                turn = new Turn(turn.Bash);
                continue;
            }

            turn.Take(one);
        }

        if (builder.Done)
        {
            turn.End();
        }

        turns.Add(turn);
        var steps = new List<CodeStep>();
        for (var round = 0; round < turns.Count; round++)
        {
            if (turns[round].Words() is not { } said)
            {
                continue;
            }

            var kind = Answer;
            if (round == 0)
            {
                kind = Report;
            }

            steps.Add(new CodeStep(kind, builder.Agent, said.At, said.Text, said.Commits, Landed: null, round));
        }

        return steps;
    }

    /// <summary>The steps in the order they happened, each findings numbered by its place among them, from round 1.</summary>
    private static List<CodeStep> InOrder(List<CodeStep> steps)
    {
        var told = new List<CodeStep>();
        var round = 0;
        foreach (var step in steps.OrderBy(step => step.At ?? DateTimeOffset.MaxValue))
        {
            if (step.Kind != Findings)
            {
                told.Add(step);
                continue;
            }

            round++;
            told.Add(step with { Round = round });
        }

        return told;
    }

    /// <summary>A validator's findings, each message it sent the builder, and its report, which says whether it landed the work.</summary>
    private static List<CodeStep> ValidatorSteps(string builder, Worker validator)
    {
        var steps = new List<CodeStep>();
        var turn = new Turn(CallsOf(validator.Events, Bash));
        foreach (var one in validator.Events)
        {
            if (FindingsOf(one, builder) is { } findings)
            {
                steps.Add(new CodeStep(Findings, validator.Agent, one.At, findings, [], Landed: null, Round: null));
                continue;
            }

            turn.Take(one);
        }

        if (validator.Done)
        {
            turn.End();
        }

        if (turn.Words() is { } said)
        {
            steps.Add(new CodeStep(Verdict, validator.Agent, said.At, said.Text, [], turn.Landed, Round: null));
        }

        return steps;
    }

    /// <summary>The findings a call sends <paramref name="builder"/>, or null for a call that sends it none.</summary>
    private static string? FindingsOf(CodeEvent call, string builder)
    {
        if (call.Kind != CodeEvent.Tool || call.Name != Send)
        {
            return null;
        }

        if (call.Subject != builder)
        {
            return null;
        }

        return call.Text;
    }

    /// <summary>The ids of the calls of the tool <paramref name="tool"/>.</summary>
    private static HashSet<string> CallsOf(IReadOnlyList<CodeEvent> events, string tool)
    {
        var calls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var one in events)
        {
            if (one.Kind == CodeEvent.Tool && one.Name == tool)
            {
                calls.Add(one.Id ?? string.Empty);
            }
        }

        return calls;
    }

    /// <summary>What an agent said for a turn, when, and the commits it made in it.</summary>
    private sealed record TurnWords(string Text, DateTimeOffset? At, IReadOnlyList<CodeCommit> Commits);

    /// <summary>
    /// One turn of an agent: the last message it sent or handed back, its last words, the commits its Bash calls made,
    /// and the range one of them fast-forwarded a branch by. Once it ended, its last words stand for a message it never
    /// sent.
    /// </summary>
    private sealed partial class Turn(HashSet<string> bash)
    {
        private readonly List<CodeCommit> commits = [];
        private (string Text, DateTimeOffset? At)? message;
        private (string Text, DateTimeOffset? At)? said;
        private bool ended;

        /// <summary>The ids of the agent's Bash calls, whose results are read for commits and landings.</summary>
        public HashSet<string> Bash { get; } = bash;

        /// <summary>The range a Bash call of the turn fast-forwarded a branch by, the last when there are more; null for none.</summary>
        public string? Landed { get; private set; }

        public void Take(CodeEvent one)
        {
            switch (one.Kind)
            {
                case CodeEvent.Tool:
                    Called(one);
                    break;
                case CodeEvent.Said:
                    said = (one.Text!, one.At);
                    break;
                case CodeEvent.Result:
                    Gave(one);
                    break;
                default:
                    break;
            }
        }

        /// <summary>The turn is over: the agent wrote again after it, or finished.</summary>
        public void End() => ended = true;

        /// <summary>What the agent said for the turn: its message, or, once the turn ended, its last words; null when it said nothing yet.</summary>
        public TurnWords? Words()
        {
            if (message is { } sent)
            {
                return new TurnWords(sent.Text, sent.At, commits);
            }

            if (ended && said is { } last)
            {
                return new TurnWords(last.Text, last.At, commits);
            }

            return null;
        }

        /// <summary>A call that sends a message, to another agent or as the agent's report, is the turn's message.</summary>
        private void Called(CodeEvent call)
        {
            if (call.Text is null)
            {
                return;
            }

            if (call.Name is Send or Handback)
            {
                message = (call.Text, call.At);
            }
        }

        /// <summary>A Bash call's result: the commits git printed, and the range of a fast-forward.</summary>
        private void Gave(CodeEvent result)
        {
            if (result.Text is null || !Bash.Contains(result.Id ?? string.Empty))
            {
                return;
            }

            foreach (Match commit in CommitLine().Matches(result.Text))
            {
                commits.Add(new CodeCommit(commit.Groups["hash"].Value, commit.Groups["subject"].Value.Trim()));
            }

            Landed = FastForward(result.Text) ?? Landed;
        }

        /// <summary>The range a fast-forward moved a branch by, as git prints it, <c>a..b</c>, or null for output that is none.</summary>
        private static string? FastForward(string output)
        {
            var moved = FastForwardLine().Match(output);
            if (!moved.Success)
            {
                return null;
            }

            return moved.Groups["range"].Value;
        }

        [GeneratedRegex(@"^Updating (?<range>[0-9a-f]{7,40}\.\.[0-9a-f]{7,40})\s*\r?\nFast-forward", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
        private static partial Regex FastForwardLine();

        /// <summary>The line a git commit opens with: <c>[branch hash] subject</c>.</summary>
        [GeneratedRegex(@"^\[[^\]\s]+(?: \(root-commit\))? (?<hash>[0-9a-f]{7,40})\] (?<subject>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
        private static partial Regex CommitLine();
    }
}

/// <summary>An agent of a piece of work: its id, its events, and whether it finished.</summary>
internal sealed record Worker(string Agent, IReadOnlyList<CodeEvent> Events, bool Done);
