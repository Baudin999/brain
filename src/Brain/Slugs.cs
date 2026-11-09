using System.Text.RegularExpressions;

namespace Brain;

/// <summary>The lowercase-hyphenated form every slug and every relation name takes.</summary>
internal static partial class Slugs
{
    private const string Hyphen = "-";

    public static bool IsValid(string slug) => Pattern().IsMatch(slug);

    /// <summary>The slug of a free-form name: lowercased, every run of other characters one hyphen; null when nothing is left.</summary>
    public static string? From(string name)
    {
        var slug = NonSlugRun().Replace(name.ToLowerInvariant(), Hyphen).Trim(Hyphen[0]);
        return IsValid(slug) ? slug : null;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugRun();
}
