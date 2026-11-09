namespace Brain.Installing;

/// <summary>
/// The questions an install or uninstall asks, each on <paramref name="prompts"/>, answered by a line of
/// <paramref name="input"/>. With <paramref name="yes"/> each takes its yes, says so on <paramref name="output"/> and
/// reads nothing. Without a terminal, an answer that never comes counts as no, or as the default where there is one.
/// </summary>
internal sealed class Choices(TextReader input, TextWriter output, TextWriter prompts, bool yes)
{
    /// <summary>Every permission mode Claude Code starts in, with what it does, in the order the menu shows them.</summary>
    public static readonly (string Mode, string Meaning)[] Modes =
    [
        ("default", "asks before it edits a file or runs a command. Claude Code's own default."),
        ("acceptEdits", "edits files without asking, still asks before a command."),
        ("plan", "reads and plans, changes nothing."),
        ("auto", "a classifier approves safe actions and asks about risky ones."),
        ("dontAsk", "refuses whatever is not allowed in advance, and never asks."),
        ("bypassPermissions", "runs everything without asking. Only in a sandbox you can throw away."),
    ];

    /// <summary>True on y or yes; anything else, or no answer, is no.</summary>
    public bool Confirm(string question)
    {
        if (yes)
        {
            output.WriteLine(question + " yes (--yes)");
            return true;
        }

        prompts.Write(question + " [y/N] ");
        return Answer() is "y" or "yes";
    }

    /// <summary>True to replace what is yours on r; anything else, or no answer, keeps it. --yes replaces.</summary>
    public bool Replace(string target)
    {
        var question = $"Replace or keep {target}?";
        if (yes)
        {
            output.WriteLine(question + " replace (--yes)");
            return true;
        }

        prompts.Write(question + " [r/k] ");
        return Answer() is "r" or "replace";
    }

    /// <summary>A path, with <paramref name="fallback"/> on an empty answer or none; ~ at its start is the user's home.</summary>
    public string Location(string question, string fallback, string userHome)
    {
        if (yes)
        {
            output.WriteLine($"{question} {fallback} (--yes)");
            return fallback;
        }

        prompts.Write($"{question} [{fallback}] ");
        var answer = input.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer))
        {
            return fallback;
        }

        return System.IO.Path.GetFullPath(answer == "~" || answer.StartsWith("~/", StringComparison.Ordinal)
            ? System.IO.Path.Combine(userHome, answer.TrimStart('~').TrimStart('/'))
            : answer);
    }

    /// <summary>
    /// A permission mode from the menu, by its number or its name, asking again on anything else. An empty answer or none
    /// takes <paramref name="fallback"/>; so does --yes.
    /// </summary>
    public string Mode(string fallback)
    {
        if (yes)
        {
            output.WriteLine($"Permission mode: {fallback} (--yes)");
            return fallback;
        }

        for (var number = 1; number <= Modes.Length; number++)
        {
            prompts.WriteLine($"  {number}  {Modes[number - 1].Mode,-18} {Modes[number - 1].Meaning}");
        }

        prompts.WriteLine();
        while (true)
        {
            prompts.Write($"Permission mode [{fallback}]: ");
            var answer = input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer))
            {
                return fallback;
            }

            var picked = Modes.Select((each, at) => (each.Mode, Number: (at + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .FirstOrDefault(each => answer == each.Number || answer == each.Mode).Mode;
            if (picked is not null)
            {
                return picked;
            }

            prompts.WriteLine($"Pick a number from 1 to {Modes.Length} or a mode's name.");
        }
    }

    private string Answer() => (input.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
}
