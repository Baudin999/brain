using Brain.Stats;
using Brain.Windows;

namespace Brain.Code;

/// <summary>
/// The Navarone signals one piece of work touched, read through Navarone's own commands: every problem with an event on
/// the worktree its builder built in, each with how it ended, from the event log; and whether it is a clash that waits
/// for a judge, from Navarone's disputes, never worked out again here. brain reads them and never writes them. A read
/// runs both commands, so the signals of a worktree are read again only once the mark the caller gives changed: the
/// count of the work's calls that ran Navarone, the one way the log can change for it.
/// </summary>
internal sealed class WorkSignals(string home, Records records)
{
    /// <summary>Not answered yet.</summary>
    public const string Open = "open";

    /// <summary>The builder changed the code, and the signal is gone or the validator kept the fix.</summary>
    public const string Fixed = "fixed";

    /// <summary>The builder gave a reason the validator has not read yet.</summary>
    public const string Answered = "answered";

    /// <summary>The validator kept the builder's reason.</summary>
    public const string Kept = "kept";

    /// <summary>The validator refused the builder's answer with its finding: the builder is to change the code.</summary>
    public const string Replaced = "replaced";

    /// <summary>The builder and the validator clash, as Navarone's disputes say, until a judge decides.</summary>
    public const string Clash = "clash";

    /// <summary>The user judged it.</summary>
    public const string Judged = "judged";

    /// <summary>The real fix is a large change, bigger than the task, which goes to the user.</summary>
    public const string Large = "large";

    private readonly Lock gate = new();
    private readonly Dictionary<string, (long Mark, CodeSignals Signals)> read = new(StringComparer.Ordinal);

    /// <summary>The signals of <paramref name="worktree"/>, read again when <paramref name="mark"/> changed since the last read.</summary>
    // navarone: 0000195
    public CodeSignals Of(string worktree, long mark)
    {
        lock (gate)
        {
            if (read.TryGetValue(worktree, out var known) && known.Mark == mark)
            {
                return known.Signals;
            }
        }

        var signals = new CodeSignals(Read: false, []);
        var log = records.Time("navarone", "events " + worktree, null, () => NavaroneLog.Read(home));
        if (log is not null)
        {
            var clashes = records.Time("navarone", "disputes " + worktree, null, () => NavaroneLog.Clashes(home));
            signals = new CodeSignals(Read: true, Ended(log, clashes ?? new HashSet<(string, string)>(), worktree));
        }

        lock (gate)
        {
            read[worktree] = (mark, signals);
        }

        return signals;
    }

    /// <summary>
    /// Every problem of the log on <paramref name="worktree"/>, in the order each last happened, with how it ended; a
    /// problem <paramref name="clashes"/> holds waits for a judge.
    /// </summary>
    internal static IReadOnlyList<CodeSignal> Ended(IReadOnlyList<NavaroneEvent> log, IReadOnlySet<(string Worktree, string Id)> clashes, string worktree)
    {
        var signals = new List<CodeSignal>();
        foreach (var events in NavaroneLog.Stories(log.Where(one => one.Worktree == worktree)))
        {
            var story = new Story(events[0].Id!);
            foreach (var one in events)
            {
                story.Take(one);
            }

            var signal = story.Signal();
            if (clashes.Contains((worktree, signal.Id)))
            {
                signal = signal with { Ended = Clash };
            }

            signals.Add(signal);
        }

        return signals;
    }

    /// <summary>One problem's events, told as where it stands after each.</summary>
    private sealed class Story(string id)
    {
        private string ended = Open;
        private string check = string.Empty;
        private string symbol = string.Empty;
        private string? reason;
        private string? finding;
        private string? judgement;
        private string? words;

        /// <summary>Where the story stands once <paramref name="one"/> happened; an event of a type no step names changes nothing.</summary>
        public void Take(NavaroneEvent one)
        {
            Named(one);
            switch (one.Type)
            {
                case "raised":
                    ended = Open;
                    break;
                case "fixed":
                    ended = Fixed;
                    break;
                case "resolved":
                    Resolved(one);
                    break;
                case "refused-reason":
                case "refused-fix":
                    Refused(one);
                    break;
                case "accepted":
                    Accepted();
                    break;
                case "large":
                    Escalated(one);
                    break;
                case "settled":
                    Settled(one);
                    break;
                default:
                    break;
            }
        }

        public CodeSignal Signal() => new(id, check, NavaroneSymbol.Name(symbol), ended, reason, finding, judgement, words);

        /// <summary>The check and the code the problem is on, as the events that name them say.</summary>
        private void Named(NavaroneEvent one)
        {
            check = one.Check ?? check;
            symbol = one.Symbol ?? symbol;
        }

        /// <summary>The builder gave a reason, which the validator reads next.</summary>
        private void Resolved(NavaroneEvent one)
        {
            ended = Answered;
            reason = one.Reason;
        }

        /// <summary>The validator refused the builder's answer with its finding.</summary>
        private void Refused(NavaroneEvent one)
        {
            ended = Replaced;
            finding = one.Finding;
        }

        /// <summary>The validator kept the builder's answer: its fix, or its reason.</summary>
        private void Accepted()
        {
            if (ended == Fixed)
            {
                return;
            }

            ended = Kept;
        }

        /// <summary>The real fix is a large change, whose text says what is wrong, where and what the fix is.</summary>
        private void Escalated(NavaroneEvent one)
        {
            ended = Large;
            reason = one.Text;
        }

        /// <summary>The user judged it, with his keep or replace and his words.</summary>
        private void Settled(NavaroneEvent one)
        {
            ended = Judged;
            judgement = one.User;
            words = one.Words;
        }
    }
}
