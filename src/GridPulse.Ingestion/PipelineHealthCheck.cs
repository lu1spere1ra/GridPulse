using GridPulse.Ingestion.Pipeline;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GridPulse.Ingestion;

/// <summary>Not ready while the database keeps rejecting batches (readings are backing up).</summary>
public sealed class PipelineHealthCheck(ReadingPipeline pipeline) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(pipeline.IsWriterHealthy
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The batch writer is failing; readings are backing up."));
}
