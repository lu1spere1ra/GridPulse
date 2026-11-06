using GridPulse.Core.Contracts;

namespace GridPulse.Ingestion.Pipeline;

/// <summary>Pushes live data to dashboards. Called from a background loop, never from the hot path.</summary>
public interface ILivePublisher
{
    Task PublishFleetAsync(FleetSnapshot snapshot, CancellationToken cancellationToken);

    Task PublishLatestAsync(MeterLatestBatch batch, CancellationToken cancellationToken);

    Task PublishWindowsAsync(IReadOnlyList<WindowClosed> windows, CancellationToken cancellationToken);
}
