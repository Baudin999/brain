using System.Globalization;
using System.Text;

namespace Brain.Conversations;

/// <summary>
/// What a sitting's agent reads before the user's first words: which conversation it sits in and where its folder is,
/// then <c>settled.md</c> and the trail of every sitting no dream has settled yet. A settled sitting's agreement is in
/// <c>settled.md</c>, so its trail stays out. A sitting never resumes another: this is all it starts from.
/// </summary>
internal static class Seed
{
    public const string ResearchDir = "research";
    public const string TempDir = "TEMP";
    private const string Ignore = ".gitignore";

    /// <summary>Makes the folders a sitting works in, <c>research/</c> and <c>TEMP/</c>, and keeps TEMP out of git.</summary>
    public static void Prepare(Conversation conversation)
    {
        Directory.CreateDirectory(Path.Combine(conversation.Dir, ResearchDir));
        Directory.CreateDirectory(Path.Combine(conversation.Dir, TempDir));
        var ignore = Path.Combine(conversation.Dir, Ignore);
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, TempDir + "/\n");
        }
    }

    /// <summary>The seed and the user's first words in the sitting, as one message to the agent.</summary>
    public static string For(Conversation conversation, string words)
    {
        var sitting = conversation.State.Current;
        var dir = conversation.Dir;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"""
            brain serve started you for a sitting of a conversation in brain's window. The user talks with you there, not in a terminal, and what you write is drawn in the window's trail pane as markdown, mermaid included.

            Conversation: {conversation.Ref}, "{conversation.State.Title}", sitting {sitting.Number} on {sitting.Runs}.
            Its folder: {dir}
            - {Path.Combine(dir, ResearchDir)}/ holds what you found on the web: store each finding there as markdown, with its sources.
            - {Path.Combine(dir, TempDir)}/ is the only place a builder writes and runs a proof of concept you send it for. It is gitignored.
            - {Path.Combine(dir, ConversationStore.SettledFile)} is what the conversation agreed so far. You never write it: dreams do, and the user edits it by hand.
            - {Path.Combine(dir, Trail.Dir)}/ holds one trail per sitting. brain serve writes them from what is said here, and no session writes a trail.

            A user's phase can open with comments, each a quote line such as `> 1 on phase 6: "quoted text"` or `> 2 on settled: "quoted text"`, followed by the comment. Each is about that text in that phase of the sitting, or in settled.md. Answer every one of them, by its number. A line of the user's own that starts with > carries a backslash before it, and inside a comment a line holding only > is a blank line.

            Below are settled.md and the trails of the sittings no dream has settled yet, then the user's first words in this sitting.


            """);
        text.Append("<settled>\n").Append(Read(Path.Combine(dir, ConversationStore.SettledFile))).Append("\n</settled>\n");
        foreach (var number in conversation.State.Sittings.Where(one => one.Dream is null).Select(one => one.Number))
        {
            var trail = Trail.PathOf(dir, number);
            if (File.Exists(trail))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n<trail sitting=\"{number}\">\n").Append(Read(trail)).Append("\n</trail>\n");
            }
        }

        return text.Append("\nThe user:\n\n").Append(words).ToString();
    }

    private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
}
