using Brain.Staging;

namespace Brain.Commands;

/// <summary>
/// <c>brain land &lt;id&gt;</c>: the command a session runs to put a pending write before the user. It lands nothing by
/// itself. It lists the write in brain's window and blocks until the user answers it there, then returns the answer to
/// whoever ran it: what landed on a yes, a no, or a no with the user's own words on what to change. Run again, it waits
/// again or returns the answer already given.
/// </summary>
internal static class LandCommand
{
    public const string Name = "land";
    private static readonly string Usage = Help.UsageOf(Name);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var id = Options.Parse(arguments).Expect(1, Usage)[0];
        var store = new PendingStore(env.Home);
        if (store.AnswerOf(id) is null)
        {
            store.Ask(id);
            env.Out.WriteLine($"pending {id} is before the user in brain's window, which `brain ui` opens; this command returns the answer");
            env.Out.Flush();
        }

        using var notice = WaitingNotice.FromEnvironment(store);
        return Say(env, Wait(store, notice, id));
    }

    private static PendingAnswer Wait(PendingStore store, WaitingNotice notice, string id)
    {
        while (true)
        {
            if (store.AnswerOf(id) is { } answer)
            {
                return answer;
            }

            if (!store.IsAsked(id))
            {
                throw new BrainException($"pending write {id} is gone without an answer: a day passed, or its files were removed; stage it again");
            }

            notice.Tick();
            Thread.Sleep(Poll);
        }
    }

    private static int Say(BrainEnv env, PendingAnswer answer)
    {
        switch (answer.Verdict)
        {
            case PendingAnswer.Yes:
                foreach (var line in answer.Lines)
                {
                    env.Out.WriteLine(line);
                }

                env.Out.WriteLine($"landed {answer.Id} on the user's yes");
                return ExitCodes.Ok;
            case PendingAnswer.No when answer.Reason is { } reason:
                env.Out.WriteLine($"the user said no to {answer.Id} and said what to change, in their own words:");
                env.Out.WriteLine(reason);
                env.Out.WriteLine("Nothing landed. Rewrite the node from their words and stage it again; the window shows it as a new write.");
                return ExitCodes.Ok;
            case PendingAnswer.No:
                env.Out.WriteLine($"the user said no to {answer.Id}: nothing landed. Leave it; do not stage it again unasked.");
                return ExitCodes.Ok;
            default:
                throw new BrainException(answer.Refusal ?? $"pending write {answer.Id} did not land");
        }
    }
}
