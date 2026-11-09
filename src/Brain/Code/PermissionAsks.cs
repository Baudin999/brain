using System.Globalization;

namespace Brain.Code;

/// <summary>A permission request that waits, and the answer its hook waits for.</summary>
internal sealed record PermissionWait(CodeAsk Ask, Task<CodeDecision> Answer);

/// <summary>
/// The permission requests of one session that wait for the user, in the order they came, in memory only. Each is
/// answered once: by the user in the window, or deny once the session ended or the user interrupted its turn, so no hook
/// waits on an agent that is gone.
/// A request that comes after the session ended is denied at once.
/// </summary>
internal sealed class PermissionAsks
{
    private readonly Lock gate = new();
    private readonly List<(CodeAsk Ask, TaskCompletionSource<CodeDecision> Answer)> waiting = [];
    private int numbered;
    private string? ended;

    /// <summary>Adds the request of <paramref name="asked"/>; the agent that sent its subagent is <paramref name="call"/>.</summary>
    public PermissionWait Add(CodePermission asked, string? call)
    {
        var answer = new TaskCompletionSource<CodeDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            numbered++;
            var ask = new CodeAsk("ask-" + numbered.ToString(CultureInfo.InvariantCulture), asked.Tool ?? string.Empty, asked.Input, asked.AgentType, asked.AgentId, call);
            if (ended is null)
            {
                waiting.Add((ask, answer));
            }
            else
            {
                answer.SetResult(CodeDecision.Deny(ended));
            }

            return new PermissionWait(ask, answer.Task);
        }
    }

    /// <summary>Answers the request <paramref name="id"/> with <paramref name="decision"/>; false when none of that id waits.</summary>
    public bool Answer(string? id, CodeDecision decision)
    {
        TaskCompletionSource<CodeDecision> answer;
        lock (gate)
        {
            var at = waiting.FindIndex((one) => one.Ask.Id == id);
            if (at < 0)
            {
                return false;
            }

            answer = waiting[at].Answer;
            waiting.RemoveAt(at);
        }

        answer.SetResult(decision);
        return true;
    }

    /// <summary>Denies every request that waits, and every one that comes from now on, with <paramref name="why"/>.</summary>
    public void End(string why)
    {
        lock (gate)
        {
            ended ??= why;
        }

        Deny(why);
    }

    /// <summary>Denies every request that waits with <paramref name="why"/>; one that comes later waits as any other.</summary>
    public void Deny(string why)
    {
        List<TaskCompletionSource<CodeDecision>> answers;
        lock (gate)
        {
            answers = [.. waiting.Select((one) => one.Answer)];
            waiting.Clear();
        }

        foreach (var answer in answers)
        {
            answer.SetResult(CodeDecision.Deny(why));
        }
    }

    /// <summary>The requests that wait, the oldest first.</summary>
    public IReadOnlyList<CodeAsk> All()
    {
        lock (gate)
        {
            return [.. waiting.Select((one) => one.Ask)];
        }
    }
}
