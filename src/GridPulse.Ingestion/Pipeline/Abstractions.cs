using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;

namespace GridPulse.Ingestion.Pipeline;

/// <summary>Maps meter serial numbers to database ids. Must be safe for concurrent reads.</summary>
public interface IMeterRegistry
{
    int Count { get; }

    bool TryResolve(string serial, out int meterId);

    string? GetSerial(int meterId);
}

/// <summary>Persists a batch of readings. Implementations must be idempotent (QoS 1 redeliveries).</summary>
public interface IReadingBatchWriter
{
    Task WriteAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken);
}

/// <summary>Pushes live data to dashboards. Called from a background loop, never from the hot path.</summary>
public interface ILivePublisher
{
    Task PublishFleetAsync(FleetSnapshot snapshot, CancellationToken cancellationToken);

    Task PublishLatestAsync(MeterLatestBatch batch, CancellationToken cancellationToken);

    Task PublishWindowsAsync(IReadOnlyList<WindowClosed> windows, CancellationToken cancellationToken);
}
