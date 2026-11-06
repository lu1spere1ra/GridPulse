using GridPulse.Api.Hosting;
using GridPulse.Core.Contracts;
using GridPulse.Core.Meters;
using GridPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

namespace GridPulse.Api.Endpoints;

public static class MeterEndpoints
{
    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder routes)
    {
        var meters = routes.MapGroup("/meters").WithTags("Meters");

        meters.MapGet("/", ListAsync).RequireRateLimiting(RateLimitingExtensions.Reads);
        meters.MapGet("/{id:int}", GetAsync).WithName("GetMeter").RequireRateLimiting(RateLimitingExtensions.Reads);
        meters.MapPost("/", CreateAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        meters.MapPost("/batch", RegisterBatchAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        meters.MapPut("/{id:int}", UpdateAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        meters.MapDelete("/{id:int}", DeleteAsync).RequireRateLimiting(RateLimitingExtensions.Writes);
        return routes;
    }

    internal static async Task<Ok<PagedResult<MeterDto>>> ListAsync(
        GridPulseDbContext db, CancellationToken cancellationToken, int page = 1, int pageSize = 50, string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = db.Meters.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(m => EF.Functions.ILike(m.Serial, pattern)
                || (m.Name != null && EF.Functions.ILike(m.Name, pattern))
                || (m.Zone != null && EF.Functions.ILike(m.Zone, pattern)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(m => m.Serial)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MeterDto(m.Id, m.Serial, m.Name, m.Zone, m.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResult<MeterDto>(items, page, pageSize, total));
    }

    internal static async Task<Results<Ok<MeterDto>, NotFound>> GetAsync(int id, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        var meter = await db.Meters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return meter is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(meter));
    }

    internal static async Task<Results<CreatedAtRoute<MeterDto>, ProblemHttpResult>> CreateAsync(
        CreateMeterRequest request, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Meters.AnyAsync(m => m.Serial == request.Serial, cancellationToken))
        {
            return TypedResults.Problem(
                title: "Meter already exists",
                detail: $"A meter with serial '{request.Serial}' is already registered.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var meter = new Meter { Serial = request.Serial, Name = request.Name, Zone = request.Zone };
        db.Meters.Add(meter);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.CreatedAtRoute(ToDto(meter), "GetMeter", new { id = meter.Id, version = "1" });
    }

    /// <summary>
    /// Idempotent bulk registration used by device provisioning (the simulator calls it at start-up).
    /// A single <c>INSERT … SELECT unnest(…) ON CONFLICT DO NOTHING</c> instead of N EF Core inserts.
    /// </summary>
    internal static async Task<Results<Ok<RegisterMetersResponse>, ValidationProblem>> RegisterBatchAsync(
        RegisterMetersRequest request, GridPulseDbContext db, CancellationToken cancellationToken)
    {
        var serials = request.Serials.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToArray();
        if (serials.Length is 0 or > 5_000 || serials.Any(s => s.Length > 32))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Serials)] = ["Provide between 1 and 5,000 serials of at most 32 characters."],
            });
        }

        var created = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO meters (serial) SELECT unnest({serials}) ON CONFLICT (serial) DO NOTHING",
            cancellationToken);
        return TypedResults.Ok(new RegisterMetersResponse(serials.Length, created));
    }

    internal static async Task<Results<NoContent, NotFound>> UpdateAsync(
        int id, UpdateMeterRequest request, GridPulseDbContext db, IOutputCacheStore cache, CancellationToken cancellationToken)
    {
        var updated = await db.Meters
            .Where(m => m.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.Name, request.Name).SetProperty(m => m.Zone, request.Zone), cancellationToken);
        if (updated == 0)
        {
            return TypedResults.NotFound();
        }

        await cache.EvictByTagAsync(OutputCacheExtensions.MeterTag(id), cancellationToken);
        return TypedResults.NoContent();
    }

    internal static async Task<Results<NoContent, NotFound>> DeleteAsync(
        int id, GridPulseDbContext db, IOutputCacheStore cache, CancellationToken cancellationToken)
    {
        var deleted = await db.Meters.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            return TypedResults.NotFound();
        }

        await cache.EvictByTagAsync(OutputCacheExtensions.MeterTag(id), cancellationToken);
        return TypedResults.NoContent();
    }

    private static MeterDto ToDto(Meter meter) => new(meter.Id, meter.Serial, meter.Name, meter.Zone, meter.CreatedAt);
}
