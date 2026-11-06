using BenchmarkDotNet.Attributes;
using GridPulse.Core.Contracts;
using GridPulse.Core.Readings;
using GridPulse.Ingestion;
using GridPulse.Ingestion.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GridPulse.Benchmarks;

/// <summary>
/// End-to-end in-memory throughput: validate → input channel → processor → writer channel →
/// batching → (no-op) writer. Excludes MQTT and the database, which are measured separately.
/// </summary>
[MemoryDiagnoser]
public class PipelineThroughputBenchmarks
{
    private const int Meters = 5_000;
    private const int Readings = 200_000;
    private ReadingMessage[] _messages = null!;

    [GlobalSetup]
    public void Setup()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        _messages = Enumerable.Range(0, Readings)
            .Select(i => new ReadingMessage($"MTR-{(i % Meters) + 1:D6}", start.AddMilliseconds(i / Meters * 1_000), 1_000, i, 230))
            .ToArray();
    }

    [Benchmark(OperationsPerInvoke = Readings)]
    public async Task SubmitAndDrain()
    {
        var writer = new CountingWriter();
        var pipeline = new ReadingPipeline(
            Options.Create(new IngestionOptions()),
            Options.Create(new ReadingValidationOptions()),
            new FixedMeterRegistry(Meters),
            writer,
            new NoopLivePublisher(),
            new IngestionMetrics(new NoopMeterFactory()),
            TimeProvider.System,
            NullLogger<ReadingPipeline>.Instance,
            []);

        await pipeline.StartAsync(CancellationToken.None);
        foreach (var message in _messages)
        {
            await pipeline.SubmitAsync(message, CancellationToken.None);
        }

        await pipeline.StopAsync(CancellationToken.None);
        if (writer.Count != Readings)
        {
            throw new InvalidOperationException($"Expected {Readings} persisted readings, got {writer.Count}.");
        }
    }
}
