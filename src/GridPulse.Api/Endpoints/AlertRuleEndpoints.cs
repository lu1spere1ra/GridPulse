using GridPulse.Api.Hosting;
using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using GridPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.Api.Endpoints;

public static class AlertRuleEndpoints
{
    public static IEndpointRouteBuilder MapAlertRuleEndpoints(this IEndpointRouteBuilder routes)
    {
        var rules = routes.MapGroup("/alert-rules").WithTags("Alert rules");

        rules.MapGet("/", ListAsync).RequireRateLimiting(RateLimitingExtensions.Reads);
        rules.MapGet("/{id:int}", GetAsync).WithName("GetAlertRule").RequireRateLimiting(RateLimitingExtensions.Reads);
        rules.MapPost("/", CreateAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        rules.MapPut("/{id:int}", UpdateAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        rules.MapDelete("/{id:int}", DeleteAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        return routes;
    }

    internal static async Task<Ok<List<AlertRuleDto>>> ListAsync(GridPulseDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await db.AlertRules.AsNoTracking().OrderBy(r => r.Id).Select(r => ToDto(r)).ToListAsync(cancellationToken));

    internal static async Task<Results<Ok<AlertRuleDto>, NotFound>> GetAsync(int id, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        var rule = await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rule is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(rule));
    }

    internal static async Task<Results<CreatedAtRoute<AlertRuleDto>, ValidationProblem>> CreateAsync(
        AlertRuleRequest request, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        if (await ValidateMeterAsync(request, db, cancellationToken) is { } problem)
        {
            return problem;
        }

        var rule = new AlertRule { Name = request.Name };
        Apply(request, rule);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.CreatedAtRoute(ToDto(rule), "GetAlertRule", new { id = rule.Id, version = "1" });
    }

    internal static async Task<Results<NoContent, NotFound, ValidationProblem>> UpdateAsync(
        int id, AlertRuleRequest request, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        if (await ValidateMeterAsync(request, db, cancellationToken) is { } problem)
        {
            return problem;
        }

        var rule = await db.AlertRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        Apply(request, rule);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal static async Task<Results<NoContent, NotFound>> DeleteAsync(int id, GridPulseDbContext db, CancellationToken cancellationToken) =>
        await db.AlertRules.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? TypedResults.NotFound()
            : TypedResults.NoContent();

    private static async Task<ValidationProblem?> ValidateMeterAsync(AlertRuleRequest request, GridPulseDbContext db, CancellationToken cancellationToken) =>
        request.MeterId is { } meterId && !await db.Meters.AnyAsync(m => m.Id == meterId, cancellationToken)
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MeterId)] = [$"Meter {meterId} does not exist."] })
            : null;

    private static void Apply(AlertRuleRequest request, AlertRule rule)
    {
        rule.Name = request.Name;
        rule.Type = request.Type;
        rule.MeterId = request.MeterId;
        rule.ThresholdW = request.Type == AlertRuleType.Threshold ? request.ThresholdW : null;
        rule.Aggregate = request.Aggregate;
        rule.OfflineMinutes = request.Type == AlertRuleType.Offline ? request.OfflineMinutes : null;
        rule.Enabled = request.Enabled;
    }

    private static AlertRuleDto ToDto(AlertRule r) =>
        new(r.Id, r.Name, r.Type, r.MeterId, r.ThresholdW, r.Aggregate, r.OfflineMinutes, r.Enabled);
}
