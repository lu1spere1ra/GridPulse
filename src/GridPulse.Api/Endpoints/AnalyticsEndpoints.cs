using GridPulse.Api.Hosting;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GridPulse.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder routes)
    {
        var analytics = routes.MapGroup("/analytics")
            .WithTags("Analytics")
            .RequireRateLimiting(RateLimitingExtensions.Reads)
            .CacheOutput(OutputCacheExtensions.Analytics);

        analytics.MapGet("/fleet", GetFleetAsync);
        analytics.MapGet("/top-consumers", GetTopConsumersAsync);
        return routes;
    }

    /// <summary>Fleet load curve and total energy over a range (rollups only; never raw data).</summary>
    internal static async Task<Results<Ok<FleetSummary>, ValidationProblem>> GetFleetAsync(
        DateTimeOffset from, DateTimeOffset to, TelemetryQueries queries, CancellationToken cancellationToken)
    {
        if (RangeQuery.Validate(from, to) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var resolution = ResolutionSelector.Choose(from, to, allowRaw: false);
        return TypedResults.Ok(await queries.GetFleetSummaryAsync(from, to, resolution, cancellationToken));
    }

    internal static async Task<Results<Ok<IReadOnlyList<TopConsumer>>, ValidationProblem>> GetTopConsumersAsync(
        DateTimeOffset from, DateTimeOffset to, TelemetryQueries queries, CancellationToken cancellationToken, int limit = 10)
    {
        if (RangeQuery.Validate(from, to) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await queries.GetTopConsumersAsync(from, to, Math.Clamp(limit, 1, 100), cancellationToken));
    }
}
