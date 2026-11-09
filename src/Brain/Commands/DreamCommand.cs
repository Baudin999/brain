using System.Globalization;
using Brain.Dreaming;

namespace Brain.Commands;

/// <summary>
/// <c>brain dream &lt;ref&gt;</c>: the one door to a dream. It distills the conversation's unsettled sittings into its
/// settled.md, marks them settled in state.md, and logs and commits the dream in the data repo. With <c>--deep</c> it
/// settles what is unsettled first and then runs a deep dream, the open agent embedding settled.md into the graph, each
/// write waiting for the user's yes in the window. brain's window runs the same code.
/// </summary>
internal static class DreamCommand
{
    public const string Name = "dream";
    private const string DeepFlag = "deep";
    private static readonly string Usage = Help.UsageOf(Name);

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.ParseFlagged(arguments, [DeepFlag]);
        var reference = options.Expect(1, Usage)[0];
        if (options.Has(DeepFlag))
        {
            return Deep(env, reference);
        }

        Settled(env, reference, Dreamer.Dream(env, reference, DateTimeOffset.UtcNow));
        return ExitCodes.Ok;
    }

    private static int Deep(BrainEnv env, string reference)
    {
        var dream = DeepDreamer.Dream(env, reference, TimeProvider.System);
        if (dream.Deep!.Settled is { } first)
        {
            Settled(env, reference, DreamLog.Find(env, first));
        }

        env.Out.WriteLine($"deep dream {dream.Id} on {reference}, {dream.Deep.Model} at {dream.Deep.Effort}, logged and committed in {DreamLog.PathOf(env, dream.Id)}");
        env.Out.WriteLine(dream.Deep.Words.Length > 0 ? dream.Deep.Words : "the agent said nothing at the end");
        BrainException.ThrowIf(dream.Why is { } why ? $"the deep dream failed: {why}" : null);
        return ExitCodes.Ok;
    }

    private static void Settled(BrainEnv env, string reference, DreamRecord dream)
    {
        var sittings = string.Join(", ", dream.Sittings.Select(number => number.ToString(CultureInfo.InvariantCulture)));
        env.Out.WriteLine($"dreamed {reference}: dream {dream.Id} settled sittings {sittings}");
        foreach (var draft in dream.Drafts)
        {
            env.Out.WriteLine($"  {draft.Model}: {(draft.Sure ? "sure" : "unsure, " + draft.Doubt)}");
        }

        env.Out.WriteLine(dream.Escalation is null
            ? $"  {dream.Outcome}: {dream.Drafts[^1].Model}'s draft is settled.md now"
            : $"  {dream.Outcome} to {dream.Escalation.Model}, whose draft is settled.md now: {dream.Why}");
        env.Out.WriteLine($"  logged and committed in {DreamLog.PathOf(env, dream.Id)}");
    }
}
