namespace GridPulse.Core.Contracts;

/// <summary>
/// Picks the coarsest series resolution that still shows detail: the result stays at or below
/// about <see cref="MaxPoints"/> points, which is plenty for a chart and keeps responses small.
/// </summary>
public static class ResolutionSelector
{
    public const int MaxPoints = 1_000;

    /// <summary>Raw readings arrive about once per second, so 15 minutes of raw data is ~900 points.</summary>
    public static readonly TimeSpan MaxRawSpan = TimeSpan.FromMinutes(15);

    public static Resolution Choose(DateTimeOffset from, DateTimeOffset to, bool allowRaw = true)
    {
        var span = to - from;
        if (allowRaw && span <= MaxRawSpan)
        {
            return Resolution.Raw;
        }

        if (span.TotalMinutes <= MaxPoints)
        {
            return Resolution.OneMinute;
        }

        return span.TotalMinutes / 15 <= MaxPoints ? Resolution.FifteenMinutes : Resolution.OneHour;
    }

    public static TimeSpan BucketSize(Resolution resolution) => resolution switch
    {
        Resolution.OneMinute => TimeSpan.FromMinutes(1),
        Resolution.FifteenMinutes => TimeSpan.FromMinutes(15),
        Resolution.OneHour => TimeSpan.FromHours(1),
        _ => TimeSpan.Zero,
    };
}
