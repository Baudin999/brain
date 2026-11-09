using System.Globalization;

namespace Brain.Code;

/// <summary>The questions of one <c>brain ask</c>, as asked, and the answers the agent that waits for them waits on.</summary>
internal sealed record QuestionsWait(CodeQuestions Asked, Task<string> Answers);

/// <summary>
/// What one session asked the user through <c>brain ask</c>, in the order it asked, in memory only: each ask open until
/// the user answers or closes it in the window, or ended once the session ended or the user interrupted its turn.
/// Answered, closed and ended asks stay, so the window can draw them where they were asked. An agent that waits is
/// given the answers' text, or a refusal once its ask ended; an ask that comes after the session ended is ended at
/// once.
/// </summary>
internal sealed class SessionQuestions
{
    private readonly Lock gate = new();
    private readonly List<(CodeQuestions Asked, TaskCompletionSource<string> Answers)> asks = [];
    private int numbered;
    private string? ended;

    /// <summary>Adds the questions of <paramref name="document"/>, asked at <paramref name="asked"/> after the session's first <paramref name="at"/> events.</summary>
    public QuestionsWait Add(AskDocument document, int at, DateTimeOffset asked)
    {
        var answers = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            numbered++;
            var id = "question-" + numbered.ToString(CultureInfo.InvariantCulture);
            var questions = new CodeQuestions(id, document.Wait == true, at, asked, document.Questions ?? [], CodeQuestions.Open);
            if (ended is not null)
            {
                questions = questions with { State = CodeQuestions.Ended };
                answers.SetException(new BrainException(ended));
            }

            asks.Add((questions, answers));
            return new QuestionsWait(questions, answers.Task);
        }
    }

    /// <summary>
    /// Answers the open ask <paramref name="id"/> with <paramref name="replies"/> and gives the answered ask with the
    /// text the agent reads of the answers, which an agent that waits now has; refused when no such ask is open or the
    /// replies do not answer it.
    /// </summary>
    public (CodeQuestions Answered, string Text) Answer(string? id, IReadOnlyList<AskReply>? replies)
    {
        IReadOnlyList<AskReply> given = replies ?? [];
        return Settle(id, (open) =>
        {
            BrainException.ThrowIf(AskContract.ReplyProblem(open.Questions, given));
            var answered = open with { State = CodeQuestions.Answered, Replies = given };
            return (answered, AskContract.Answers(answered, given));
        });
    }

    /// <summary>
    /// Dismisses the open ask <paramref name="id"/>, closing it unanswered at the user's word, and gives the closed ask
    /// with the text the agent reads, which an agent that waits now has; refused when no such ask is open.
    /// </summary>
    public (CodeQuestions Closed, string Text) Dismiss(string? id)
    {
        return Settle(id, (open) =>
        {
            var closed = open with { State = CodeQuestions.Closed };
            return (closed, AskContract.Closed(closed));
        });
    }

    /// <summary>
    /// Settles the open ask <paramref name="id"/> as <paramref name="settle"/> says, under the lock, and gives the
    /// settled ask with the text, which an agent that waits now has; refused when no such ask is open.
    /// </summary>
    private (CodeQuestions Settled, string Text) Settle(string? id, Func<CodeQuestions, (CodeQuestions Settled, string Text)> settle)
    {
        (CodeQuestions Settled, string Text) result;
        TaskCompletionSource<string> answers;
        lock (gate)
        {
            var at = asks.FindIndex((one) => one.Asked.Id == id && one.Asked.State == CodeQuestions.Open);
            BrainException.ThrowIf(at < 0 ? $"no open questions {id} in this session" : null);
            answers = asks[at].Answers;
            result = settle(asks[at].Asked);
            asks[at] = (result.Settled, answers);
        }

        answers.SetResult(result.Text);
        return result;
    }

    /// <summary>
    /// Ends every open ask, and every one that comes from now on, with <paramref name="why"/>, and gives the asks it
    /// ended, which the user never answered.
    /// </summary>
    public IReadOnlyList<CodeQuestions> End(string why)
    {
        lock (gate)
        {
            ended ??= why;
        }

        return EndOpen(why);
    }

    /// <summary>
    /// Ends every open ask with <paramref name="why"/>, as an interrupt of the turn that asked does, and gives the asks
    /// it ended, which the user never answered; one that comes later is asked as any other.
    /// </summary>
    public IReadOnlyList<CodeQuestions> EndOpen(string why)
    {
        var waiting = new List<TaskCompletionSource<string>>();
        var unanswered = new List<CodeQuestions>();
        lock (gate)
        {
            for (var at = 0; at < asks.Count; at++)
            {
                if (asks[at].Asked.State != CodeQuestions.Open)
                {
                    continue;
                }

                var closed = asks[at].Asked with { State = CodeQuestions.Ended };
                waiting.Add(asks[at].Answers);
                unanswered.Add(closed);
                asks[at] = (closed, asks[at].Answers);
            }
        }

        foreach (var answers in waiting)
        {
            answers.SetException(new BrainException(why));
        }

        return unanswered;
    }

    /// <summary>Every ask, the oldest first.</summary>
    public IReadOnlyList<CodeQuestions> All()
    {
        lock (gate)
        {
            return [.. asks.Select((one) => one.Asked)];
        }
    }
}
