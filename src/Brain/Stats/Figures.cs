using System.Globalization;

namespace Brain.Stats;

/// <summary>How the metrics count and print: tokens estimated from characters, numbers, shares and days.</summary>
internal static class Figures
{
    /// <summary>Characters per token, the estimate every token count in brain stats is made with.</summary>
    public const int CharsPerToken = 4;

    public static long Tokens(long chars) => chars / CharsPerToken;

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A count and its noun, the noun in the singular for one: 1 time, 3 times.</summary>
    public static string Count(long value, string one, string many) => $"{Number(value)} {(value == 1 ? one : many)}";

    public static string Percent(long part, long whole) =>
        whole == 0 ? "0%" : (100.0 * part / whole).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The day of a usage line's time, as it was written, local to where it was written; null when it does not read.</summary>
    public static DateOnly? DayOf(string at) =>
        at.Length >= FieldNames.DateFormat.Length ? Day(at[..FieldNames.DateFormat.Length]) : null;

    /// <summary>A day written as <see cref="FieldNames.DateFormat"/>; null when it does not read.</summary>
    public static DateOnly? Day(string text) =>
        DateOnly.TryParseExact(text, FieldNames.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
