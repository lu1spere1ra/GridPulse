using GridPulse.Api.Hosting;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Persistence;
using GridPulse.Infrastructure.Queries;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.Api.Endpoints;

public static class ReadingEndpoints
{
    public static IEndpointRouteBuilder MapReadingEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/meters/{id:int}/readings", GetSeriesAsync)
            .WithTags("Readings")
            .RequireRateLimiting(RateLimitingExtensions.Reads)
            .CacheOutput(OutputCacheExtensions.HistoricalReadings);
        return routes;
    }

    /// <summary>
    /// Power series of one meter. <c>resolution=Auto</c> (default) picks raw, 1-minute, 15-minute or
    /// hourly data so that the response stays at about 1,000 points at most.
    /// </summary>
    internal static async Task<Results<Ok<ReadingSeries>, NotFound, ValidationProblem>> GetSeriesAsync(
        int id,
        DateTimeOffset from,
        DateTimeOffset to,
        GridPulseDbContext db,
        TelemetryQueries queries,
        CancellationToken cancellationToken,
        Resolution resolution = Resolution.Auto)
    {
        if (RangeQuery.Validate(from, to) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (resolution == Resolution.Auto)
        {
            resolution = ResolutionSelector.Choose(from, to);
        }
        else if (resolution == Resolution.Raw && to - from > TimeSpan.FromHours(6))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["resolution"] = ["Raw data is limited to ranges of 6 hours."],
            });
        }

        if (!await db.Meters.AnyAsync(m => m.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var points = await queries.GetMeterSeriesAsync(id, from, to, resolution, cancellationToken);
        return TypedResults.Ok(new ReadingSeries(id, resolution, from, to, points));
    }
}
