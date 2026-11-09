using System.Globalization;
using Brain.Children;
using Brain.Conversations;
using Brain.Serving;

namespace Brain.Dreaming;

/// <summary>
/// A deep dream, a dream's second step: it embeds what a conversation settled into brain's graph. It runs only when the
/// user asks, from <c>brain dream &lt;ref&gt; --deep</c> or the window's "dream into brain" button. When the conversation
/// has sittings no dream settled, a dream settles them first, under the same lock. Then the open agent, the same one a
/// sitting and a terminal run, starts through <see cref="ClaudeRuns"/> in the conversation's place's repo, on the deep
/// dream setting, seeded with the conversation, its folder, settled.md and the task. It searches the graph and stages
/// each write through the brain CLI, and every write waits for the user's yes in the window, one node at a time. It
/// refuses while a sitting is open or a dream holds the lock, and holds the lock while it runs. It commits each write
/// that landed, as any open agent does, and never writes settled.md or a trail or commits the conversation's folder.
/// Its final words are logged in the data repo's <c>dreams/&lt;id&gt;.json</c>, which brain commits alone.
/// </summary>
internal static class DeepDreamer
{
    /// <summary>How long the dream waits on its agent before it looks again; it waits for as long as the agent works.</summary>
    private static readonly TimeSpan Look = TimeSpan.FromSeconds(20);

    public static DreamRecord Dream(BrainEnv env, string reference, TimeProvider time)
    {
        var (held, conversation) = Dreamer.Hold(env, reference);
        using (held)
        {
            var settled = Dreamer.Unsettled(conversation).Count > 0 ? Dreamer.Settle(env, conversation, time.GetUtcNow()) : null;
            conversation = settled is null ? conversation : ConversationStore.Find(env, conversation.Ref);
            Git.RefuseStaged(env.Home);
            var (model, effort) = BrainSettings.Read(env.Home).DeepDream;
            var now = time.GetUtcNow();
            var text = Settled(conversation);
            var (words, problem) = Run(env, conversation, model, effort, time);
            var id = DreamLog.NewId(env, now);
            var record = new DreamRecord(
                id,
                now,
                conversation.Ref,
                conversation.State.Title,
                [],
                new DreamShape(Sittings: 0, Phases: 0, Characters: 0, text.Length),
                [],
                Comparison: null,
                DreamRecord.Deeply,
                problem,
                Escalation: null,
                text,
                text,
                new DeepDream(model, effort, settled?.Id, words));
            DreamLog.Write(env, record);
            try
            {
                Git.CommitPath(env.Home, DreamLog.PathOf(env, id), skip: null, $"Deep dream {id} on {conversation.State.Title}");
            }
            catch (BrainException refused)
            {
                throw new BrainException($"the deep dream ended, but its log {DreamLog.PathOf(env, id)} could not be committed: {refused.Message}");
            }

            return record;
        }
    }

    /// <summary>What the agent reads first: which conversation it dreams, where its folder is, the task, and settled.md.</summary>
    public static string Brief(BrainEnv env, Conversation conversation)
    {
        var dir = conversation.Dir;
        var home = env.Home;
        return string.Create(CultureInfo.InvariantCulture, $"""
            brain started you for a deep dream of a conversation: step two of a dream, which embeds what the conversation settled into brain's graph. The user asked for it. Nobody talks with you while you run; the user answers each write you stage in brain's window.

            Conversation: {conversation.Ref}, "{conversation.State.Title}".
            Its folder: {dir}
            - {Path.Combine(dir, ConversationStore.SettledFile)} is what the conversation agreed. It comes first, and it is below.
            - {Path.Combine(dir, Trail.Dir)}/ holds the trail of every sitting, {Path.Combine(dir, Seed.ResearchDir)}/ what was found on the web, and {Path.Combine(dir, ConversationStore.StateFile)} the sittings. Read any of them when settled.md leaves something unclear.

            The task:
            1. For each subject settled.md settles, search the graph with brain lookup, node and walk.
            2. Update the nodes it made stale, forget the nodes it made needless, and add what is new and belongs in brain. Leave out what the graph already holds and what is still open.
            3. Stage one write at a time through the brain CLI, and land it before you stage the next, so the user can refuse one change and keep the rest. Nobody comes back to you after this turn, so land in the foreground: run `brain land <id>` alone, with the Bash tool's longest timeout, and when it times out, run it again until it prints the user's answer. On a no with words, rewrite the node from their words and stage it again.
            4. Commit each write once it lands, as any open agent does, with nothing else staged. A write in a project's graph: `git -C <repo> add docs/brain/graph`, then `git -C <repo> commit -m "<what landed>"`, as two separate commands. A write in personal or global: the same on the data repo, `git -C {home} add graph`, then `git -C {home} commit -m "<what landed>"`. Never stage or commit the conversation's folder.
            5. Never write settled.md, state.md or a trail. brain logs this dream itself.
            6. End with a short account: what landed, what the user refused, and what you left out and why.

            <settled>
            {Settled(conversation).Trim()}
            </settled>
            """);
    }

    private static string Settled(Conversation conversation)
    {
        var path = Path.Combine(conversation.Dir, ConversationStore.SettledFile);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>Runs the open agent on the task until its turn ends, then stops it and all it started; its final words, and why it failed when it did.</summary>
    private static (string Words, string? Problem) Run(BrainEnv env, Conversation conversation, string model, string effort, TimeProvider time)
    {
        var words = string.Empty;
        var place = new RunPlace(WindowTabs.Dreams, AgentEntry.Dream, conversation.Ref, "deep dream of " + conversation.Ref);
        var start = new AgentStart(place, model, effort, [], brief => brief, phase => words = phase.Who == Trail.Agent ? phase.Text : words);
        var agent = OpenAgent.Start(start, env.Claude, ConversationPlaces.Named(env, conversation.Place).Repo, time);
        try
        {
            agent.Say(Brief(env, conversation), []);
            while (agent.Talk() is { Working: true } talk)
            {
                agent.WaitPast(talk.Version, Look);
            }

            return (words, agent.Talk().Problem);
        }
        finally
        {
            agent.Stop();
        }
    }
}
