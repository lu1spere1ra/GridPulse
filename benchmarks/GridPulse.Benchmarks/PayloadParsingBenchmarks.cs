using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using GridPulse.Core.Contracts;

namespace GridPulse.Benchmarks;

/// <summary>Cost of turning one MQTT payload into a <see cref="ReadingMessage"/>.</summary>
[MemoryDiagnoser]
public class PayloadParsingBenchmarks
{
    private static readonly JsonSerializerOptions ReflectionOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _payload = Encoding.UTF8.GetBytes(
        """{"meterId":"MTR-000123","ts":"2026-11-06T10:15:00.123+00:00","powerW":1234.5,"energyWh":5532100,"voltageV":231.2}""");

    [Benchmark(Baseline = true)]
    public ReadingMessage? Reflection() => JsonSerializer.Deserialize<ReadingMessage>(_payload, ReflectionOptions);

    [Benchmark]
    public ReadingMessage? SourceGenerated() => JsonSerializer.Deserialize(_payload, GridPulseJsonContext.Default.ReadingMessage);
}
