using System.Diagnostics.Metrics;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Ingestion.Pipeline;

namespace GridPulse.Benchmarks;

internal sealed class NoopMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);

    public void Dispose()
    {
    }
}

internal sealed class FixedMeterRegistry(int count) : IMeterRegistry
{
    private readonly Dictionary<string, int> _ids =
        Enumerable.Range(1, count).ToDictionary(i => $"MTR-{i:D6}", i => i);

    public int Count => _ids.Count;

    public IReadOnlyCollection<int> MeterIds => _ids.Values;

    public bool TryResolve(string serial, out int meterId) => _ids.TryGetValue(serial, out meterId);

    public string? GetSerial(int meterId) => $"MTR-{meterId:D6}";
}

internal sealed class CountingWriter : IReadingBatchWriter
{
    private long _count;

    public long Count => Interlocked.Read(ref _count);

    public Task WriteAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        Interlocked.Add(ref _count, batch.Count);
        return Task.CompletedTask;
    }
}

internal sealed class NoopLivePublisher : ILivePublisher
{
    public Task PublishFleetAsync(FleetSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishLatestAsync(MeterLatestBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishWindowsAsync(IReadOnlyList<WindowClosed> windows, CancellationToken cancellationToken) => Task.CompletedTask;
}
