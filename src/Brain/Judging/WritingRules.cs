namespace Brain.Judging;

/// <summary>
/// The rules the judge holds entries to: the first four bullets under "## Writing" in the brain skill, so the rules
/// have one home and the judge reads them at run time.
/// </summary>
internal static class WritingRules
{
    public const int Count = 4;
    private const string Heading = "## Writing";
    private const string Bullet = "- ";
    private const string CarriageReturn = "\r";

    /// <summary>The rules as bullets, one per line, or null when the skill text does not hold them.</summary>
    public static string? From(string skill)
    {
        var lines = skill.Replace(CarriageReturn, string.Empty, StringComparison.Ordinal).Split(NodeSyntax.NewLine);
        var section = lines
            .SkipWhile(line => !string.Equals(line.TrimEnd(), Heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith(NodeSyntax.HeadingPrefix, StringComparison.Ordinal));
        var rules = Bullets(section).Take(Count).ToList();
        return rules.Count == Count ? string.Join(NodeSyntax.NewLine, rules) : null;
    }

    /// <summary>Each bullet joined with its indented continuation lines into one line.</summary>
    private static IEnumerable<string> Bullets(IEnumerable<string> lines)
    {
        string? open = null;
        foreach (var line in lines)
        {
            if (line.StartsWith(Bullet, StringComparison.Ordinal))
            {
                if (open is not null)
                {
                    yield return open;
                }

                open = line.TrimEnd();
            }
            else if (open is not null && line.Trim().Length > 0)
            {
                open += ' ' + line.Trim();
            }
        }

        if (open is not null)
        {
            yield return open;
        }
    }
}
