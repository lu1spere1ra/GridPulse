using GridPulse.Api.Hosting;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.Api.Endpoints;

public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder routes)
    {
        var alerts = routes.MapGroup("/alerts").WithTags("Alerts");
        alerts.MapGet("/", ListAsync).RequireRateLimiting(RateLimitingExtensions.Reads);
        alerts.MapPost("/{id:long}/acknowledge", AcknowledgeAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        return routes;
    }

    internal static async Task<Ok<List<AlertDto>>> ListAsync(
        GridPulseDbContext db, CancellationToken cancellationToken, AlertState? state = null, int? meterId = null, int limit = 100)
    {
        var alerts = db.Alerts.AsNoTracking();
        if (state is { } s)
        {
            alerts = alerts.Where(a => a.State == s);
        }

        if (meterId is { } id)
        {
            alerts = alerts.Where(a => a.MeterId == id);
        }

        var result = await alerts
            .OrderByDescending(a => a.RaisedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Join(db.AlertRules, a => a.RuleId, r => r.Id, (a, r) => new { a, r })
            .Join(db.Meters, x => x.a.MeterId, m => m.Id, (x, m) => new AlertDto(
                x.a.Id, x.a.RuleId, x.r.Name, x.r.Type, x.a.MeterId, m.Serial, x.a.State, x.a.RaisedAt, x.a.ResolvedAt, x.a.AcknowledgedAt, x.a.Message))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(result);
    }

    internal static async Task<Results<NoContent, NotFound>> AcknowledgeAsync(
        long id, GridPulseDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var updated = await db.Alerts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.AcknowledgedAt, a => a.AcknowledgedAt ?? now), cancellationToken);
        return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
