namespace GridPulse.Core.Readings;

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
