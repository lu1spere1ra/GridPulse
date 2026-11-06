using System.Collections.Concurrent;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Ingestion.Pipeline;

namespace GridPulse.UnitTests.Fakes;

public sealed class InMemoryMeterRegistry : IMeterRegistry
{
    private readonly Dictionary<string, int> _ids;

    public InMemoryMeterRegistry(int count) =>
        _ids = Enumerable.Range(1, count).ToDictionary(i => $"MTR-{i:D6}", i => i);

    public int Count => _ids.Count;

    public IReadOnlyCollection<int> MeterIds => _ids.Values;

    public bool TryResolve(string serial, out int meterId) => _ids.TryGetValue(serial, out meterId);

    public string? GetSerial(int meterId) => $"MTR-{meterId:D6}";
}

/// <summary>Records batches; can be gated to simulate a slow or blocked database.</summary>
public sealed class RecordingBatchWriter : IReadingBatchWriter
{
    private TaskCompletionSource _gate = CompletedGate();

    public ConcurrentQueue<Reading[]> Batches { get; } = new();

    public int Total => Batches.Sum(b => b.Length);

    public void Block() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate.TrySetResult();

    public async Task WriteAsync(IReadOnlyList<Reading> batch, CancellationToken cancellationToken)
    {
        await _gate.Task;
        Batches.Enqueue([.. batch]);
    }

    private static TaskCompletionSource CompletedGate()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }
}

public sealed class RecordingLivePublisher : ILivePublisher
{
    public ConcurrentQueue<FleetSnapshot> Fleet { get; } = new();

    public ConcurrentQueue<WindowClosed> Windows { get; } = new();

    public Task PublishFleetAsync(FleetSnapshot snapshot, CancellationToken cancellationToken)
    {
        Fleet.Enqueue(snapshot);
        return Task.CompletedTask;
    }

    public Task PublishLatestAsync(MeterLatestBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishWindowsAsync(IReadOnlyList<WindowClosed> windows, CancellationToken cancellationToken)
    {
        foreach (var window in windows)
        {
            Windows.Enqueue(window);
        }

        return Task.CompletedTask;
    }
}
