namespace GridPulse.Api.Endpoints;

/// <summary>Shared validation for <c>from</c>/<c>to</c> query parameters.</summary>
internal static class RangeQuery
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(90);

    public static Dictionary<string, string[]>? Validate(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return new() { ["to"] = ["'to' must be after 'from'."] };
        }

        return to - from > MaxRange
            ? new() { ["to"] = [$"The range cannot exceed {MaxRange.TotalDays} days."] }
            : null;
    }
}
