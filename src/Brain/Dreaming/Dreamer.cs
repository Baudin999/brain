using Brain.Conversations;

namespace Brain.Dreaming;

/// <summary>
/// A dream: distills a conversation's unsettled sittings into its <c>settled.md</c>. The dreamer setting's two models,
/// Haiku and Sonnet unless set, each draft the new settled.md from the same sittings, each at its own effort. When both
/// are sure, Sonnet compares the drafts; when they agree, the second model's draft lands. When they diverge or one is
/// unsure, Opus gets the sittings, both drafts and why, and its draft lands. The comparison and the escalation run at
/// medium effort, the model's own default. A dream
/// writes the conversation only, never the graph. state.md marks every sitting it settled with its id, and the log in
/// the data repo's <c>dreams/</c> keeps it and is committed. It never runs while a sitting is open, and it holds the
/// conversation's lock while it dreams, so no sitting starts meanwhile.
/// </summary>
internal static class Dreamer
{
    private static readonly (string Model, string Effort) Escalation = ("opus", "medium");
    private static readonly (string Model, string Effort) Comparer = ("sonnet", "medium");
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>The sittings no dream settled that hold a trail, the current one among them.</summary>
    public static IReadOnlyList<Sitting> Unsettled(Conversation conversation) =>
        [.. conversation.State.Sittings.Where(sitting => sitting.Dream is null && Said(conversation, sitting))];

    /// <summary>The current sitting while it is open: something was said in it and no new sitting ended it.</summary>
    public static Sitting? Open(Conversation conversation) =>
        conversation.State.Current is { Ended: null } current && Said(conversation, current) ? current : null;

    public static DreamRecord Dream(BrainEnv env, string reference, DateTimeOffset now)
    {
        var (held, conversation) = Hold(env, reference);
        using (held)
        {
            return Settle(env, conversation, now);
        }
    }

    /// <summary>
    /// Takes the conversation's lock for a dream and reads the conversation under it. Refuses at once when a dream holds
    /// the lock, and when a sitting is open, letting the lock go again.
    /// </summary>
    public static (IDisposable Held, Conversation Conversation) Hold(BrainEnv env, string reference)
    {
        var held = ConversationLock.Take(env, ConversationStore.Find(env, reference));
        try
        {
            var conversation = ConversationStore.Find(env, reference);
            BrainException.ThrowIf(Open(conversation) is { } open
                ? $"sitting {open.Number} of {reference} is open, and a dream never runs while a sitting is; end it first with Alt+T or brain conversation sitting"
                : null);
            return (held, conversation);
        }
        catch
        {
            held.Dispose();
            throw;
        }
    }

    /// <summary>Distills the unsettled sittings of <paramref name="conversation"/>, whose lock the caller holds through <see cref="Hold"/>.</summary>
    public static DreamRecord Settle(BrainEnv env, Conversation conversation, DateTimeOffset now)
    {
        var reference = conversation.Ref;
        var sittings = Unsettled(conversation);
        BrainException.ThrowIf(sittings.Count == 0 ? $"nothing to dream in {reference}: no sitting is unsettled" : null);
        Git.RefuseStaged(env.Home);

        var settled = Path.Combine(conversation.Dir, ConversationStore.SettledFile);
        var before = File.Exists(settled) ? File.ReadAllText(settled) : string.Empty;
        var trails = sittings.Select(sitting => (sitting.Number, Trail: File.ReadAllText(Trail.PathOf(conversation.Dir, sitting.Number)))).ToList();
        var task = DreamPrompt.Sittings(before, trails);
        var cheap = BrainSettings.Read(env.Home).Dreamer;
        var drafts = Task.WhenAll(cheap.Select(asked => Task.Run(() => DreamDraft.Parse(asked.Model, Ask(env, asked, DreamPrompt.Dreamer, task))))).GetAwaiter().GetResult();

        DreamComparison? comparison = null;
        string? why = drafts.FirstOrDefault(draft => !draft.Sure) is { } unsure ? $"{unsure.Model} was unsure: {unsure.Doubt}" : null;
        if (why is null)
        {
            comparison = DreamComparison.Parse(Comparer.Model, Ask(env, Comparer, DreamPrompt.Comparison, DreamPrompt.Drafts(drafts[0], drafts[1])));
            why = comparison.Agreed ? null : $"the drafts diverged: {comparison.Why}";
        }

        var escalation = why is null ? null : DreamDraft.Parse(Escalation.Model, Ask(env, Escalation, DreamPrompt.Escalation, DreamPrompt.Escalated(task, drafts[0], drafts[1], why)));
        BrainException.ThrowIf(escalation is { Text.Length: 0 } ? $"the dream failed, and nothing changed: {Escalation.Model} answered no draft" : null);
        var after = (escalation ?? drafts[^1]).Text;
        var id = DreamLog.NewId(env, now);
        var record = new DreamRecord(
            id,
            now,
            reference,
            conversation.State.Title,
            [.. sittings.Select(sitting => sitting.Number)],
            new DreamShape(sittings.Count, sittings.Sum(sitting => Trail.Read(Trail.PathOf(conversation.Dir, sitting.Number)).Count), trails.Sum(trail => trail.Trail.Length), before.Length),
            drafts,
            comparison,
            escalation is null ? DreamRecord.Agreed : DreamRecord.Escalated,
            why,
            escalation,
            before,
            after);

        // The log commits first, so a refusal at commit time, as when something was staged while the models thought,
        // leaves the conversation as it was and no log behind.
        DreamLog.Write(env, record);
        try
        {
            Git.CommitPath(env.Home, DreamLog.PathOf(env, id), skip: null, $"Dream {id} on {conversation.State.Title}: {record.Outcome}");
        }
        catch (BrainException refused)
        {
            File.Delete(DreamLog.PathOf(env, id));
            throw new BrainException($"the dream could not be committed, and nothing changed: {refused.Message}");
        }

        AtomicFile.Write(settled, after + "\n");
        var settledNow = sittings.Select(sitting => sitting.Number).ToHashSet();
        ConversationStore.Save(env, conversation, conversation.State with
        {
            Sittings = [.. conversation.State.Sittings.Select(sitting => settledNow.Contains(sitting.Number) ? sitting with { Dream = id } : sitting)],
        });
        return record;
    }

    private static bool Said(Conversation conversation, Sitting sitting) =>
        new FileInfo(Trail.PathOf(conversation.Dir, sitting.Number)) is { Exists: true, Length: > 0 };

    private static string Ask(BrainEnv env, (string Model, string Effort) asked, string system, string prompt)
    {
        var (model, effort) = asked;
        try
        {
            return env.Models.Ask(model, effort, system, prompt, Timeout);
        }
        catch (BrainException silent)
        {
            throw new BrainException($"the dream failed, and nothing changed: {model} gave no answer: {silent.Message}");
        }
    }
}
