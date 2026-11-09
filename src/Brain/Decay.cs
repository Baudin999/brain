namespace Brain;

/// <summary>How much one traversal still counts: it halves every 90 days.</summary>
internal static class Decay
{
    public const double HalfLifeDays = 90;
    public const double UserFactor = 2;
    private const double Half = 0.5;

    public static double Of(DateOnly when, DateOnly today) =>
        Math.Pow(Half, Math.Max(0, today.DayNumber - when.DayNumber) / HalfLifeDays);
}
