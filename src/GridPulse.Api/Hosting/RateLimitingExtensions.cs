using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace GridPulse.Api.Hosting;

internal static class RateLimitingExtensions
{
    public const string Reads = "reads";
    public const string Writes = "writes";

    /// <summary>
    /// Per-client-IP limits. Reads use a fixed window (cheap, predictable); writes use a token
    /// bucket, which allows short bursts (e.g. bulk meter registration) but caps the sustained rate.
    /// Limits come from configuration so tests and environments can tune them.
    /// </summary>
    public static IServiceCollection AddGridPulseRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitSettings>().BindConfiguration("RateLimiting");
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(Reads, context =>
            {
                var settings = Settings(context);
                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.ReadsPerWindow,
                    Window = settings.Window,
                    QueueLimit = 0,
                });
            });

            options.AddPolicy(Writes, context =>
            {
                var settings = Settings(context);
                return RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.WriteBurst,
                    TokensPerPeriod = settings.WritesPerWindow,
                    ReplenishmentPeriod = settings.Window,
                    QueueLimit = 0,
                });
            });
        });
        return services;
    }

    private static RateLimitSettings Settings(HttpContext context) =>
        context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitSettings>>().Value;

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

internal sealed class RateLimitSettings
{
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    public int ReadsPerWindow { get; set; } = 200;

    public int WritesPerWindow { get; set; } = 20;

    public int WriteBurst { get; set; } = 40;
}
