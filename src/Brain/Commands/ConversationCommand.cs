using System.Globalization;
using Brain.Conversations;

namespace Brain.Commands;

/// <summary>
/// <c>brain conversation</c>: the one door to a conversation's folder. <c>start</c> makes one in a scope or a project
/// with its first sitting, <c>park</c> turns a Claude Code session's words into an ended first sitting, <c>sitting</c>
/// ends the current sitting and starts the next, <c>rename</c> gives it a title,
/// <c>commit</c> commits its folder,
/// <c>remove</c> deletes it, and <c>list</c> rebuilds the index and prints every conversation. brain's window
/// runs the same code, so it never writes a folder itself.
/// </summary>
internal static class ConversationCommand
{
    public const string Name = "conversation";
    private const string Start = "start";
    private const string NewSitting = "sitting";
    private const string Park = "park";
    private const string Rename = "rename";
    private const string List = "list";
    private const string Commit = "commit";
    private const string Remove = "remove";
    private const string Model = "model";
    private const string Effort = "effort";
    private static readonly HelpEntry Entry = Help.Find(Name)!;

    public static int Run(BrainEnv env, IReadOnlyList<string> arguments)
    {
        var options = Options.Parse(arguments, Model, Effort);
        var positional = options.Positional;
        var verb = positional.Count > 0 ? positional[0] : string.Empty;
        var count = positional.Count;
        switch (verb)
        {
            case Start when count == 2:
                var (model, effort) = Sitting(env, options);
                Say(env, "started", ConversationStore.Start(env, positional[1], model, effort, DateTimeOffset.UtcNow));
                return ExitCodes.Ok;
            case Park when count == 3:
                Say(env, "parked", ConversationStore.Park(env, positional[2], positional[1], options.Get(Model), options.Get(Effort), BrainSettings.Read(env.Home).Sitting.Model, DateTimeOffset.UtcNow));
                return ExitCodes.Ok;
            case NewSitting when count == 2:
                var (next, at) = Sitting(env, options);
                Say(env, "new sitting in", ConversationStore.Sit(env, positional[1], next, at, DateTimeOffset.UtcNow));
                return ExitCodes.Ok;
            case Rename when count == 3 && options.Get(Model) is null && options.Get(Effort) is null:
                Say(env, "renamed", ConversationStore.Rename(env, positional[1], positional[2]));
                return ExitCodes.Ok;
            case Commit when count == 2 && options.Get(Model) is null && options.Get(Effort) is null:
                env.Out.WriteLine($"committed {positional[1]}: {ConversationStore.Commit(env, positional[1])}");
                return ExitCodes.Ok;
            case Remove when count == 2 && options.Get(Model) is null && options.Get(Effort) is null:
                var removed = ConversationStore.Remove(env, positional[1]);
                env.Out.WriteLine($"removed {removed.Ref}: {removed.State.Title}");
                env.Out.WriteLine($"  deleted {removed.Dir}; the deletion is not committed");
                return ExitCodes.Ok;
            case List when count == 1 && options.Get(Model) is null && options.Get(Effort) is null:
                Print(env, ConversationStore.Rebuild(env));
                return ExitCodes.Ok;
            default:
                throw BrainException.Usage(string.Join("; ", [Entry.Usage, .. Entry.Alternatives]));
        }
    }

    /// <summary>The model and effort the command names, else the ones the settings start a new sitting with.</summary>
    private static (string Model, string Effort) Sitting(BrainEnv env, Options options)
    {
        if (options.Get(Model) is { } model && options.Get(Effort) is { } effort)
        {
            return (model, effort);
        }

        var first = BrainSettings.Read(env.Home).Sitting;
        return (options.Get(Model) ?? first.Model, options.Get(Effort) ?? first.Effort);
    }

    private static void Say(BrainEnv env, string what, Conversation conversation)
    {
        var sitting = conversation.State.Current;
        env.Out.WriteLine($"{what} {conversation.Ref}: {conversation.State.Title}");
        env.Out.WriteLine($"  sitting {sitting.Number.ToString(CultureInfo.InvariantCulture)} on {sitting.Runs}, in {conversation.Dir}");
    }

    private static void Print(BrainEnv env, ConversationIndex index)
    {
        foreach (var conversation in index.Conversations)
        {
            var sittings = conversation.State.Sittings.Count.ToString(CultureInfo.InvariantCulture);
            env.Out.WriteLine($"{conversation.Ref}  {conversation.State.Title}  ({conversation.State.Status}, {sittings} sittings)");
        }

        foreach (var problem in index.Problems)
        {
            env.Err.WriteLine(problem);
        }

        env.Out.WriteLine($"conversations: {index.Conversations.Count.ToString(CultureInfo.InvariantCulture)}, {index.Problems.Count.ToString(CultureInfo.InvariantCulture)} unreadable");
    }
}
