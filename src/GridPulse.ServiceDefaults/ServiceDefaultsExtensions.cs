using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace GridPulse.ServiceDefaults;

/// <summary>
/// Conventions shared by every GridPulse host: OpenTelemetry metrics and health endpoints.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Health checks tagged with this are part of readiness but not liveness.</summary>
    public const string ReadyTag = "ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks();

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("GridPulse.*")
                .AddMeter("Npgsql")
                // The default OTel buckets (0, 5, 10, 25… seconds) put every sub-second value in the
                // first bucket, which makes percentiles meaningless. These match what we measure.
                .AddView("gridpulse.ingestion.latency", SecondsBuckets([0.01, 0.025, 0.05, 0.1, 0.25, 0.4, 0.5, 0.75, 1, 1.5, 2.5, 5, 10]))
                .AddView("gridpulse.ingestion.batch.duration", SecondsBuckets([0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5])));

        // Export only when an OTLP endpoint is configured (compose sets it; tests do not).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.WithMetrics(metrics => metrics.AddOtlpExporter());
        }

        return builder;
    }

    private static ExplicitBucketHistogramConfiguration SecondsBuckets(double[] boundaries) => new() { Boundaries = boundaries };

    /// <summary>
    /// <c>/health/live</c>: the process is up. <c>/health/ready</c>: dependencies are reachable.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = 200,
                [HealthStatus.Degraded] = 200,
                [HealthStatus.Unhealthy] = 503,
            },
        });
        return app;
    }
}
