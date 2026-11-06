using BenchmarkDotNet.Attributes;
using GridPulse.Core.Readings;
using GridPulse.Ingestion;
using GridPulse.Ingestion.Pipeline;

namespace GridPulse.Benchmarks;

/// <summary>
/// The single-threaded processor stage: ordering guard + two window aggregators + live state.
/// One invocation = one reading for each of 5,000 meters (one second of the target load);
/// results are reported per reading. Windows close every 60 invocations, so their cost is amortised.
/// </summary>
[MemoryDiagnoser]
public class ProcessorBenchmarks
{
    private const int Meters = 5_000;
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private ReadingProcessor _processor = null!;
    private int _second;

    /// <summary>Warm up once so the benchmark measures steady state, not dictionary growth.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _processor = new ReadingProcessor(new IngestionOptions(), new FixedMeterRegistry(Meters), new IngestionMetrics(new NoopMeterFactory()));
        _second = 0;
        ProcessOneSecondOfFleet();
    }

    [Benchmark(OperationsPerInvoke = Meters)]
    public void ProcessOneSecondOfFleet()
    {
        var ts = T0.AddSeconds(_second++);
        for (var meter = 1; meter <= Meters; meter++)
        {
            _processor.Process(new Reading(meter, ts, 1_000, _second, 230));
        }
    }
}
