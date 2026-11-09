namespace Brain.Code;

/// <summary>
/// The name a session may be given, the title Claude Code's <c>/rename</c> stores in its transcript: once trimmed, one
/// to <see cref="MaxLength"/> ASCII letters and digits, and no other session of the window carries it, whatever the
/// case. The window's field checks the same rule as <c>web/components/session-list.js</c>.
/// </summary>
internal static class SessionName
{
    public const int MaxLength = 60;

    /// <summary>The trimmed <paramref name="name"/>; refused with why when it breaks the rule, or when <paramref name="others"/>, the names of the other sessions, hold it.</summary>
    public static string Checked(string? name, IEnumerable<string> others)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new BrainException("a name is empty");
        }

        if (!trimmed.All(char.IsAsciiLetterOrDigit))
        {
            throw new BrainException("a name holds only letters and digits");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new BrainException($"a name is at most {MaxLength} characters");
        }

        if (others.Any(other => string.Equals(other, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BrainException($"another session is named {trimmed}");
        }

        return trimmed;
    }
}
