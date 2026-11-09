using System.Globalization;
using System.Text;

namespace Brain.Dreaming;

/// <summary>
/// What a dream asks. Each cheap dreamer gets settled.md and the trails of the sittings it settles, and writes the whole
/// new settled.md, saying first whether it is sure. A comparison reads the two drafts and says whether they agree. When
/// they diverge or a dreamer is unsure, the escalation gets the sittings, both drafts and why.
/// </summary>
internal static class DreamPrompt
{
    public const string Sure = "SURE";
    public const string Unsure = "UNSURE";
    public const string Agree = "AGREE";
    public const string Diverge = "DIVERGE";

    /// <summary>What goes into settled.md, for a dreamer and for the escalation alike.</summary>
    private const string Rules = """
        - Keep what settled.md holds, unless a sitting changed it; then write what was agreed instead.
        - Add what the sittings agreed: each decision or rule, with the reason given for it. A point is agreed only when the user agreed to it.
        - Add what the user rejected, with the reason, so it is not proposed again.
        - Leave out what is still open, what was only proposed, and how the talk went.
        - Never add what the sittings do not say.
        - Markdown: a heading per subject, one point per bullet, plain words, no em dashes.
        """;

    public const string Dreamer = $"""
        You are a dreamer of brain, a knowledge tool. A conversation is one topic, talked through over several sittings between the user and an agent. settled.md is what the conversation agreed so far. You distill the sittings you are given into the new settled.md.

        {Rules}

        Your first line is exactly {Sure}, or {Unsure}: and why, when the sittings leave unclear what was agreed. Every line after it is the whole new settled.md and nothing else.
        """;

    public const string Comparison = $"""
        Two dreamers distilled the same sittings of a conversation into settled.md, each on its own. Compare their drafts.

        Answer {Agree} when both settle the same points with the same meaning, whatever their wording and order. Answer {Diverge}: and what differs, when one settles a point the other lacks, or they settle a point differently.

        Your answer is one line: {Agree}, or {Diverge}: and what differs.
        """;

    public const string Escalation = $"""
        You settle a conversation where two cheap dreamers could not. A conversation is one topic, talked through over several sittings between the user and an agent. settled.md is what it agreed so far. Two dreamers each wrote a new settled.md from the same sittings, and they diverged or one was unsure. Read the sittings and write the settled.md they support.

        {Rules}

        Your first line is exactly {Sure}, or {Unsure}: and why. Every line after it is the whole new settled.md and nothing else.
        """;

    /// <summary>settled.md and each sitting's trail, as every dreamer reads them.</summary>
    public static string Sittings(string settled, IReadOnlyList<(int Number, string Trail)> trails)
    {
        var text = new StringBuilder();
        text.Append("<settled>\n").Append(settled.Trim()).Append("\n</settled>\n");
        foreach (var (number, trail) in trails)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n<trail sitting=\"{number}\">\n").Append(trail.Trim()).Append("\n</trail>\n");
        }

        return text.ToString();
    }

    /// <summary>The two drafts, for the comparison.</summary>
    public static string Drafts(DreamDraft first, DreamDraft second) =>
        $"<draft>\n{first.Text}\n</draft>\n\n<draft>\n{second.Text}\n</draft>\n";

    /// <summary>The sittings, both drafts and why the dream escalated, for the escalation.</summary>
    public static string Escalated(string sittings, DreamDraft first, DreamDraft second, string why) =>
        $"{sittings}\n{Drafts(first, second)}\nWhy it came to you: {why}\n";
}
