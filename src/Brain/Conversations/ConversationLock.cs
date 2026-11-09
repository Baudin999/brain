namespace Brain.Conversations;

/// <summary>
/// Keeps a dream and a sitting apart on one conversation, across processes: <c>brain dream</c> holds the lock while it
/// dreams, and a new sitting, a rename and the user's words take it for the moment they change the folder. The lock is
/// a file in the data repo's <c>cache/locks</c>, opened with no sharing, so the operating system lets it go when the
/// process that held it ends.
/// </summary>
internal static class ConversationLock
{
    private const string Dir = "locks";
    private const string Extension = ".lock";

    /// <summary>The lock on <paramref name="conversation"/>; refuses at once when a dream holds it.</summary>
    public static IDisposable Take(BrainEnv env, Conversation conversation) =>
        Hold(env, conversation, Extension, $"a dream runs on {conversation.Ref}; wait until it lands");

    /// <summary>
    /// The sitting lock: brain serve holds it for as long as a live sitting agent works on <paramref name="conversation"/>,
    /// so another process, such as a removal, sees the agent. Refuses at once when it is held.
    /// </summary>
    public static IDisposable TakeSitting(BrainEnv env, Conversation conversation) =>
        Hold(env, conversation, ".sitting" + Extension, $"brain serve has a live sitting agent on {conversation.Ref}; stop it first");

    private static FileStream Hold(BrainEnv env, Conversation conversation, string suffix, string refusal)
    {
        var dir = Path.Combine(env.Home, Layout.CacheDir, Dir);
        Directory.CreateDirectory(dir);
        try
        {
            return new FileStream(Path.Combine(dir, conversation.Place + "." + conversation.Slug + suffix), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new BrainException(refusal);
        }
    }
}
