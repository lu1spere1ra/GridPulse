using Microsoft.AspNetCore.OutputCaching;

namespace GridPulse.Api.Hosting;

internal static class OutputCacheExtensions
{
    public const string HistoricalReadings = "historical-readings";
    public const string Analytics = "analytics";

    public static string MeterTag(int meterId) => $"meter:{meterId}";

    public static IServiceCollection AddGridPulseOutputCache(this IServiceCollection services)
    {
        services.AddOutputCache(options =>
        {
            options.AddPolicy(HistoricalReadings, new HistoricalRangePolicy(TimeSpan.FromMinutes(10)));
            options.AddPolicy(Analytics, new HistoricalRangePolicy(TimeSpan.FromMinutes(5)));
        });
        return services;
    }

    /// <summary>
    /// Caches range queries only when the range is entirely in the past: a range that reaches
    /// into the last minute is still changing, so caching it would show stale data on a live
    /// dashboard. Entries vary by query string and are tagged per meter so updates can evict them.
    /// </summary>
    private sealed class HistoricalRangePolicy(TimeSpan duration) : IOutputCachePolicy
    {
        private static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(1);

        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            var request = context.HttpContext.Request;
            var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var settled = DateTimeOffset.TryParse(request.Query["to"], System.Globalization.CultureInfo.InvariantCulture, out var to)
                && to <= now - SettleTime;

            context.EnableOutputCaching = true;
            context.AllowCacheLookup = settled && HttpMethods.IsGet(request.Method);
            context.AllowCacheStorage = context.AllowCacheLookup;
            context.AllowLocking = true;
            context.ResponseExpirationTimeSpan = duration;
            context.CacheVaryByRules.QueryKeys = "*";

            if (request.RouteValues.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var meterId))
            {
                context.Tags.Add(MeterTag(meterId));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
            {
                context.AllowCacheStorage = false;
            }

            return ValueTask.CompletedTask;
        }
    }
}
